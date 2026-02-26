using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestingPlatform;

public enum UserRole { Admin, Teacher, Student }
public enum TestStatus { Draft, Published, Archived }
public enum QuestionType { SingleChoice, MultipleChoice, Text, Numeric }
public enum AssignmentTargetType { Group, User }
public enum AttemptStatus { Active, Submitted, Expired }

public static class Clock
{
    public static DateTime UtcNow() => DateTime.UtcNow;
    public static string ToDb(DateTime dt) => dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    public static DateTime FromDb(string s) => DateTime.Parse(s, null, DateTimeStyles.RoundtripKind).ToUniversalTime();
}

public static class JsonUtil
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize<T>(T obj) => JsonSerializer.Serialize(obj, Options);
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;
}

public sealed class User
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public UserRole Role { get; set; }
}

public sealed class Group
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class TestEntity
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public long CreatedByTeacherId { get; set; }
    public TestStatus Status { get; set; }
    public int PassPercent { get; set; } = 60;
    public string CreatedAt { get; set; } = Clock.ToDb(Clock.UtcNow());
}

public sealed class QuestionEntity
{
    public long Id { get; set; }
    public long TestId { get; set; }
    public QuestionType Type { get; set; }
    public string Text { get; set; } = "";
    public double Points { get; set; }
    public string SettingsJson { get; set; } = "{}";
    public string CreatedAt { get; set; } = Clock.ToDb(Clock.UtcNow());
}

public sealed class OptionEntity
{
    public long Id { get; set; }
    public long QuestionId { get; set; }
    public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
    public int SortOrder { get; set; }
}

public sealed class Assignment
{
    public long Id { get; set; }
    public long TestId { get; set; }
    public AssignmentTargetType TargetType { get; set; }
    public long TargetId { get; set; }
    public string? AvailableFrom { get; set; }
    public string? Deadline { get; set; }
    public int AttemptLimit { get; set; } = 1;
    public int? TimeLimitMinutes { get; set; }
    public bool ShuffleQuestions { get; set; } = true;
    public bool ShuffleOptions { get; set; } = true;
    public bool ShowScoreAfter { get; set; } = true;
    public bool ShowCorrectAfter { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Attempt
{
    public long Id { get; set; }
    public long AssignmentId { get; set; }
    public long UserId { get; set; }
    public string StartedAt { get; set; } = Clock.ToDb(Clock.UtcNow());
    public string? EndsAt { get; set; }
    public string? SubmittedAt { get; set; }
    public AttemptStatus Status { get; set; } = AttemptStatus.Active;
    public string SnapshotJson { get; set; } = "{}";
}

public sealed class AttemptAnswer
{
    public long Id { get; set; }
    public long AttemptId { get; set; }
    public long QuestionId { get; set; }
    public string AnswerJson { get; set; } = "{}";
    public string SavedAt { get; set; } = Clock.ToDb(Clock.UtcNow());
    public bool IsFinal { get; set; } = true;
}

public sealed class AttemptResult
{
    public long AttemptId { get; set; }
    public double Score { get; set; }
    public double MaxScore { get; set; }
    public double Percent { get; set; }
    public bool Passed { get; set; }
    public string CheckedAt { get; set; } = Clock.ToDb(Clock.UtcNow());
    public string DetailsJson { get; set; } = "{}";
}

public sealed class ManualCheck
{
    public long Id { get; set; }
    public long AttemptId { get; set; }
    public long QuestionId { get; set; }
    public long TeacherId { get; set; }
    public double ScoreGiven { get; set; }
    public string? Comment { get; set; }
    public string CheckedAt { get; set; } = Clock.ToDb(Clock.UtcNow());
}

public sealed class AuditEvent
{
    public long Id { get; set; }
    public long ActorUserId { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public long EntityId { get; set; }
    public string At { get; set; } = Clock.ToDb(Clock.UtcNow());
    public string MetaJson { get; set; } = "{}";
}

public sealed class TextSettings
{
    public List<string> Accepted { get; set; } = new();
    public bool CaseInsensitive { get; set; } = true;
    public bool Trim { get; set; } = true;
    public bool AllowManualCheck { get; set; } = true;
}

public sealed class NumericSettings
{
    public double Correct { get; set; }
    public double Tolerance { get; set; } = 0.01;
}

public sealed class AttemptSnapshot
{
    public long TestId { get; set; }
    public string TestTitle { get; set; } = "";
    public int PassPercent { get; set; }
    public bool ShowScoreAfter { get; set; }
    public bool ShowCorrectAfter { get; set; }
    public int? TimeLimitMinutes { get; set; }
    public List<SnapshotQuestion> Questions { get; set; } = new();
}

public sealed class SnapshotQuestion
{
    public long Id { get; set; }
    public QuestionType Type { get; set; }
    public string Text { get; set; } = "";
    public double Points { get; set; }
    public string SettingsJson { get; set; } = "{}";
    public List<SnapshotOption> Options { get; set; } = new();
}

public sealed class SnapshotOption
{
    public long Id { get; set; }
    public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
}

public sealed class GradeDetails
{
    public bool PendingManual { get; set; }
    public List<QuestionGradeDetail> Questions { get; set; } = new();
}

public sealed class QuestionGradeDetail
{
    public long QuestionId { get; set; }
    public QuestionType Type { get; set; }
    public double Awarded { get; set; }
    public double Max { get; set; }
    public bool Correct { get; set; }
    public bool NeedsManual { get; set; }
    public string? Comment { get; set; }
}

public sealed class AvailableAssignmentView
{
    public long AssignmentId { get; set; }
    public long TestId { get; set; }
    public string TestTitle { get; set; } = "";
    public string? Deadline { get; set; }
    public int? TimeLimitMinutes { get; set; }
    public int AttemptLimit { get; set; }
    public int UsedAttempts { get; set; }
}

public static class Guard
{
    public static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void NotEmpty(string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException(message);
    }
}
