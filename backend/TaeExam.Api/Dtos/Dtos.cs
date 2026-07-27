namespace TaeExam.Api.Dtos;

public record ExamSummaryDto(int Id, string Title, string Type, int TotalPoints, int PassThresholdPoints, int QuestionCount, DateTime CreatedAtUtc);

public record ExamQuestionDto(
    int ExamQuestionId,
    int OrderIndex,
    string Chapter,
    string? Level,
    string? Topic,
    bool IsScenario,
    string? ScenarioText,
    string QuestionText,
    List<string> Options,
    bool IsMultiChoice,
    int Points);

public record ExamPaperDto(
    int Id,
    string Title,
    string Type,
    int TotalPoints,
    int PassThresholdPoints,
    int? TimeLimitMinutes,
    List<ExamQuestionDto> Questions);

public record StartAttemptRequest(int ExamId);

public record StartAttemptResponse(int AttemptId);

public record AnswerSubmission(int ExamQuestionId, List<int> SelectedIndexes);

public record SubmitAttemptRequest(List<AnswerSubmission> Answers);

public record QuestionReviewDto(
    int ExamQuestionId,
    string Chapter,
    string? Topic,
    string? Level,
    bool IsScenario,
    string? ScenarioText,
    string QuestionText,
    List<string> Options,
    int Points,
    List<int> SelectedIndexes,
    List<int> CorrectIndexes,
    bool IsCorrect,
    int PointsAwarded,
    string Explanation,
    string? DistractorDesign);

public record ChapterBreakdownDto(string Chapter, string ChapterTitle, int Correct, int Total, int PointsEarned, int PointsMax);

public record AttemptResultDto(
    int AttemptId,
    int ExamId,
    string ExamTitle,
    int Score,
    int MaxScore,
    decimal PercentScore,
    bool Passed,
    DateTime? SubmittedAtUtc,
    List<QuestionReviewDto> Questions,
    List<ChapterBreakdownDto> ChapterBreakdown);

public record AttemptSummaryDto(
    int Id,
    int ExamId,
    string ExamTitle,
    string ExamType,
    int Score,
    int MaxScore,
    decimal PercentScore,
    bool Passed,
    DateTime? SubmittedAtUtc,
    string Status);

public record OverallStatsDto(int TotalAttempts, decimal AvgPercent, decimal PassRate);

public record ChapterAccuracyDto(string Chapter, string ChapterTitle, int QuestionsSeen, int Correct, decimal AccuracyPct);

public record TopicAccuracyDto(string Topic, int QuestionsSeen, int Correct, decimal AccuracyPct);

public record TrendPointDto(int AttemptId, DateTime? SubmittedAtUtc, decimal PercentScore);

public record AnalysisOverviewDto(
    OverallStatsDto Overall,
    List<ChapterAccuracyDto> ByChapter,
    List<TopicAccuracyDto> ByTopic,
    List<string> WeakestChapters,
    List<TrendPointDto> Trend);

public record GenerateExamRequest(
    string? Title = null,
    int QuestionCount = 40,
    bool BoostWeakChapters = true,
    decimal BoostFactor = 1.5m,
    int AvoidRecentExamsCount = 2);

public record ChapterBlueprintDto(string Chapter, int TargetCount, decimal WeightUsed);

public record GenerateExamResponse(int ExamId, List<ChapterBlueprintDto> BlueprintUsed, List<string> Warnings);

public record DrillRequest(int? MaxQuestions = null);

public record DrillResponse(int ExamId, List<ChapterAccuracyDto> WeakAreaSummary, int CoreWrongCount, int FillCount, List<string> Warnings);
