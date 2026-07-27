# TAE Exam & Submission Analysis System — Design

## 1. Purpose

A self-hosted system for practicing the ISTQB CTAL-TAE (Advanced Level Test Automation Engineer) certification exam, built around a question bank imported from 6 existing standalone mock-exam HTML files. It supports:

1. Importing the 240 questions embedded in the 6 HTML files into a real database.
2. Taking any exam an unlimited number of times, with every attempt recorded.
3. Analyzing accumulated results per syllabus chapter/topic and using that analysis to assemble new exam papers (rule-based recombination of the question bank — not AI-generated question text).
4. Generating a smaller "targeted practice" set from a specific past attempt's wrong answers.

Stack: **Node.js/Express** (thin static host + API proxy) for the frontend, **C# ASP.NET Core Web API + EF Core** for the backend, **MySQL** (`taeExam` database on `localhost`) for storage.

## 2. Architecture

```
Browser
  │  HTTP (HTML/CSS/vanilla JS, fetch())
  ▼
frontend/  (Express, port 3000)
  │  static file host for public/*.html, css/, js/
  │  /api/* requests are forwarded as-is to the backend (no logic here)
  ▼
backend/TaeExam.Api/  (ASP.NET Core Minimal API, port 5017)
  │  all scoring, generation algorithms, and persistence live here
  ▼
MySQL — database `taeExam` on localhost
```

The frontend holds **no business logic or scoring** — it renders whatever the backend returns and forwards user actions back as API calls. This matters for exam integrity: the backend never sends `correctIndexes`/`explanation` for a question until after that attempt is submitted, so the browser has no way to leak answers early even if someone inspects the page source or network tab before submitting.

## 3. Data pipeline (one-time ETL)

`tools/extract-questions.js` is a standalone Node script (no dependencies beyond the `vm` core module) that reads the 6 original HTML files, locates the embedded `const Qs = [...]` / `const ALL_QS = [...]` array literal via bracket-matching (string-literal aware, so quoted text can't throw off the bracket count), and evaluates it with `vm.runInNewContext` — safe here because the files are trusted local static assets, not untrusted input.

The 6 files turned out to use **3 slightly different schemas** (single vs. array `ans`, `pts` present or absent, `scen`/`app`/`topic`/`level` present in different subsets). The script normalizes all of them into one shape and writes three files under `seed/`:

- **`questions.json`** — 240 unified question records: `{ legacyId, sourceFile, chapter, topic, level, isMultiChoice, isScenario, scenarioText, questionText, options[], correctIndexes[], distractorDesign, explanation, points }`. Where a source file had no explicit `pts`, points default to `isScenario ? 2 : 1`.
- **`syllabus_chapters.json`** — the 8 examinable chapters of ISTQB CTAL-TAE Syllabus v2.0 (title, study minutes, K-level), hand-transcribed from the PDF's table of contents. The `Ch1..Ch8` tags already used in the source files line up exactly with this structure, so no chapter renumbering was needed.
- **`imported_exams.json`** — one entry per source file, preserving that file's own original total points and pass mark (extracted from its scoring script, since the 6 files don't share one pass-percentage rule — e.g. 65% vs. 80%), plus the exact question order.

Re-running the script is safe/idempotent; it only needs to happen again if the source HTML files change.

## 4. Backend

### 4.1 Data model (EF Core entities)

| Entity | Purpose |
|---|---|
| `SyllabusChapter` | The 8 reference chapters (code, title, study minutes, K-level). |
| `Question` | One normalized question. `Options`/`CorrectIndexes` are `List<string>`/`List<int>` stored as JSON columns via EF value converters. |
| `Exam` | An assembled paper: `Type` is `Imported`, `Generated`, or `Drill`. Generated exams store the blueprint used (`BlueprintJson`) for auditability; drills store which `Attempt` they were generated from. |
| `ExamQuestion` | Join row: which questions are on an exam, in what order, with a `PointsOverride` (usually the question's own default, but frozen per-exam so later edits to the bank don't retroactively change historical exams). |
| `Attempt` | One sitting of an exam: score, max score, percent, pass/fail, status (`InProgress`/`Submitted`). |
| `AttemptAnswer` | One question's submitted answer within an attempt: selected indexes, correctness, points awarded. |

### 4.2 Seeding

On startup (`DbSeeder.cs`), if the `Questions` table is empty, the backend reads the three `seed/*.json` files and populates `SyllabusChapters` → `Questions` → 6 `Exam` rows (`Type=Imported`) with their original question order and point totals. This makes the 6 original exams immediately playable through the new system without any manual setup.

### 4.3 Scoring (never trust the client)

`POST /api/attempts/{id}/submit` is the only place where correctness is decided. The client sends only `{ examQuestionId, selectedIndexes }` pairs; the server looks up each question's stored `CorrectIndexes` and compares as a **set** (order-independent, supports multi-select). `GET /api/exams/{id}` (the pre-submission paper) never includes `correctIndexes` or `explanation` in its response — it's a separate DTO projection, not just a client-side hide.

### 4.4 Paper generation (`PaperGenerationService`)

