using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TaeExam.Api.Data;
using TaeExam.Api.Dtos;
using UglyToad.PdfPig;

namespace TaeExam.Api.Services;

public class ExamImportParseException(string message) : Exception(message);

internal record ParsedQuestionStem(int Number, int PointsFromHeader, string QuestionText, List<string> Options, int ExpectedCorrectCount);

internal record AnswerKeyEntry(List<int> CorrectIndexes, string? Chapter, string? Level, int Points);

public class ExamImportParsingService(AppDbContext db)
{
    // Repeating page header/footer boilerplate that can land mid-question across a page break —
    // stripped wholesale before slicing question/option/explanation text out of the raw extraction.
    // Covers multiple known ISTQB sample-exam template revisions (e.g. v2.x "Advanced Level Test
    // Automation Engineering (TAE)" vs. the older v1.3 "Test Automation Engineering, Specialist" cover
    // line, and "2026/05/12" vs. "April 12, 2024" date formats), since the exact wording/layout drifts
    // between editions.
    private static readonly string[] BoilerplatePatterns =
    [
        @"Advanced Level Test Automation Engineering \(TAE\)",
        @"Test Automation Engineering,?\s*Specialist",
        @"Sample Exam set [A-Z]",
        @"Sample Exam\s*[–-]\s*Questions",
        @"Sample Exam\s*[–-]\s*Answers",
        @"Version [\d.]+\s*(?:of sample exam [A-Z])?\s*Page \d+ of \d+\s*(?:\d{4}/\d{2}/\d{2}|[A-Za-z]+ \d{1,2},\s*\d{4})",
        @"©\s*International Software Testing Qualifications Board",
        @"^\s*Question\s*$",
        @"Question\s*\r?\n?\s*Number\s*\r?\n?\s*\(#\)",
        @"Explanation\s*/\s*Rationale\s*Learning\s*\r?\n?\s*Objective\s*\r?\n?\s*\(LO\)\s*K-Level\s*Number\s*\r?\n?\s*of\s*\r?\n?\s*Points",
        @"Correct Answers?",
        @"\bAnswers\b",
        @"\bAnswer Key\b",
    ];

