using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PlatformApp;

public sealed class AvailableAssignment
{
    public long AssignmentId { get; set; }
    public string TestTitle { get; set; } = "";
    public string? Deadline { get; set; }
    public int AttemptLimit { get; set; }
    public int UsedAttempts { get; set; }
    public int? TimeLimitMinutes { get; set; }
}

public static class AttemptLogic
{
    public static List<AvailableAssignment> ListAvailableAssignments(long userId)
    {
        using var c = EngineDb.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT a.Id,t.Title,a.Deadline,a.AttemptLimit,a.TimeLimitMinutes
FROM Assignments a JOIN Tests t ON t.Id=a.TestId
WHERE a.IsActive=1 AND t.Status='Published' AND (
(a.TargetType='User' AND a.TargetId=@u) OR (a.TargetType='Group' AND a.TargetId IN (SELECT GroupId FROM GroupMembers WHERE UserId=@u))
)";
        cmd.Parameters.AddWithValue("@u", userId);
        using var r = cmd.ExecuteReader();
        var list = new List<AvailableAssignment>();
        while (r.Read())
        {
            var id = r.GetInt64(0);
            var dl = r.IsDBNull(2) ? null : r.GetString(2);
            if (dl != null && TimeUtil.Parse(dl) < TimeUtil.UtcNow) continue;
            list.Add(new AvailableAssignment { AssignmentId = id, TestTitle = r.GetString(1), Deadline = dl, AttemptLimit = r.GetInt32(3), TimeLimitMinutes = r.IsDBNull(4) ? null : r.GetInt32(4), UsedAttempts = EngineDb.GetAttemptsByAssignmentAndUser(id, userId).Count });
        }
        return list;
    }

    public static long StartAttempt(long userId, long assignmentId)
    {
        var a = EngineDb.GetAssignment(assignmentId);
        var t = EngineDb.GetTest(a.TestId);
        Guard.True(t.Status == TestStatus.Published, "Only published tests can be started");
        Guard.True(a.IsActive, "Assignment disabled");
        if (a.AvailableFrom != null) Guard.True(TimeUtil.Parse(a.AvailableFrom) <= TimeUtil.UtcNow, "Not available yet");
        if (a.Deadline != null) Guard.True(TimeUtil.Parse(a.Deadline) >= TimeUtil.UtcNow, "Deadline passed");
        Guard.True(ListAvailableAssignments(userId).Any(x => x.AssignmentId == assignmentId), "Not assigned");
        var all = EngineDb.GetAttemptsByAssignmentAndUser(assignmentId, userId);
        Guard.True(all.Count < a.AttemptLimit, "Attempt limit reached");
        Guard.True(!all.Any(x => x.Status == AttemptStatus.Active), "You already have active attempt");

        var snap = BuildSnapshot(t, a);
        using var c = EngineDb.Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO Attempts(AssignmentId,UserId,StartedAt,EndsAt,SubmittedAt,Status,SnapshotJson) VALUES(@a,@u,@s,@e,NULL,'Active',@sn); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@a", assignmentId); cmd.Parameters.AddWithValue("@u", userId); cmd.Parameters.AddWithValue("@s", TimeUtil.Iso(TimeUtil.UtcNow)); cmd.Parameters.AddWithValue("@e", a.TimeLimitMinutes.HasValue ? TimeUtil.Iso(TimeUtil.UtcNow.AddMinutes(a.TimeLimitMinutes.Value)) : (object?)DBNull.Value); cmd.Parameters.AddWithValue("@sn", JsonUtil.To(snap));
        var id = (long)(cmd.ExecuteScalar() ?? 0L);
        EngineDb.InsertAudit(c, tx, userId, "attempt.start", "Attempt", id, "{}"); tx.Commit();
        return id;
    }

