// One-time ETL: extract the embedded question arrays from the 6 original CTAL-TAE
// mock-exam HTML files into normalized JSON the C# backend can seed from.
//
// Safe to re-run any time the source HTML files change; output is deterministic.
'use strict';

const fs = require('fs');
const path = require('path');
const vm = require('vm');

const ROOT = path.resolve(__dirname, '..');
const SEED_DIR = path.join(ROOT, 'seed');

const DEFAULT_SCENARIO_POINTS = 2;
const DEFAULT_STANDARD_POINTS = 1;

const SOURCE_FILES = [
  'CTAL_TAE_exam_6_66pt_2016_structure.html',
  'CTAL_TAE_mock_exam1_40q.html',
  'CTAL_TAE_mock_exam3_v2.html',
  'CTAL_TAE_mock_exam4.html',
  'CTAL_TAE_mock_exam_v5_official_style.html',
  'TAE_targeted_drill_example2.html',
];

const SYLLABUS_CHAPTERS = [
  { code: 'Ch1', number: 1, title: 'Introduction and Objectives for Test Automation', studyMinutes: 45, kLevel: 'K2' },
  { code: 'Ch2', number: 2, title: 'Preparing for Test Automation', studyMinutes: 180, kLevel: 'K4' },
  { code: 'Ch3', number: 3, title: 'Test Automation Architecture', studyMinutes: 210, kLevel: 'K3' },
  { code: 'Ch4', number: 4, title: 'Implementing Test Automation', studyMinutes: 150, kLevel: 'K4' },
  { code: 'Ch5', number: 5, title: 'Implementation and Deployment Strategies for Test Automation', studyMinutes: 90, kLevel: 'K3' },
  { code: 'Ch6', number: 6, title: 'Test Automation Reporting and Metrics', studyMinutes: 150, kLevel: 'K4' },
  { code: 'Ch7', number: 7, title: 'Verifying the Test Automation Solution', studyMinutes: 135, kLevel: 'K3' },
  { code: 'Ch8', number: 8, title: 'Continuous Improvement', studyMinutes: 210, kLevel: 'K4' },
];

/** Find `const Qs = [` or `const ALL_QS = [` and bracket-match forward to its closing `]`,
 * string-literal aware so brackets inside quoted text don't break the count. */
function extractArrayLiteral(src) {
  const startMatch = src.match(/const\s+(?:Qs|ALL_QS)\s*=\s*\[/);
  if (!startMatch) throw new Error('Could not locate question array declaration');
  const openBracketIndex = startMatch.index + startMatch[0].length - 1;

  let depth = 0;
  let inString = null; // null | "'" | '"'
  let i = openBracketIndex;
  for (; i < src.length; i++) {
    const c = src[i];
    if (inString) {
      if (c === '\\') { i++; continue; } // skip escaped char
      if (c === inString) inString = null;
      continue;
    }
    if (c === "'" || c === '"') { inString = c; continue; }
    if (c === '[') depth++;
    else if (c === ']') {
      depth--;
      if (depth === 0) return src.slice(openBracketIndex, i + 1);
    }
  }
  throw new Error('Unbalanced brackets while extracting question array');
}

function extractPassPoints(src) {
  let m = src.match(/PASS_PTS\s*=\s*(\d+)/);
  if (m) return { passPoints: Number(m[1]), mode: 'literal' };
  m = src.match(/score\s*>=\s*(\d+)/);
  if (m) return { passPoints: Number(m[1]), mode: 'literal' };
  m = src.match(/PASS_PTS\s*=\s*Math\.ceil\(TOTAL_PTS\s*\*\s*([\d.]+)\)/);
  if (m) return { passPoints: null, mode: 'fraction', fraction: Number(m[1]) };
  throw new Error('Could not locate a pass-mark rule in source');
}

function normalizeQuestion(raw, sourceFile, importOrder) {
  const isScenario = Boolean(raw.scen ?? raw.app ?? false);
  const correctIndexes = Array.isArray(raw.ans) ? raw.ans.slice() : [raw.ans];
  const isMultiChoice = Array.isArray(raw.ans) && (raw.multi === true || raw.ans.length > 1);
  const points = typeof raw.pts === 'number' ? raw.pts : (isScenario ? DEFAULT_SCENARIO_POINTS : DEFAULT_STANDARD_POINTS);

  return {
    importOrder,
    legacyId: raw.id,
    sourceFile,
    chapter: raw.ch,
    topic: raw.topic ?? null,
    level: raw.level ?? null,
    isMultiChoice,
    isScenario,
    scenarioText: raw.scenario ?? null,
    questionText: raw.q,
    options: raw.opts,
    correctIndexes,
    distractorDesign: raw.dd ?? null,
    explanation: raw.expl,
    points,
  };
}

function main() {
  const allQuestions = [];
  const importedExams = [];
  let importOrder = 0;

  for (const fileName of SOURCE_FILES) {
    const filePath = path.join(ROOT, fileName);
    const src = fs.readFileSync(filePath, 'utf8');

    const arrayLiteralSrc = extractArrayLiteral(src);
    const rawQuestions = vm.runInNewContext('(' + arrayLiteralSrc + ')', {});

    const normalized = rawQuestions.map((raw) => normalizeQuestion(raw, fileName, importOrder++));
    allQuestions.push(...normalized);

    const totalPoints = normalized.reduce((sum, q) => sum + q.points, 0);
    const passInfo = extractPassPoints(src);
    const passThresholdPoints = passInfo.mode === 'literal'
      ? passInfo.passPoints
      : Math.ceil(totalPoints * passInfo.fraction);

    importedExams.push({
      sourceFile: fileName,
      title: fileName.replace(/\.html$/, '').replace(/_/g, ' '),
      totalPoints,
      passThresholdPoints,
      questionLegacyIdsInOrder: rawQuestions.map((raw) => raw.id),
    });
  }

  fs.mkdirSync(SEED_DIR, { recursive: true });
  fs.writeFileSync(path.join(SEED_DIR, 'questions.json'), JSON.stringify(allQuestions, null, 2));
  fs.writeFileSync(path.join(SEED_DIR, 'syllabus_chapters.json'), JSON.stringify(SYLLABUS_CHAPTERS, null, 2));
  fs.writeFileSync(path.join(SEED_DIR, 'imported_exams.json'), JSON.stringify(importedExams, null, 2));

  // Sanity summary
  const byChapter = {};
  for (const q of allQuestions) byChapter[q.chapter] = (byChapter[q.chapter] || 0) + 1;
  console.log(`Extracted ${allQuestions.length} questions from ${SOURCE_FILES.length} files.`);
  console.log('Per-chapter counts:', byChapter);
  console.log('Imported exams:', importedExams.map((e) => `${e.sourceFile}: ${e.totalPoints}pts, pass=${e.passThresholdPoints}`).join('\n  '));
}

main();
