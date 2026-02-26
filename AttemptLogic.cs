using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace TestingPlatform;

public static class AttemptLogic
{
    public static List<AvailableAssignmentView> ListAvailableAssignments(long userId)
    {
        var groupIds = EngineDb.GetGroupIdsForUser(userId);
        using var c = EngineDb.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT a.Id,a.TestId,t.Title,a.Deadline,a.TimeLimitMinutes,a.AttemptLimit
FROM Assignments a JOIN Tests t ON t.Id=a.TestId
WHERE a.IsActive=1 AND t.Status='Published' AND (
(a.TargetType='User' AND a.TargetId=@u) OR
(a.TargetType='Group' AND a.TargetId IN (SELECT GroupId FROM GroupMembers WHERE UserId=@u))
);";
        cmd.Parameters.AddWithValue("@u", userId);
        using var r = cmd.ExecuteReader();
        var now = Clock.UtcNow();
        var list = new List<AvailableAssignmentView>();
        while (r.Read())
        {
            var id = r.GetInt64(0);
            var deadline = r.IsDBNull(3) ? null : r.GetString(3);
            if (deadline != null && Clock.FromDb(deadline) < now) continue;
            var used = EngineDb.GetAttemptsByAssignmentAndUser(id, userId).Count;
            list.Add(new AvailableAssignmentView
            {
                AssignmentId = id,
                TestId = r.GetInt64(1),
                TestTitle = r.GetString(2),
                Deadline = deadline,
                TimeLimitMinutes = r.IsDBNull(4) ? null : r.GetInt32(4),
                AttemptLimit = r.GetInt32(5),
                UsedAttempts = used
            });
        }
        return list;
    }