    private static AttemptSnapshot BuildSnapshot(TestEntity t, Assignment a)
    {
        var rnd = new Random();
        var qs = EngineDb.GetQuestions(t.Id);
        if (a.ShuffleQuestions) qs = qs.OrderBy(_ => rnd.Next()).ToList();
        var snap = new AttemptSnapshot { TestId = t.Id, TestTitle = t.Title, PassPercent = t.PassPercent, ShowScoreAfter = a.ShowScoreAfter, ShowCorrectAfter = a.ShowCorrectAfter, TimeLimitMinutes = a.TimeLimitMinutes };
        foreach (var q in qs)
        {
            var sq = new SnapshotQuestion { Id = q.Id, Type = q.Type, Text = q.Text, Points = q.Points, SettingsJson = q.SettingsJson };
            var ops = EngineDb.GetOptions(q.Id);
            if (a.ShuffleOptions) ops = ops.OrderBy(_ => rnd.Next()).ToList();
            sq.Options.AddRange(ops.Select(o => new SnapshotOption { Id = o.Id, Text = o.Text, IsCorrect = o.IsCorrect }));
            snap.Questions.Add(sq);
        }
        return snap;
    }

    public static void SaveAnswer(long attemptId, long questionId, string answerJson)
    {
        var a = EngineDb.GetAttempt(attemptId);
        Guard.True(a.Status == AttemptStatus.Active, "Attempt not active");
        if (a.EndsAt != null && TimeUtil.Parse(a.EndsAt) <= TimeUtil.UtcNow) { SubmitAttempt(attemptId, "timeExpired"); throw new InvalidOperationException("Time expired"); }
        var snap = JsonUtil.From<AttemptSnapshot>(a.SnapshotJson);
        Guard.True(snap.Questions.Any(x => x.Id == questionId), "Wrong question");
        using var c = EngineDb.Open(); using var tx = c.BeginTransaction();
        using (var d = c.CreateCommand()) { d.Transaction = tx; d.CommandText = "DELETE FROM AttemptAnswers WHERE AttemptId=@a AND QuestionId=@q"; d.Parameters.AddWithValue("@a", attemptId); d.Parameters.AddWithValue("@q", questionId); d.ExecuteNonQuery(); }
        using (var i = c.CreateCommand()) { i.Transaction = tx; i.CommandText = "INSERT INTO AttemptAnswers(AttemptId,QuestionId,AnswerJson,SavedAt) VALUES(@a,@q,@j,@s)"; i.Parameters.AddWithValue("@a", attemptId); i.Parameters.AddWithValue("@q", questionId); i.Parameters.AddWithValue("@j", answerJson); i.Parameters.AddWithValue("@s", TimeUtil.Iso(TimeUtil.UtcNow)); i.ExecuteNonQuery(); }
        tx.Commit();
    }

    public static void SubmitAttempt(long attemptId, string reason)
    {
        using var c = EngineDb.Open(); using var tx = c.BeginTransaction();
        using var g = c.CreateCommand(); g.Transaction = tx; g.CommandText = "SELECT Status,UserId FROM Attempts WHERE Id=@id"; g.Parameters.AddWithValue("@id", attemptId); using var r = g.ExecuteReader(); Guard.True(r.Read(), "Attempt not found"); var st = Enum.Parse<AttemptStatus>(r.GetString(0)); var uid = r.GetInt64(1); if (st != AttemptStatus.Active) { tx.Commit(); return; }
        r.Close();
        using var u = c.CreateCommand(); u.Transaction = tx; u.CommandText = "UPDATE Attempts SET Status=@s,SubmittedAt=@t WHERE Id=@id"; u.Parameters.AddWithValue("@s", reason == "timeExpired" ? AttemptStatus.Expired.ToString() : AttemptStatus.Submitted.ToString()); u.Parameters.AddWithValue("@t", TimeUtil.Iso(TimeUtil.UtcNow)); u.Parameters.AddWithValue("@id", attemptId); u.ExecuteNonQuery();
        EngineDb.InsertAudit(c, tx, uid, "attempt.submit", "Attempt", attemptId, JsonUtil.To(new { reason }));
        tx.Commit();
        AutoGrade(attemptId);
    }

