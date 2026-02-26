using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace PlatformApp;

public enum UserRole { Admin, Teacher, Student }
public enum TestStatus { Draft, Published, Archived }
public enum QuestionType { SingleChoice, MultipleChoice, Text, Numeric }
public enum AttemptStatus { Active, Submitted, Expired }
public enum TargetType { Group, User }

public static class Theme
{
    public static readonly Font Font = new("Segoe UI", 10f);
    public static readonly Font HeaderFont = new("Segoe UI Semibold", 13f);
    public static Color Bg = Color.White;
    public static Color Panel = Color.FromArgb(245, 246, 248);
    public static Color Accent = Color.FromArgb(37, 99, 235);
    public static Color Text = Color.FromArgb(30, 41, 59);

    public static void Apply(Form f)
    {
        f.Font = Font;
        f.BackColor = Bg;
        f.StartPosition = FormStartPosition.CenterScreen;
        f.MinimumSize = new Size(960, 640);
    }

    public static Button Btn(string text, EventHandler onClick, bool accent = false)
    {
        var b = new Button { Text = text, AutoSize = true, Padding = new Padding(10, 6, 10, 6), FlatStyle = FlatStyle.Flat };
        b.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        b.FlatAppearance.BorderSize = 1;
        b.BackColor = accent ? Accent : Color.White;
        b.ForeColor = accent ? Color.White : Text;
        b.Click += onClick;
        return b;
    }

    public static DataGridView Grid()
    {
        var g = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            BackgroundColor = Bg,
            BorderStyle = BorderStyle.None,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false
        };
        g.ColumnHeadersDefaultCellStyle.BackColor = Panel;
        g.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 10f);
        g.EnableHeadersVisualStyles = false;
        g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 250, 251);
        return g;
    }
}

public static class TimeUtil
{
    public static DateTime UtcNow => DateTime.UtcNow;
    public static string Iso(DateTime dt) => dt.ToUniversalTime().ToString("O");
    public static DateTime Parse(string s) => DateTime.Parse(s, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
}

public static class JsonUtil
{
    public static readonly JsonSerializerOptions Opt = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    public static string To<T>(T model) => JsonSerializer.Serialize(model, Opt);
    public static T From<T>(string json) => JsonSerializer.Deserialize<T>(json, Opt)!;
}

public static class Guard
{
    public static void True(bool cond, string msg)
    {
        if (!cond) throw new InvalidOperationException(msg);
    }
}

public sealed class PasswordBundle
{
    public byte[] Salt { get; set; } = Array.Empty<byte>();
    public byte[] Hash { get; set; } = Array.Empty<byte>();
}

public static class Security
{
    public static PasswordBundle HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        using var pbkdf = new Rfc2898DeriveBytes(password, salt, 100_000, HashAlgorithmName.SHA256);
        return new PasswordBundle { Salt = salt, Hash = pbkdf.GetBytes(32) };
    }

    public static bool Verify(string password, byte[] salt, byte[] hash)
    {
        using var pbkdf = new Rfc2898DeriveBytes(password, salt, 100_000, HashAlgorithmName.SHA256);
        var check = pbkdf.GetBytes(32);
        return CryptographicOperations.FixedTimeEquals(check, hash);
    }
}

public sealed class User
{
    public long Id { get; set; }
    public string Login { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public UserRole Role { get; set; }
    public byte[] PasswordSalt { get; set; } = Array.Empty<byte>();
    public byte[] PasswordHash { get; set; } = Array.Empty<byte>();
    public bool IsActive { get; set; }
    public string CreatedAt { get; set; } = TimeUtil.Iso(TimeUtil.UtcNow);
}

public sealed class Group { public long Id { get; set; } public string Name { get; set; } = ""; }
public sealed class TestEntity
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public long CreatedByTeacherId { get; set; }
    public TestStatus Status { get; set; }
    public int PassPercent { get; set; } = 60;
    public int DefaultAttemptLimit { get; set; } = 1;
    public int? DefaultTimeLimitMinutes { get; set; }
    public bool DefaultShuffleQuestions { get; set; } = true;
    public bool DefaultShuffleOptions { get; set; } = true;
    public bool DefaultShowScoreAfter { get; set; } = true;
    public bool DefaultShowCorrectAfter { get; set; }
    public string CreatedAt { get; set; } = TimeUtil.Iso(TimeUtil.UtcNow);
}
public sealed class QuestionEntity
{
    public long Id { get; set; }
    public long TestId { get; set; }
    public QuestionType Type { get; set; }
    public string Text { get; set; } = "";
    public double Points { get; set; } = 1;
    public string SettingsJson { get; set; } = "{}";
}
public sealed class OptionEntity { public long Id { get; set; } public long QuestionId { get; set; } public string Text { get; set; } = ""; public bool IsCorrect { get; set; } public int SortOrder { get; set; } }
public sealed class Assignment
{
    public long Id { get; set; }
    public long TestId { get; set; }
    public TargetType TargetType { get; set; }
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
    public string StartedAt { get; set; } = TimeUtil.Iso(TimeUtil.UtcNow);
    public string? EndsAt { get; set; }
    public string? SubmittedAt { get; set; }
    public AttemptStatus Status { get; set; } = AttemptStatus.Active;
    public string SnapshotJson { get; set; } = "{}";
}
public sealed class AttemptResult { public long AttemptId { get; set; } public double Score { get; set; } public double MaxScore { get; set; } public double Percent { get; set; } public bool Passed { get; set; } public string CheckedAt { get; set; } = TimeUtil.Iso(TimeUtil.UtcNow); public string DetailsJson { get; set; } = "{}"; }
public sealed class ManualCheck { public long Id { get; set; } public long AttemptId { get; set; } public long QuestionId { get; set; } public long TeacherId { get; set; } public double ScoreGiven { get; set; } public string Comment { get; set; } = ""; public string CheckedAt { get; set; } = TimeUtil.Iso(TimeUtil.UtcNow); }
public sealed class AuditEvent { public long Id { get; set; } public long ActorUserId { get; set; } public string Action { get; set; } = ""; public string EntityType { get; set; } = ""; public long EntityId { get; set; } public string At { get; set; } = TimeUtil.Iso(TimeUtil.UtcNow); public string MetaJson { get; set; } = "{}"; }

public sealed class TextSettings { public List<string> Accepted { get; set; } = new(); public bool CaseInsensitive { get; set; } = true; public bool Trim { get; set; } = true; public bool AllowManualCheck { get; set; } = true; }
public sealed class NumericSettings { public double Correct { get; set; } public double Tolerance { get; set; } = 0.01; }

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
public sealed class SnapshotQuestion { public long Id { get; set; } public QuestionType Type { get; set; } public string Text { get; set; } = ""; public double Points { get; set; } public string SettingsJson { get; set; } = "{}"; public List<SnapshotOption> Options { get; set; } = new(); }
public sealed class SnapshotOption { public long Id { get; set; } public string Text { get; set; } = ""; public bool IsCorrect { get; set; } }
public sealed class GradeDetail { public long QuestionId { get; set; } public bool Correct { get; set; } public bool NeedsManual { get; set; } public double Awarded { get; set; } public double Max { get; set; } public string? Comment { get; set; } }
public sealed class GradeDetails { public bool PendingManual { get; set; } public List<GradeDetail> Questions { get; set; } = new(); }

public sealed class SessionUser { public long Id { get; set; } public string Login { get; set; } = ""; public string DisplayName { get; set; } = ""; public UserRole Role { get; set; } }