Used by `POST /api/exams/generate` (requirement #3 — new papers based on the syllabus/analysis):

1. **Base blueprint** — the live per-chapter share of the question bank (e.g. Ch3 "Test Automation Architecture" naturally gets the largest share, since it's both the biggest chapter in the syllabus and the best-represented in the imported bank).
2. **Weak-chapter boost** (optional, on by default) — chapters where the user's aggregate accuracy (from all submitted attempts) is below the overall average get their share multiplied by a boost factor; chapters with no attempt history yet are treated as exactly average (neutral). Fractions are renormalized to sum to 1.
3. **Target counts** — largest-remainder method turns fractions into exact integer question counts summing to the requested total, clamped to what's actually available per chapter.
4. **Selection** — prefers questions not used in the last N exams (repeat-avoidance); falls back to reuse only if a chapter's fresh pool runs short (recorded in a `warnings` list returned to the caller).
5. Final question order is shuffled (not grouped by chapter) before the exam is persisted.

### 4.5 Targeted drill (`DrillGenerationService`)

Used by `POST /api/exams/drill/{attemptId}` (requirement #4):

1. **Core set** = every question in that attempt that was answered wrong or left blank.
2. **Fill pool** = other bank questions sharing a touched chapter (and topic, when available) that were **not** on the original exam.
3. Fill candidates are ranked same-chapter+topic first, then same-chapter-only, preferring scenario/application-style questions, and used to pad the drill up to a target size (default: `2× the wrong-answer count`, clamped to 10–40).
4. The response includes a `weakAreaSummary` (per-chapter accuracy from that one attempt) that drives the drill page's banner text.

### 4.6 REST API surface

`GET /api/syllabus-chapters`, `GET /api/exams`, `GET /api/exams/{id}`, `POST /api/exams/generate`, `POST /api/exams/drill/{attemptId}`, `POST /api/attempts`, `POST /api/attempts/{id}/submit`, `GET /api/attempts`, `GET /api/attempts/{id}`, `GET /api/analysis/overview`.

## 5. Frontend

Plain Express (`frontend/server.js`) serves static files from `public/` and forwards any `/api/*` request byte-for-byte to the backend (`BACKEND_URL` env var) — it holds no logic of its own.

| Page | Role |
|---|---|
| `index.html` | Dashboard: exam list + quick stats (attempts, avg score, pass rate, weakest chapter). |
| `exam.html?examId=` | Take-exam flow. Three tabs (Exam paper / Answer key / Results), mirroring the tab-bar UI already prototyped in the original 6 files. Answer key and Results stay hidden until the submit response arrives. |
| `history.html` | Past attempts, with a link into each attempt's review and a "Targeted practice" link per submitted attempt. |
| `attempt.html?attemptId=` | Full review of one past attempt (score, chapter breakdown, per-question answer key). |
| `analysis.html` | Per-chapter accuracy bars, per-topic accuracy table, trend table — plain CSS/HTML, no charting library. |
| `generate.html` | Form for requirement #3 (question count, weak-chapter boost toggle) → redirects into the new exam. |
| `drill.html?attemptId=` | Interstitial for requirement #4: shows the weak-area banner/metrics from `DrillGenerationService`, then hands off into the normal take-exam flow. |

`css/shared.css` and `js/api.js` are shared across all pages — the CSS classes (`.q-block`, `.badge`, `.tab-bar`, `.ans-card`, `.weak-banner`, `.metric-grid`, etc.) are carried over from the original files' inline styles, redefined against this project's own light/dark CSS variables (the originals relied on an artifact-preview host's injected variables that don't exist in a plain browser).

## 6. Configuration

| Setting | Location | Current value |
|---|---|---|
| DB connection | `backend/TaeExam.Api/appsettings.json` → `ConnectionStrings:Default` | `Server=localhost;Database=taeExam;User=root;Password=***;` (MySQL, via Pomelo.EntityFrameworkCore.MySql) |
| Seed data path | `Program.cs` (`SeedDataPath` config key, falls back to `<repo root>/seed`) | `seed/*.json` |
| Backend port | `Properties/launchSettings.json` / `--urls` | `http://localhost:5017` |
| Frontend port / backend target | `frontend/server.js` env vars | `PORT` (default 3000), `BACKEND_URL` (default `http://localhost:5017`) |

## 7. Running locally

```bash
# 1. (Re)build the question bank from the source HTML files — only needed if they change
node tools/extract-questions.js

# 2. Backend — creates/updates the MySQL schema, then seeds on first run if empty
cd backend/TaeExam.Api
dotnet ef database update
dotnet run --urls http://localhost:5017

# 3. Frontend
cd frontend
npm install
BACKEND_URL=http://localhost:5017 npm start
# open http://localhost:3000
```

## 8. Known simplifications (v1)

- Single implicit user — no auth/login, all attempts are global (fine for a personal study tool).
- `MySqlConnector`/Pomelo used with `ServerVersion.AutoDetect`, so the backend must be able to reach MySQL at startup.
- Generated/drill exams' pass threshold is a fixed 65% of total points (matching the strictest of the 6 imported exams) rather than being configurable per request.