    public static long StartAttempt(long userId, long assignmentId)
    {
        var assignment = EngineDb.GetAssignment(assignmentId);
        var test = EngineDb.GetTest(assignment.TestId);
        Guard.True(test.Status == TestStatus.Published, "Test is not published");
        Guard.True(assignment.IsActive, "Assignment inactive");
        var now = Clock.UtcNow();
        if (!string.IsNullOrWhiteSpace(assignment.AvailableFrom)) Guard.True(Clock.FromDb(assignment.AvailableFrom!) <= now, "Assignment not yet available");
        if (!string.IsNullOrWhiteSpace(assignment.Deadline)) Guard.True(now <= Clock.FromDb(assignment.Deadline!), "Deadline passed");

        var available = ListAvailableAssignments(userId).Any(x => x.AssignmentId == assignmentId);
        Guard.True(available, "Assignment unavailable for user");

        var attempts = EngineDb.GetAttemptsByAssignmentAndUser(assignmentId, userId);
        Guard.True(attempts.Count < assignment.AttemptLimit, "Attempt limit reached");
        Guard.True(!attempts.Any(a => a.Status == AttemptStatus.Active), "Active attempt already exists");

        var snapshot = BuildSnapshot(test, assignment);
        using var c = EngineDb.Open();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        var endsAt = assignment.TimeLimitMinutes.HasValue ? Clock.ToDb(now.AddMinutes(assignment.TimeLimitMinutes.Value)) : null;
        cmd.CommandText = @"INSERT INTO Attempts(AssignmentId,UserId,StartedAt,EndsAt,SubmittedAt,Status,SnapshotJson)
VALUES(@a,@u,@s,@e,NULL,'Active',@sn); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@a", assignmentId);
        cmd.Parameters.AddWithValue("@u", userId);
        cmd.Parameters.AddWithValue("@s", Clock.ToDb(now));
        cmd.Parameters.AddWithValue("@e", (object?)endsAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sn", JsonUtil.Serialize(snapshot));
        var attemptId = (long)(cmd.ExecuteScalar() ?? 0L);
        EngineDb.InsertAudit(c, tx, userId, "StartAttempt", "Attempt", attemptId, JsonUtil.Serialize(new { assignmentId }));
        tx.Commit();
        return attemptId;
    }

    private static AttemptSnapshot BuildSnapshot(TestEntity test, Assignment assignment)
    {
        var rng = new Random();
        var questions = EngineDb.GetQuestions(test.Id);
        if (assignment.ShuffleQuestions) questions = questions.OrderBy(_ => rng.Next()).ToList();

        var snap = new AttemptSnapshot
        {
            TestId = test.Id,
            TestTitle = test.Title,
            PassPercent = test.PassPercent,
            ShowScoreAfter = assignment.ShowScoreAfter,
            ShowCorrectAfter = assignment.ShowCorrectAfter,
            TimeLimitMinutes = assignment.TimeLimitMinutes
        };

        foreach (var q in questions)
        {
            var sq = new SnapshotQuestion { Id = q.Id, Type = q.Type, Text = q.Text, Points = q.Points, SettingsJson = q.SettingsJson };
            var options = EngineDb.GetOptions(q.Id);
            if (assignment.ShuffleOptions) options = options.OrderBy(_ => rng.Next()).ToList();
            sq.Options.AddRange(options.Select(o => new SnapshotOption { Id = o.Id, Text = o.Text, IsCorrect = o.IsCorrect }));
            snap.Questions.Add(sq);
        }

        return snap;
    }

    public static void SaveAnswer(long attemptId, long questionId, string answerJson)
    {
        var attempt = EngineDb.GetAttempt(attemptId);
        Guard.True(attempt.Status == AttemptStatus.Active, "Attempt not active");
        if (!string.IsNullOrWhiteSpace(attempt.EndsAt) && Clock.FromDb(attempt.EndsAt!) <= Clock.UtcNow())
        {
            SubmitAttempt(attemptId, "timeExpired");
            throw new InvalidOperationException("Attempt expired and submitted");
        }

        var snapshot = JsonUtil.Deserialize<AttemptSnapshot>(attempt.SnapshotJson);
        Guard.True(snapshot.Questions.Any(q => q.Id == questionId), "Question not in snapshot");

        using var c = EngineDb.Open();
        using var tx = c.BeginTransaction();
        using var del = c.CreateCommand();
        del.Transaction = tx;
        del.CommandText = "DELETE FROM AttemptAnswers WHERE AttemptId=@a AND QuestionId=@q;";
        del.Parameters.AddWithValue("@a", attemptId);
        del.Parameters.AddWithValue("@q", questionId);
        del.ExecuteNonQuery();

        using var ins = c.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = "INSERT INTO AttemptAnswers(AttemptId,QuestionId,AnswerJson,SavedAt,IsFinal) VALUES(@a,@q,@j,@s,1);";
        ins.Parameters.AddWithValue("@a", attemptId); ins.Parameters.AddWithValue("@q", questionId); ins.Parameters.AddWithValue("@j", answerJson); ins.Parameters.AddWithValue("@s", Clock.ToDb(Clock.UtcNow()));
        ins.ExecuteNonQuery();
        tx.Commit();
    }

    public static void SubmitAttempt(long attemptId, string reason)
    {
        using var c = EngineDb.Open();
        using var tx = c.BeginTransaction();
        using var get = c.CreateCommand();
        get.Transaction = tx;
        get.CommandText = "SELECT Status,UserId FROM Attempts WHERE Id=@id;";
        get.Parameters.AddWithValue("@id", attemptId);
        using var r = get.ExecuteReader();
        Guard.True(r.Read(), "Attempt not found");
        var status = Enum.Parse<AttemptStatus>(r.GetString(0));
        var userId = r.GetInt64(1);
        if (status != AttemptStatus.Active)
        {
            tx.Commit();
            return;
        }
        r.Close();

        using var upd = c.CreateCommand();
        upd.Transaction = tx;
        var newStatus = reason == "timeExpired" ? AttemptStatus.Expired : AttemptStatus.Submitted;
        upd.CommandText = "UPDATE Attempts SET Status=@s,SubmittedAt=@t WHERE Id=@id;";
        upd.Parameters.AddWithValue("@s", newStatus.ToString());
        upd.Parameters.AddWithValue("@t", Clock.ToDb(Clock.UtcNow()));
        upd.Parameters.AddWithValue("@id", attemptId);
        upd.ExecuteNonQuery();
        EngineDb.InsertAudit(c, tx, userId, "SubmitAttempt", "Attempt", attemptId, JsonUtil.Serialize(new { reason }));
        tx.Commit();

        AutoGradeAttempt(attemptId);
    }

    public static void AutoGradeAttempt(long attemptId)
    {
        var attempt = EngineDb.GetAttempt(attemptId);
        var snapshot = JsonUtil.Deserialize<AttemptSnapshot>(attempt.SnapshotJson);
        var answers = GetAttemptAnswers(attemptId);
        var manualMap = GetManualChecksMap(attemptId);

        var details = new GradeDetails();
        double score = 0;
        double maxScore = snapshot.Questions.Sum(q => q.Points);

        foreach (var q in snapshot.Questions)
        {
            var detail = new QuestionGradeDetail { QuestionId = q.Id, Type = q.Type, Max = q.Points };
            answers.TryGetValue(q.Id, out var answerJson);
            answerJson ??= "{}";

            switch (q.Type)
            {
                case QuestionType.SingleChoice:
                {
                    long selected = ReadLong(answerJson, "selectedOptionId");
                    var correct = q.Options.FirstOrDefault(o => o.IsCorrect)?.Id;
                    detail.Correct = selected != 0 && selected == correct;
                    detail.Awarded = detail.Correct ? q.Points : 0;
                    break;
                }
                case QuestionType.MultipleChoice:
                {
                    var selected = ReadLongList(answerJson, "selectedOptionIds");
                    var correctIds = q.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToHashSet();
                    var wrongIds = q.Options.Where(o => !o.IsCorrect).Select(o => o.Id).ToHashSet();
                    double step = correctIds.Count == 0 ? q.Points : q.Points / correctIds.Count;
                    double subtotal = selected.Count(id => correctIds.Contains(id)) * step - selected.Count(id => wrongIds.Contains(id)) * step;
                    if (subtotal < 0) subtotal = 0;
                    if (subtotal > q.Points) subtotal = q.Points;
                    detail.Awarded = subtotal;
                    detail.Correct = Math.Abs(subtotal - q.Points) < 0.001;
                    break;
                }
                case QuestionType.Numeric:
                {
                    var ns = JsonUtil.Deserialize<NumericSettings>(q.SettingsJson);
                    double val = ReadDouble(answerJson, "value");
                    detail.Correct = Math.Abs(val - ns.Correct) <= ns.Tolerance;
                    detail.Awarded = detail.Correct ? q.Points : 0;
                    break;
                }
                case QuestionType.Text:
                {
                    var ts = JsonUtil.Deserialize<TextSettings>(q.SettingsJson);
                    var t = ReadString(answerJson, "text");
                    var input = ts.Trim ? t.Trim() : t;
                    var accepted = ts.Accepted.Select(x => ts.Trim ? x.Trim() : x).ToList();
                    if (ts.CaseInsensitive)
                    {
                        input = input.ToLowerInvariant();
                        accepted = accepted.Select(x => x.ToLowerInvariant()).ToList();
                    }
                    if (accepted.Contains(input))
                    {
                        detail.Correct = true;
                        detail.Awarded = q.Points;
                    }
                    else if (ts.AllowManualCheck)
                    {
                        if (manualMap.TryGetValue(q.Id, out var mc))
                        {
                            detail.NeedsManual = false;
                            detail.Awarded = Math.Max(0, Math.Min(q.Points, mc.ScoreGiven));
                            detail.Comment = mc.Comment;
                        }
                        else
                        {
                            detail.NeedsManual = true;
                            detail.Comment = "Pending manual check";
                            details.PendingManual = true;
                        }
                    }
                    else
                    {
                        detail.Awarded = 0;
                        detail.Correct = false;
                    }
                    break;
                }
            }
            score += detail.Awarded;
            details.Questions.Add(detail);
        }

        var percent = maxScore <= 0 ? 0 : score / maxScore * 100.0;
        var passed = percent >= snapshot.PassPercent && !details.PendingManual;
        using var c = EngineDb.Open();
        using var tx = c.BeginTransaction();
        using var upsert = c.CreateCommand();
        upsert.Transaction = tx;
        upsert.CommandText = @"INSERT INTO AttemptResults(AttemptId,Score,MaxScore,Percent,Passed,CheckedAt,DetailsJson)
VALUES(@a,@s,@m,@p,@pa,@c,@d)
ON CONFLICT(AttemptId) DO UPDATE SET Score=excluded.Score,MaxScore=excluded.MaxScore,Percent=excluded.Percent,Passed=excluded.Passed,CheckedAt=excluded.CheckedAt,DetailsJson=excluded.DetailsJson;";
        upsert.Parameters.AddWithValue("@a", attemptId);
        upsert.Parameters.AddWithValue("@s", score);
        upsert.Parameters.AddWithValue("@m", maxScore);
        upsert.Parameters.AddWithValue("@p", percent);
        upsert.Parameters.AddWithValue("@pa", passed ? 1 : 0);
        upsert.Parameters.AddWithValue("@c", Clock.ToDb(Clock.UtcNow()));
        upsert.Parameters.AddWithValue("@d", JsonUtil.Serialize(details));
        upsert.ExecuteNonQuery();
        tx.Commit();
    }

    public static void ApplyManualCheck(long attemptId, long questionId, long teacherId, double scoreGiven, string? comment)
    {
        using var c = EngineDb.Open();
        using var tx = c.BeginTransaction();
        using var del = c.CreateCommand();
        del.Transaction = tx;
        del.CommandText = "DELETE FROM ManualChecks WHERE AttemptId=@a AND QuestionId=@q;";
        del.Parameters.AddWithValue("@a", attemptId);
        del.Parameters.AddWithValue("@q", questionId);
        del.ExecuteNonQuery();

        using var ins = c.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = "INSERT INTO ManualChecks(AttemptId,QuestionId,TeacherId,ScoreGiven,Comment,CheckedAt) VALUES(@a,@q,@t,@s,@c,@at);";
        ins.Parameters.AddWithValue("@a", attemptId); ins.Parameters.AddWithValue("@q", questionId); ins.Parameters.AddWithValue("@t", teacherId);
        ins.Parameters.AddWithValue("@s", scoreGiven); ins.Parameters.AddWithValue("@c", (object?)comment ?? DBNull.Value); ins.Parameters.AddWithValue("@at", Clock.ToDb(Clock.UtcNow()));
        ins.ExecuteNonQuery();

        EngineDb.InsertAudit(c, tx, teacherId, "ManualCheck", "Attempt", attemptId, JsonUtil.Serialize(new { questionId, scoreGiven }));
        tx.Commit();
        AutoGradeAttempt(attemptId);
    }

    public static object GetAttemptReview(long userId, long attemptId)
    {
        var user = EngineDb.GetUser(userId);
        var attempt = EngineDb.GetAttempt(attemptId);
        var assignment = EngineDb.GetAssignment(attempt.AssignmentId);
        var result = EngineDb.GetAttemptResult(attemptId);
        var snapshot = JsonUtil.Deserialize<AttemptSnapshot>(attempt.SnapshotJson);
        var answers = GetAttemptAnswers(attemptId);

        Guard.True(user.Role != UserRole.Student || attempt.UserId == userId, "Access denied");

        var payload = new
        {
            attempt.Id,
            attempt.Status,
            attempt.StartedAt,
            attempt.SubmittedAt,
            TestTitle = snapshot.TestTitle,
            Score = (user.Role == UserRole.Student && !assignment.ShowScoreAfter) ? (double?)null : result?.Score,
            Percent = (user.Role == UserRole.Student && !assignment.ShowScoreAfter) ? (double?)null : result?.Percent,
            ShowCorrect = user.Role == UserRole.Student ? (assignment.ShowCorrectAfter && attempt.Status != AttemptStatus.Active) : true,
            Questions = snapshot.Questions.Select(q => new
            {
                q.Id,
                q.Text,
                q.Type,
                Answer = answers.TryGetValue(q.Id, out var a) ? a : "{}",
                Correct = (user.Role == UserRole.Student && (!assignment.ShowCorrectAfter || attempt.Status == AttemptStatus.Active)) ? null : q.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToList()
            }).ToList()
        };
        return payload;
    }

    public static object GetAnalyticsForTeacher(long teacherId, long testId)
    {
        var test = EngineDb.GetTest(testId);
        Guard.True(test.CreatedByTeacherId == teacherId, "No access");
        var attempts = EngineDb.GetAttemptsByTest(testId);
        var results = attempts.Select(a => EngineDb.GetAttemptResult(a.Id)).Where(x => x != null).Cast<AttemptResult>().ToList();
        double avg = results.Count == 0 ? 0 : results.Average(r => r.Score);
        double pass = results.Count == 0 ? 0 : results.Count(r => r.Passed) * 100.0 / results.Count;

        var qErrors = new Dictionary<long, (int wrong, int total)>();
        foreach (var r in results)
        {
            var d = JsonUtil.Deserialize<GradeDetails>(r.DetailsJson);
            foreach (var q in d.Questions)
            {
                if (!qErrors.ContainsKey(q.QuestionId)) qErrors[q.QuestionId] = (0, 0);
                var cur = qErrors[q.QuestionId];
                cur.total++;
                if (!q.Correct) cur.wrong++;
                qErrors[q.QuestionId] = cur;
            }
        }

        var hard = qErrors.OrderByDescending(x => x.Value.total == 0 ? 0 : (double)x.Value.wrong / x.Value.total).Take(5)
            .Select(x => new { QuestionId = x.Key, WrongRate = x.Value.total == 0 ? 0 : (double)x.Value.wrong / x.Value.total }).ToList();

        return new { AverageScore = avg, PassRate = pass, Attempts = results.Count, HardQuestions = hard };
    }

    public static string ExportResultsCsv(long teacherId, long testId, string path)
    {
        var test = EngineDb.GetTest(testId);
        Guard.True(test.CreatedByTeacherId == teacherId, "No access");
        var attempts = EngineDb.GetAttemptsByTest(testId);
        var users = EngineDb.GetUserNamesMap();
        using var c = EngineDb.Open();
        using var writer = new StreamWriter(path, false, System.Text.Encoding.UTF8);
        writer.WriteLine("StudentName,Group,Attempt#,Score,Percent,Passed,StartedAt,SubmittedAt");
        foreach (var a in attempts)
        {
            var result = EngineDb.GetAttemptResult(a.Id);
            if (result == null) continue;
            var group = GetFirstGroupNameForUser(c, a.UserId);
            var attemptNo = EngineDb.GetAttemptsByAssignmentAndUser(a.AssignmentId, a.UserId).FindIndex(x => x.Id == a.Id) + 1;
            writer.WriteLine($"{Csv(users.GetValueOrDefault(a.UserId, "unknown"))},{Csv(group)},{attemptNo},{result.Score:0.##},{result.Percent:0.##},{(result.Passed ? 1 : 0)},{Csv(a.StartedAt)},{Csv(a.SubmittedAt ?? "")}");
        }
        return path;
    }

    private static string GetFirstGroupNameForUser(SqliteConnection c, long userId)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT g.Name FROM Groups g JOIN GroupMembers gm ON gm.GroupId=g.Id WHERE gm.UserId=@u ORDER BY g.Name LIMIT 1;";
        cmd.Parameters.AddWithValue("@u", userId);
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }

    private static Dictionary<long, string> GetAttemptAnswers(long attemptId)
    {
        var m = new Dictionary<long, string>();
        using var c = EngineDb.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT QuestionId,AnswerJson FROM AttemptAnswers WHERE AttemptId=@a;";
        cmd.Parameters.AddWithValue("@a", attemptId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) m[r.GetInt64(0)] = r.GetString(1);
        return m;
    }

    private static Dictionary<long, ManualCheck> GetManualChecksMap(long attemptId)
    {
        var m = new Dictionary<long, ManualCheck>();
        using var c = EngineDb.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,AttemptId,QuestionId,TeacherId,ScoreGiven,Comment,CheckedAt FROM ManualChecks WHERE AttemptId=@a;";
        cmd.Parameters.AddWithValue("@a", attemptId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) m[r.GetInt64(2)] = new ManualCheck { Id = r.GetInt64(0), AttemptId = r.GetInt64(1), QuestionId = r.GetInt64(2), TeacherId = r.GetInt64(3), ScoreGiven = r.GetDouble(4), Comment = r.IsDBNull(5) ? null : r.GetString(5), CheckedAt = r.GetString(6) };
        return m;
    }

    private static string Csv(string s) => '"' + s.Replace("\"", "\"\"") + '"';

    private static long ReadLong(string json, string prop)
    {
        using var d = System.Text.Json.JsonDocument.Parse(json);
        return d.RootElement.TryGetProperty(prop, out var p) && p.ValueKind == System.Text.Json.JsonValueKind.Number ? p.GetInt64() : 0;
    }
    private static List<long> ReadLongList(string json, string prop)
    {
        var result = new List<long>();
        using var d = System.Text.Json.JsonDocument.Parse(json);
        if (d.RootElement.TryGetProperty(prop, out var p) && p.ValueKind == System.Text.Json.JsonValueKind.Array)
            foreach (var i in p.EnumerateArray()) if (i.ValueKind == System.Text.Json.JsonValueKind.Number) result.Add(i.GetInt64());
        return result;
    }
    private static double ReadDouble(string json, string prop)
    {
        using var d = System.Text.Json.JsonDocument.Parse(json);
        return d.RootElement.TryGetProperty(prop, out var p) && p.ValueKind == System.Text.Json.JsonValueKind.Number ? p.GetDouble() : double.NaN;
    }
    private static string ReadString(string json, string prop)
    {
        using var d = System.Text.Json.JsonDocument.Parse(json);
        return d.RootElement.TryGetProperty(prop, out var p) && p.ValueKind == System.Text.Json.JsonValueKind.String ? p.GetString() ?? "" : "";
    }
}