    public static void AutoGrade(long attemptId)
    {
        var a = EngineDb.GetAttempt(attemptId);
        var snap = JsonUtil.From<AttemptSnapshot>(a.SnapshotJson);
        var ans = EngineDb.GetAttemptAnswers(attemptId);
        var details = new GradeDetails();
        var score = 0.0; var max = snap.Questions.Sum(x => x.Points);

        foreach (var q in snap.Questions)
        {
            var d = new GradeDetail { QuestionId = q.Id, Max = q.Points };
            var aj = ans.TryGetValue(q.Id, out var v) ? v : "{}";
            if (q.Type == QuestionType.SingleChoice)
            {
                var sel = JsonUtil.From<Dictionary<string, long>>(aj).GetValueOrDefault("selectedOptionId");
                var corr = q.Options.FirstOrDefault(o => o.IsCorrect)?.Id;
                d.Correct = sel == corr && corr.HasValue; d.Awarded = d.Correct ? q.Points : 0;
            }
            else if (q.Type == QuestionType.MultipleChoice)
            {
                var ids = JsonUtil.From<Dictionary<string, long[]>>(aj).GetValueOrDefault("selectedOptionIds") ?? Array.Empty<long>();
                var cor = q.Options.Where(x => x.IsCorrect).Select(x => x.Id).ToHashSet();
                var wr = q.Options.Where(x => !x.IsCorrect).Select(x => x.Id).ToHashSet();
                var step = cor.Count == 0 ? q.Points : q.Points / cor.Count;
                var subtotal = ids.Count(cor.Contains) * step - ids.Count(wr.Contains) * step;
                d.Awarded = Math.Max(0, Math.Min(q.Points, subtotal)); d.Correct = Math.Abs(d.Awarded - q.Points) < 0.001;
            }
            else if (q.Type == QuestionType.Numeric)
            {
                var nm = JsonUtil.From<NumericSettings>(q.SettingsJson);
                var value = JsonUtil.From<Dictionary<string, double>>(aj).GetValueOrDefault("value", double.NaN);
                d.Correct = !double.IsNaN(value) && Math.Abs(value - nm.Correct) <= nm.Tolerance; d.Awarded = d.Correct ? q.Points : 0;
            }
            else
            {
                var txt = JsonUtil.From<Dictionary<string, string>>(aj).GetValueOrDefault("text", "") ?? "";
                var s = JsonUtil.From<TextSettings>(q.SettingsJson);
                var value = s.Trim ? txt.Trim() : txt;
                var accepted = s.Accepted.Select(x => s.Trim ? x.Trim() : x).ToList();
                if (s.CaseInsensitive) { value = value.ToLowerInvariant(); accepted = accepted.Select(x => x.ToLowerInvariant()).ToList(); }
                if (accepted.Contains(value)) { d.Correct = true; d.Awarded = q.Points; }
                else if (s.AllowManualCheck) { d.NeedsManual = true; d.Comment = "Pending manual"; details.PendingManual = true; }
            }
            score += d.Awarded; details.Questions.Add(d);
        }
        var percent = max > 0 ? score / max * 100.0 : 0;
        var passed = percent >= snap.PassPercent && !details.PendingManual;
        using var c = EngineDb.Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO AttemptResults(AttemptId,Score,MaxScore,Percent,Passed,CheckedAt,DetailsJson) VALUES(@a,@s,@m,@p,@pa,@c,@d) ON CONFLICT(AttemptId) DO UPDATE SET Score=excluded.Score,MaxScore=excluded.MaxScore,Percent=excluded.Percent,Passed=excluded.Passed,CheckedAt=excluded.CheckedAt,DetailsJson=excluded.DetailsJson";
        cmd.Parameters.AddWithValue("@a", attemptId); cmd.Parameters.AddWithValue("@s", score); cmd.Parameters.AddWithValue("@m", max); cmd.Parameters.AddWithValue("@p", percent); cmd.Parameters.AddWithValue("@pa", passed ? 1 : 0); cmd.Parameters.AddWithValue("@c", TimeUtil.Iso(TimeUtil.UtcNow)); cmd.Parameters.AddWithValue("@d", JsonUtil.To(details)); cmd.ExecuteNonQuery(); tx.Commit();
    }

    public static void ApplyManual(long attemptId, long qid, long teacherId, double score, string comment)
    {
        using var c = EngineDb.Open(); using var tx = c.BeginTransaction();
        using (var d = c.CreateCommand()) { d.Transaction = tx; d.CommandText = "DELETE FROM ManualChecks WHERE AttemptId=@a AND QuestionId=@q"; d.Parameters.AddWithValue("@a", attemptId); d.Parameters.AddWithValue("@q", qid); d.ExecuteNonQuery(); }
        using (var i = c.CreateCommand()) { i.Transaction = tx; i.CommandText = "INSERT INTO ManualChecks(AttemptId,QuestionId,TeacherId,ScoreGiven,Comment,CheckedAt) VALUES(@a,@q,@t,@s,@c,@at)"; i.Parameters.AddWithValue("@a", attemptId); i.Parameters.AddWithValue("@q", qid); i.Parameters.AddWithValue("@t", teacherId); i.Parameters.AddWithValue("@s", score); i.Parameters.AddWithValue("@c", comment); i.Parameters.AddWithValue("@at", TimeUtil.Iso(TimeUtil.UtcNow)); i.ExecuteNonQuery(); }
        EngineDb.InsertAudit(c, tx, teacherId, "attempt.manual", "Attempt", attemptId, JsonUtil.To(new { qid, score }));
        tx.Commit();
        AutoGrade(attemptId);
    }