    private static readonly Dictionary<string, int> CountWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ONE"] = 1,
        ["TWO"] = 2,
        ["THREE"] = 3,
    };

    public async Task<ExamImportPreviewResponse> ParseAsync(Stream questionsPdf, Stream? answersPdf, CancellationToken ct = default)
    {
        string questionsText;
        try
        {
            questionsText = ExtractText(questionsPdf);
        }
        catch (Exception ex) when (ex is not ExamImportParseException)
        {
            throw new ExamImportParseException($"Could not read the questions PDF: {ex.Message}");
        }

        var stems = ParseQuestionsPdf(questionsText);

        Dictionary<int, AnswerKeyEntry>? answerKey = null;
        Dictionary<int, string>? rationale = null;
        if (answersPdf is not null)
        {
            string answersText;
            try
            {
                answersText = ExtractText(answersPdf);
            }
            catch (Exception ex) when (ex is not ExamImportParseException)
            {
                throw new ExamImportParseException($"Could not read the answers PDF: {ex.Message}");
            }
            answerKey = ParseAnswerSummaryTable(answersText);
            rationale = ParseAnswerRationale(answersText);
        }

        var warnings = new List<string>();
        var validChapterNumbers = (await db.SyllabusChapters.Select(c => c.Number).ToListAsync(ct)).ToHashSet();

        if (answerKey is not null && answerKey.Count != stems.Count)
        {
            warnings.Add($"The answer key has {answerKey.Count} entries but {stems.Count} questions were found in the questions PDF — unmatched questions will need chapter/answer entered manually.");
        }

        var questions = Merge(stems, answerKey, rationale, validChapterNumbers, warnings);
        return new ExamImportPreviewResponse(questions, warnings, answerKey is not null);
    }

    internal static string ExtractText(Stream pdfStream)
    {
        using var document = PdfDocument.Open(pdfStream);
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            sb.AppendLine(page.Text);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string StripBoilerplate(string text)
    {
        foreach (var pattern in BoilerplatePatterns)
        {
            text = Regex.Replace(text, pattern, " ", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        }
        return text;
    }

    private static string NormalizeWhitespace(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    internal static List<ParsedQuestionStem> ParseQuestionsPdf(string questionsFullText)
    {
        // "Points"/"points" casing differs between ISTQB template revisions (e.g. v2.x capitalizes it,
        // the older v1.3 template doesn't) — match case-insensitively throughout.
        var q1Matches = Regex.Matches(questionsFullText, @"Question #1 \(\d+ Points?\)", RegexOptions.IgnoreCase);
        if (q1Matches.Count == 0)
        {
            throw new ExamImportParseException("Could not find any 'Question #N (M Points)' headers — this doesn't look like the ISTQB CTAL-TAE Sample Exam Questions PDF.");
        }
        var bodyStartIndex = q1Matches[^1].Index;
        var bodyText = questionsFullText[bodyStartIndex..];

        var headerRegex = new Regex(@"Question #(\d+) \((\d+) Points?\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        var headers = headerRegex.Matches(bodyText).Cast<Match>().ToList();
        if (headers.Count == 0)
        {
            throw new ExamImportParseException("Could not find any 'Question #N (M Points)' headers in the exam body.");
        }

        // PdfPig's page.Text has no line breaks at all (a whole page is one continuous string),
        // so option markers cannot be found via ^-anchored/Multiline matching — instead find every
        // "x)" marker preceded by whitespace, then keep only a strictly sequential a,b,c,d[,e] run
        // (guards against stray "a)"-shaped text elsewhere in the stem being mistaken for an option).
        var rawOptionRegex = new Regex(@"(?<=\s)([a-e])\)\s+");
        var trailerRegex = new Regex(@"Select (ONE|TWO|THREE) options?\.", RegexOptions.IgnoreCase);

        var stems = new List<ParsedQuestionStem>();
        for (var i = 0; i < headers.Count; i++)
        {
            var blockStart = headers[i].Index + headers[i].Length;
            var blockEnd = i + 1 < headers.Count ? headers[i + 1].Index : bodyText.Length;
            var block = StripBoilerplate(bodyText[blockStart..blockEnd]);

            var trailerMatch = trailerRegex.Match(block);
            var expectedCount = trailerMatch.Success ? CountWords.GetValueOrDefault(trailerMatch.Groups[1].Value, 1) : 1;
            var contentBlock = trailerMatch.Success ? block[..trailerMatch.Index] : block;

            var candidateMatches = rawOptionRegex.Matches(contentBlock).Cast<Match>().ToList();
            var optionMatches = new List<Match>();
            var expectedLetter = 'a';
            foreach (var m in candidateMatches)
            {
                if (m.Groups[1].Value[0] != expectedLetter) continue;
                optionMatches.Add(m);
                expectedLetter++;
                if (expectedLetter > 'e') break;
            }
            var stemText = optionMatches.Count > 0 ? contentBlock[..optionMatches[0].Index] : contentBlock;

            var options = new List<string>();
            for (var oi = 0; oi < optionMatches.Count; oi++)
            {
                var optStart = optionMatches[oi].Index + optionMatches[oi].Length;
                var optEnd = oi + 1 < optionMatches.Count ? optionMatches[oi + 1].Index : contentBlock.Length;
                options.Add(NormalizeWhitespace(contentBlock[optStart..optEnd]));
            }

            stems.Add(new ParsedQuestionStem(
                int.Parse(headers[i].Groups[1].Value),
                int.Parse(headers[i].Groups[2].Value),
                NormalizeWhitespace(stemText),
                options,
                expectedCount));
        }

        return stems;
    }

    internal static Dictionary<int, AnswerKeyEntry> ParseAnswerSummaryTable(string answersFullText)
    {
        // "Answer Key" also appears in the ToC and the front-matter instructions before the real
        // section heading — the last occurrence is the real one (same reasoning as the Questions PDF's
        // ToC-vs-body collision for "Question #1").
        var answerKeyIdx = answersFullText.LastIndexOf("Answer Key", StringComparison.Ordinal);
        if (answerKeyIdx < 0)
        {
            throw new ExamImportParseException("Could not find the 'Answer Key' summary table in the answers PDF — this doesn't look like the ISTQB CTAL-TAE Sample Exam Answers PDF.");
        }
        var answersIdx = answersFullText.IndexOf("Answers", answerKeyIdx + "Answer Key".Length, StringComparison.Ordinal);
        var summaryBlock = answersIdx > answerKeyIdx
            ? answersFullText[answerKeyIdx..answersIdx]
            : answersFullText[answerKeyIdx..];

        // The LO prefix varies by ISTQB template edition — "TAE-1.1.1" in newer exams, "ALTA-E-1.1.1"
        // in the older (2016-syllabus-era) v1.3 exam — so match any letter/dash prefix ending in the
        // numeric "N.N.N" LO code, rather than hardcoding one prefix.
        var tupleRegex = new Regex(@"(\d{1,2})\s+([a-e](?:,\s*[a-e])*)\s+((?:[A-Za-z]+-)+\d+(?:\.\d+)+)\s+(K[234])\s+(\d)");
        var result = new Dictionary<int, AnswerKeyEntry>();
        foreach (Match m in tupleRegex.Matches(summaryBlock))
        {
            var qnum = int.Parse(m.Groups[1].Value);
            var correctIndexes = m.Groups[2].Value.Split(',').Select(l => l.Trim()[0] - 'a').ToList();
            var lo = m.Groups[3].Value; // e.g. "TAE-1.1.1" or "ALTA-E-1.1.1"
            string? chapter = null;
            var chapterNumMatch = Regex.Match(lo, @"(\d+)\.\d+\.\d+");
            if (chapterNumMatch.Success)
            {
                chapter = $"Ch{chapterNumMatch.Groups[1].Value}";
            }
            var level = m.Groups[4].Value;
            var points = int.Parse(m.Groups[5].Value);
            result[qnum] = new AnswerKeyEntry(correctIndexes, chapter, level, points);
        }
        return result;
    }

    internal static Dictionary<int, string> ParseAnswerRationale(string answersFullText)
    {
        // Must skip past the summary table the same way ParseAnswerSummaryTable does (LastIndexOf to
        // avoid the ToC/instructions mentions) — otherwise the anchor scan below would also pick up
        // the summary table's own "TAE-x.y.z K# n" rows and misalign every block.
        var answerKeyIdx = answersFullText.LastIndexOf("Answer Key", StringComparison.Ordinal);
        var searchFrom = answerKeyIdx >= 0 ? answerKeyIdx + "Answer Key".Length : 0;
        var answersIdx = answersFullText.IndexOf("Answers", searchFrom, StringComparison.Ordinal);
        if (answersIdx < 0)
        {
            return new Dictionary<int, string>();
        }
        var rationaleBlock = answersFullText[answersIdx..];

        // Same generalized LO-prefix pattern as ParseAnswerSummaryTable (varies by template edition).
        var anchorRegex = new Regex(@"(?:[A-Za-z]+-)+\d+(?:\.\d+)+\s+K[234]\s+\d");
        var anchors = anchorRegex.Matches(rationaleBlock).Cast<Match>().ToList();
        var prefixRegex = new Regex(@"^\s*(\d{1,2})\s+[a-e](?:,\s*[a-e])*\s+");

        var result = new Dictionary<int, string>();
        for (var i = 0; i < anchors.Count; i++)
        {
            var chunkStart = i == 0 ? 0 : anchors[i - 1].Index + anchors[i - 1].Length;
            var chunkEnd = anchors[i].Index;
            if (chunkEnd <= chunkStart) continue;
            // Strip boilerplate BEFORE testing the prefix — a chunk starting right after a page break
            // otherwise begins with repeated header/footer text instead of "{qnum} {letter(s)} ", which
            // would make the prefix match fail even though the real content is there.
            var chunk = StripBoilerplate(rationaleBlock[chunkStart..chunkEnd]).TrimStart();

            var prefixMatch = prefixRegex.Match(chunk);
            if (!prefixMatch.Success) continue; // best-effort: skip a block we can't cleanly anchor, leave blank

            var qnum = int.Parse(prefixMatch.Groups[1].Value);
            var explanationText = NormalizeWhitespace(chunk[prefixMatch.Length..]);
            if (explanationText.Length > 0)
            {
                result[qnum] = explanationText;
            }
        }
        return result;
    }

    private static List<DraftQuestionDto> Merge(
        List<ParsedQuestionStem> stems,
        Dictionary<int, AnswerKeyEntry>? answerKey,
        Dictionary<int, string>? rationale,
        HashSet<int> validChapterNumbers,
        List<string> warnings)
    {
        var results = new List<DraftQuestionDto>();
        foreach (var stem in stems)
        {
            var flags = new List<string>();
            AnswerKeyEntry? entry = null;

            if (answerKey is null)
            {
                flags.Add("NoAnswerKeyProvided");
            }
            else if (!answerKey.TryGetValue(stem.Number, out entry))
            {
                flags.Add("NoMatchingAnswerEntry");
            }

            string? chapter = entry?.Chapter;
            if (entry is not null && entry.Chapter is null)
            {
                flags.Add("ChapterNotDetected");
            }
            else if (chapter is not null)
            {
                var chapterNum = int.Parse(chapter[2..]);
                if (!validChapterNumbers.Contains(chapterNum))
                {
                    flags.Add("ChapterOutOfRange");
                    warnings.Add($"Question {stem.Number}: detected chapter Ch{chapterNum} is outside the known syllabus range — please pick a chapter manually.");
                    chapter = null;
                }
            }

            var correctIndexes = entry?.CorrectIndexes ?? [];
            if (correctIndexes.Count == 0)
            {
                flags.Add("CorrectAnswerNotDetected");
            }
            else if (correctIndexes.Count != stem.ExpectedCorrectCount)
            {
                flags.Add("AnswerCountMismatch");
            }

            var explanation = (rationale is not null && rationale.TryGetValue(stem.Number, out var expl)) ? expl : "";
            if (explanation.Length == 0)
            {
                flags.Add("ExplanationNotDetected");
            }

            results.Add(new DraftQuestionDto(
                stem.Number,
                chapter,
                entry?.Level,
                null,
                false,
                null,
                stem.QuestionText,
                stem.Options,
                correctIndexes,
                correctIndexes.Count > 1,
                entry?.Points ?? stem.PointsFromHeader,
                explanation,
                flags));
        }
        return results;
    }
}
