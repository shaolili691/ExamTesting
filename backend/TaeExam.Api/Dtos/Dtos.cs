namespace TaeExam.Api.Dtos;

public record DraftQuestionDto(
    int QuestionNumber,
    string? Chapter,
    string? Level,
    string? Topic,
    bool IsScenario,
    string? ScenarioText,
    string QuestionText,
    List<string> Options,
    List<int> CorrectIndexes,
    bool IsMultiChoice,
    int Points,
    string Explanation,
    List<string> Flags);

public record ExamImportPreviewResponse(List<DraftQuestionDto> Questions, List<string> Warnings, bool AnswersFileProvided);

public record ImportQuestionRequest(
    string Chapter,
    string? Level,
    string? Topic,
    bool IsScenario,
    string? ScenarioText,
    string QuestionText,
    List<string> Options,
    List<int> CorrectIndexes,
    bool IsMultiChoice,
    int Points,
    string Explanation,
    string? DistractorDesign);

public record ConfirmExamImportRequest(string Title, int CategoryId, List<ImportQuestionRequest> Questions);

public record ConfirmExamImportResponse(int ExamId, int QuestionCount, int TotalPoints);

public record RegisterRequest(string Username, string Email, string Password);
public record LoginRequest(string Username, string Password);
public record RefreshRequest(string RefreshToken);
public record LogoutRequest(string RefreshToken);

public record UserSummaryDto(int Id, string Username, string Email, string RoleName);

public record AuthResponse(string AccessToken, string RefreshToken, DateTime ExpiresAtUtc, UserSummaryDto User);

public record AuditLogEntry(int? UserId, string Action, string? Entity, int? EntityId, string? Description, string? Ip, string? Browser, DateTime CreatedAtUtc);

public record AuditLogDto(int Id, int? UserId, string? Username, string Action, string? Entity, int? EntityId, string? Description, string? Ip, string? Browser, DateTime CreatedAtUtc);

public record CreateAnnouncementRequest(string Title, string Content, DateTime PublishAtUtc, DateTime? ExpireAtUtc, int Priority);

public record AnnouncementDto(int Id, string Title, string Content, DateTime PublishAtUtc, DateTime? ExpireAtUtc, int Priority, string CreatedByUsername, DateTime CreatedAtUtc);

public record ExamCategoryDto(int Id, string Name);

public record RolePermissionRowDto(string Key, string Label, List<string> Roles);
public record UpdateRolePermissionRequest(bool Allowed);

public record CreateUserRequest(string Username, string Email, string Password, string RoleName);
public record UpdateUserRequest(string Email, string RoleName, bool Active);
public record AdminUserDto(int Id, string Username, string Email, string RoleName, bool Active, DateTime CreatedAtUtc);

public record WrongQuestionDto(
    int QuestionId,
    string Chapter,
    string? ChapterTitle,
    string? Topic,
    string? Level,
    bool IsScenario,
    string? ScenarioText,
    string QuestionText,
    List<string> Options,
    List<int> CorrectIndexes,
    List<int> LastSelectedIndexes,
    string Explanation,
    DateTime LastMissedAtUtc,
    int TimesMissed);

public record AnswerStateDto(int ExamQuestionId, List<int> SelectedIndexes, bool Marked);

public record AttemptStateDto(int AttemptId, int ExamId, bool Resumed, DateTime StartedAtUtc, int? TimeLimitMinutes, List<AnswerStateDto> Answers);

public record ExamSummaryDto(int Id, string Title, string Type, int TotalPoints, int PassThresholdPoints, int QuestionCount, DateTime CreatedAtUtc, int? CategoryId, string? CategoryName);

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

public record StartAttemptResponse(int AttemptId, bool Resumed);

public record AnswerSubmission(int ExamQuestionId, List<int> SelectedIndexes, bool Marked = false);

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
    DateTime StartedAtUtc,
    DateTime? SubmittedAtUtc,
    int? ElapsedSeconds,
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

public record OverallStatsDto(int TotalAttempts, decimal AvgPercent, decimal PassRate, int StudyTimeMinutes, int CompletedExams);

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