    public static object GetReview(SessionUser user, long attemptId)
    {
        var a = EngineDb.GetAttempt(attemptId);
        if (user.Role == UserRole.Student) Guard.True(a.UserId == user.Id, "Access denied");
        var assignment = EngineDb.GetAssignment(a.AssignmentId);
        var result = EngineDb.GetResult(attemptId);
        var snap = JsonUtil.From<AttemptSnapshot>(a.SnapshotJson);
        var answers = EngineDb.GetAttemptAnswers(attemptId);
        return new
        {
            a.Id,
            a.Status,
            snap.TestTitle,
            Score = user.Role == UserRole.Student && !assignment.ShowScoreAfter ? null : result?.Score,
            Percent = user.Role == UserRole.Student && !assignment.ShowScoreAfter ? null : result?.Percent,
            Questions = snap.Questions.Select(q => new
            {
                q.Id,
                q.Text,
                q.Type,
                Answer = answers.GetValueOrDefault(q.Id, "{}"),
                Correct = user.Role == UserRole.Student && !(assignment.ShowCorrectAfter && a.Status != AttemptStatus.Active) ? null : q.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToList()
            })
        };
    }

    public static object Analytics(long teacherId, long testId)
    {
        Guard.True(EngineDb.GetTest(testId).CreatedByTeacherId == teacherId, "Access denied");
        var attempts = EngineDb.GetAttemptsByTest(testId);
        var results = attempts.Select(x => EngineDb.GetResult(x.Id)).Where(x => x != null).Cast<AttemptResult>().ToList();
        var avg = results.Count == 0 ? 0 : results.Average(x => x.Score);
        var pass = results.Count == 0 ? 0 : results.Count(x => x.Passed) * 100.0 / results.Count;
        var map = new Dictionary<long, (int err, int total)>();
        foreach (var r in results)
        {
            var d = JsonUtil.From<GradeDetails>(r.DetailsJson);
            foreach (var q in d.Questions)
            {
                if (!map.ContainsKey(q.QuestionId)) map[q.QuestionId] = (0, 0);
                var v = map[q.QuestionId]; v.total++; if (!q.Correct) v.err++; map[q.QuestionId] = v;
            }
        }
        return new { AverageScore = avg, PassRate = pass, Hard = map.OrderByDescending(x => x.Value.total == 0 ? 0 : (double)x.Value.err / x.Value.total).Take(5).Select(x => new { QuestionId = x.Key, WrongRate = x.Value.total == 0 ? 0 : (double)x.Value.err / x.Value.total }) };
    }

    public static string ExportCsv(long teacherId, long testId, string path)
    {
        Guard.True(EngineDb.GetTest(testId).CreatedByTeacherId == teacherId, "Access denied");
        var attempts = EngineDb.GetAttemptsByTest(testId);
        var users = EngineDb.GetUsers().ToDictionary(x => x.Id, x => x.DisplayName);
        using var w = new StreamWriter(path);
        w.WriteLine("Student,Group,Score,Percent,Passed,StartedAt,SubmittedAt");
        foreach (var a in attempts)
        {
            var r = EngineDb.GetResult(a.Id); if (r == null) continue;
            var g = EngineDb.GetGroupIdsByUser(a.UserId).Select(x => EngineDb.GetGroups().FirstOrDefault(g => g.Id == x)?.Name).FirstOrDefault() ?? "";
            w.WriteLine($"\"{users.GetValueOrDefault(a.UserId, "Unknown")}\",\"{g}\",{r.Score:0.##},{r.Percent:0.##},{(r.Passed ? 1 : 0)},\"{a.StartedAt}\",\"{a.SubmittedAt}\"");
        }
        return path;
    }
}
