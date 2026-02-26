using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.Sqlite;

namespace TestingPlatform;

public static class EngineDb
{
    private static readonly string DbPath = System.IO.Path.Combine(AppContext.BaseDirectory, "app.db");
    private static readonly string ConnString = new SqliteConnectionStringBuilder { DataSource = DbPath }.ToString();

    public static SqliteConnection Open()
    {
        var c = new SqliteConnection(ConnString);
        c.Open();
        using var p = c.CreateCommand();
        p.CommandText = "PRAGMA foreign_keys = ON;";
        p.ExecuteNonQuery();
        return c;
    }

    public static void Init()
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        void Exec(string sql)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        Exec(@"CREATE TABLE IF NOT EXISTS Users(
Id INTEGER PRIMARY KEY,
Name TEXT NOT NULL,
Role TEXT NOT NULL
);");
        Exec(@"CREATE TABLE IF NOT EXISTS Groups(
Id INTEGER PRIMARY KEY,
Name TEXT NOT NULL UNIQUE
);");
        Exec(@"CREATE TABLE IF NOT EXISTS GroupMembers(
GroupId INTEGER NOT NULL,
UserId INTEGER NOT NULL,
PRIMARY KEY(GroupId,UserId),
FOREIGN KEY(GroupId) REFERENCES Groups(Id) ON DELETE CASCADE,
FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
);");
        Exec(@"CREATE TABLE IF NOT EXISTS Tests(
Id INTEGER PRIMARY KEY,
Title TEXT NOT NULL,
Description TEXT,
CreatedByTeacherId INTEGER NOT NULL,
Status TEXT NOT NULL,
PassPercent INTEGER NOT NULL DEFAULT 60,
CreatedAt TEXT NOT NULL,
FOREIGN KEY(CreatedByTeacherId) REFERENCES Users(Id)
);");
        Exec(@"CREATE TABLE IF NOT EXISTS Questions(
Id INTEGER PRIMARY KEY,
TestId INTEGER NOT NULL,
Type TEXT NOT NULL,
Text TEXT NOT NULL,
Points REAL NOT NULL,
SettingsJson TEXT NOT NULL,
CreatedAt TEXT NOT NULL,
FOREIGN KEY(TestId) REFERENCES Tests(Id) ON DELETE CASCADE
);");
        Exec(@"CREATE TABLE IF NOT EXISTS Options(
Id INTEGER PRIMARY KEY,
QuestionId INTEGER NOT NULL,
Text TEXT NOT NULL,
IsCorrect INTEGER NOT NULL DEFAULT 0,
SortOrder INTEGER NOT NULL DEFAULT 0,
FOREIGN KEY(QuestionId) REFERENCES Questions(Id) ON DELETE CASCADE
);");
        Exec(@"CREATE TABLE IF NOT EXISTS Assignments(
Id INTEGER PRIMARY KEY,
TestId INTEGER NOT NULL,
TargetType TEXT NOT NULL,
TargetId INTEGER NOT NULL,
AvailableFrom TEXT,
Deadline TEXT,
AttemptLimit INTEGER NOT NULL DEFAULT 1,
TimeLimitMinutes INTEGER,
ShuffleQuestions INTEGER NOT NULL DEFAULT 1,
ShuffleOptions INTEGER NOT NULL DEFAULT 1,
ShowScoreAfter INTEGER NOT NULL DEFAULT 1,
ShowCorrectAfter INTEGER NOT NULL DEFAULT 0,
IsActive INTEGER NOT NULL DEFAULT 1,
FOREIGN KEY(TestId) REFERENCES Tests(Id) ON DELETE CASCADE
);");
        Exec(@"CREATE TABLE IF NOT EXISTS Attempts(
Id INTEGER PRIMARY KEY,
AssignmentId INTEGER NOT NULL,
UserId INTEGER NOT NULL,
StartedAt TEXT NOT NULL,
EndsAt TEXT,
SubmittedAt TEXT,
Status TEXT NOT NULL,
SnapshotJson TEXT NOT NULL,
FOREIGN KEY(AssignmentId) REFERENCES Assignments(Id),
FOREIGN KEY(UserId) REFERENCES Users(Id)
);");
        Exec(@"CREATE TABLE IF NOT EXISTS AttemptAnswers(
Id INTEGER PRIMARY KEY,
AttemptId INTEGER NOT NULL,
QuestionId INTEGER NOT NULL,
AnswerJson TEXT NOT NULL,
SavedAt TEXT NOT NULL,
IsFinal INTEGER NOT NULL DEFAULT 1,
FOREIGN KEY(AttemptId) REFERENCES Attempts(Id) ON DELETE CASCADE
);");
        Exec(@"CREATE TABLE IF NOT EXISTS AttemptResults(
AttemptId INTEGER PRIMARY KEY,
Score REAL NOT NULL,
MaxScore REAL NOT NULL,
Percent REAL NOT NULL,
Passed INTEGER NOT NULL,
CheckedAt TEXT NOT NULL,
DetailsJson TEXT NOT NULL,
FOREIGN KEY(AttemptId) REFERENCES Attempts(Id) ON DELETE CASCADE
);");
        Exec(@"CREATE TABLE IF NOT EXISTS ManualChecks(
Id INTEGER PRIMARY KEY,
AttemptId INTEGER NOT NULL,
QuestionId INTEGER NOT NULL,
TeacherId INTEGER NOT NULL,
ScoreGiven REAL NOT NULL,
Comment TEXT,
CheckedAt TEXT NOT NULL,
FOREIGN KEY(AttemptId) REFERENCES Attempts(Id) ON DELETE CASCADE,
FOREIGN KEY(TeacherId) REFERENCES Users(Id)
);");
        Exec(@"CREATE TABLE IF NOT EXISTS AuditLog(
Id INTEGER PRIMARY KEY,
ActorUserId INTEGER NOT NULL,
Action TEXT NOT NULL,
EntityType TEXT NOT NULL,
EntityId INTEGER NOT NULL,
At TEXT NOT NULL,
MetaJson TEXT NOT NULL,
FOREIGN KEY(ActorUserId) REFERENCES Users(Id)
);");

        Exec("CREATE INDEX IF NOT EXISTS IX_Attempts_User_Assignment ON Attempts(UserId, AssignmentId);");
        Exec("CREATE INDEX IF NOT EXISTS IX_AttemptAnswers_AttemptId ON AttemptAnswers(AttemptId);");
        Exec("CREATE INDEX IF NOT EXISTS IX_Questions_TestId ON Questions(TestId);");
        Exec("CREATE INDEX IF NOT EXISTS IX_Assignments_TestId ON Assignments(TestId);");
        Exec("CREATE INDEX IF NOT EXISTS IX_GroupMembers_UserId ON GroupMembers(UserId);");

        tx.Commit();
        SeedIfEmpty();
    }

    public static void SeedIfEmpty()
    {
        using var c = Open();
        using var check = c.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM Users;";
        var count = Convert.ToInt64(check.ExecuteScalar());
        if (count > 0) return;

        using var tx = c.BeginTransaction();
        long AddUser(string n, UserRole r)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO Users(Name,Role) VALUES(@n,@r); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@n", n);
            cmd.Parameters.AddWithValue("@r", r.ToString());
            return (long)(cmd.ExecuteScalar() ?? 0L);
        }
        var admin = AddUser("admin", UserRole.Admin);
        var teacher = AddUser("teacher1", UserRole.Teacher);
        var s1 = AddUser("student1", UserRole.Student); var s2 = AddUser("student2", UserRole.Student); var s3 = AddUser("student3", UserRole.Student);
        var s4 = AddUser("student4", UserRole.Student); var s5 = AddUser("student5", UserRole.Student); var s6 = AddUser("student6", UserRole.Student);

        long AddGroup(string name)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO Groups(Name) VALUES(@n); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@n", name);
            return (long)(cmd.ExecuteScalar() ?? 0L);
        }
        var g1 = AddGroup("Group A"); var g2 = AddGroup("Group B");
        void AddMember(long g, long u)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO GroupMembers(GroupId,UserId) VALUES(@g,@u);";
            cmd.Parameters.AddWithValue("@g", g); cmd.Parameters.AddWithValue("@u", u);
            cmd.ExecuteNonQuery();
        }
        AddMember(g1, s1); AddMember(g1, s2); AddMember(g1, s3); AddMember(g2, s4); AddMember(g2, s5); AddMember(g2, s6);

        long t1;
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO Tests(Title,Description,CreatedByTeacherId,Status,PassPercent,CreatedAt) VALUES(@t,@d,@cb,@s,@p,@ca); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@t", "Network Basics");
            cmd.Parameters.AddWithValue("@d", "Demo published test");
            cmd.Parameters.AddWithValue("@cb", teacher);
            cmd.Parameters.AddWithValue("@s", TestStatus.Published.ToString());
            cmd.Parameters.AddWithValue("@p", 60);
            cmd.Parameters.AddWithValue("@ca", Clock.ToDb(Clock.UtcNow()));
            t1 = (long)(cmd.ExecuteScalar() ?? 0L);
        }
        long t2;
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO Tests(Title,Description,CreatedByTeacherId,Status,PassPercent,CreatedAt) VALUES(@t,@d,@cb,@s,@p,@ca); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@t", "Draft test"); cmd.Parameters.AddWithValue("@d", "Teacher draft");
            cmd.Parameters.AddWithValue("@cb", teacher); cmd.Parameters.AddWithValue("@s", TestStatus.Draft.ToString());
            cmd.Parameters.AddWithValue("@p", 70); cmd.Parameters.AddWithValue("@ca", Clock.ToDb(Clock.UtcNow()));
            t2 = (long)(cmd.ExecuteScalar() ?? 0L);
        }

        long q1 = AddQuestion(c, tx, t1, QuestionType.SingleChoice, "OSI has how many layers?", 2, "{}");
        AddOption(c, tx, q1, "5", false, 1); AddOption(c, tx, q1, "7", true, 2); AddOption(c, tx, q1, "9", false, 3);

        long q2 = AddQuestion(c, tx, t1, QuestionType.MultipleChoice, "Select transport protocols", 4, "{}");
        AddOption(c, tx, q2, "TCP", true, 1); AddOption(c, tx, q2, "UDP", true, 2); AddOption(c, tx, q2, "HTTP", false, 3); AddOption(c, tx, q2, "ICMP", false, 4);

        var txtSettings = JsonUtil.Serialize(new TextSettings { Accepted = new() { "tcp", "transmission control protocol" }, CaseInsensitive = true, Trim = true, AllowManualCheck = true });
        AddQuestion(c, tx, t1, QuestionType.Text, "Expand TCP", 3, txtSettings);

        var numSettings = JsonUtil.Serialize(new NumericSettings { Correct = 3.14, Tolerance = 0.01 });
        AddQuestion(c, tx, t1, QuestionType.Numeric, "Pi (2 decimals)", 1, numSettings);

        AddQuestion(c, tx, t2, QuestionType.Text, "Draft question", 1, txtSettings);

        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO Assignments(TestId,TargetType,TargetId,AvailableFrom,Deadline,AttemptLimit,TimeLimitMinutes,ShuffleQuestions,ShuffleOptions,ShowScoreAfter,ShowCorrectAfter,IsActive)
VALUES(@t,'Group',@gid,@af,@dl,2,30,1,1,1,0,1);";
            cmd.Parameters.AddWithValue("@t", t1);
            cmd.Parameters.AddWithValue("@gid", g1);
            cmd.Parameters.AddWithValue("@af", Clock.ToDb(Clock.UtcNow().AddDays(-1)));
            cmd.Parameters.AddWithValue("@dl", Clock.ToDb(Clock.UtcNow().AddDays(30)));
            cmd.ExecuteNonQuery();
        }

        InsertAudit(c, tx, admin, "Seed", "System", 1, "{}");
        tx.Commit();
    }

    private static long AddQuestion(SqliteConnection c, SqliteTransaction tx, long t, QuestionType type, string text, double pts, string settings)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO Questions(TestId,Type,Text,Points,SettingsJson,CreatedAt) VALUES(@t,@ty,@tx,@p,@s,@ca); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@t", t); cmd.Parameters.AddWithValue("@ty", type.ToString()); cmd.Parameters.AddWithValue("@tx", text);
        cmd.Parameters.AddWithValue("@p", pts); cmd.Parameters.AddWithValue("@s", settings); cmd.Parameters.AddWithValue("@ca", Clock.ToDb(Clock.UtcNow()));
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    private static void AddOption(SqliteConnection c, SqliteTransaction tx, long q, string text, bool corr, int sort)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO Options(QuestionId,Text,IsCorrect,SortOrder) VALUES(@q,@t,@c,@s);";
        cmd.Parameters.AddWithValue("@q", q); cmd.Parameters.AddWithValue("@t", text); cmd.Parameters.AddWithValue("@c", corr ? 1 : 0); cmd.Parameters.AddWithValue("@s", sort);
        cmd.ExecuteNonQuery();
    }

    public static List<User> GetUsers()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,Name,Role FROM Users ORDER BY Name;";
        using var r = cmd.ExecuteReader();
        var list = new List<User>();
        while (r.Read()) list.Add(new User { Id = r.GetInt64(0), Name = r.GetString(1), Role = Enum.Parse<UserRole>(r.GetString(2)) });
        return list;
    }

    public static User GetUser(long id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,Name,Role FROM Users WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        Guard.True(r.Read(), "User not found");
        return new User { Id = r.GetInt64(0), Name = r.GetString(1), Role = Enum.Parse<UserRole>(r.GetString(2)) };
    }

    public static long AddUser(string name, UserRole role, long actorId)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO Users(Name,Role) VALUES(@n,@r); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@n", name); cmd.Parameters.AddWithValue("@r", role.ToString());
        var id = (long)(cmd.ExecuteScalar() ?? 0L);
        InsertAudit(c, tx, actorId, "Create", "User", id, JsonUtil.Serialize(new { name, role = role.ToString() }));
        tx.Commit();
        return id;
    }

    public static void DeleteUser(long userId, long actorId)
    {
        using var c = Open(); using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM Users WHERE Id=@id;"; cmd.Parameters.AddWithValue("@id", userId); cmd.ExecuteNonQuery();
        InsertAudit(c, tx, actorId, "Delete", "User", userId, "{}"); tx.Commit();
    }

    public static List<Group> GetGroups()
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT Id,Name FROM Groups ORDER BY Name;";
        using var r = cmd.ExecuteReader(); var list = new List<Group>();
        while (r.Read()) list.Add(new Group { Id = r.GetInt64(0), Name = r.GetString(1) });
        return list;
    }

    public static long AddGroup(string name, long actorId)
    {
        using var c = Open(); using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO Groups(Name) VALUES(@n); SELECT last_insert_rowid();"; cmd.Parameters.AddWithValue("@n", name);
        var id = (long)(cmd.ExecuteScalar() ?? 0L); InsertAudit(c, tx, actorId, "Create", "Group", id, JsonUtil.Serialize(new { name })); tx.Commit(); return id;
    }

    public static void SetGroupMember(long groupId, long userId, bool add, long actorId)
    {
        using var c = Open(); using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = add ? "INSERT OR IGNORE INTO GroupMembers(GroupId,UserId) VALUES(@g,@u);" : "DELETE FROM GroupMembers WHERE GroupId=@g AND UserId=@u;";
        cmd.Parameters.AddWithValue("@g", groupId); cmd.Parameters.AddWithValue("@u", userId); cmd.ExecuteNonQuery();
        InsertAudit(c, tx, actorId, add ? "AddMember" : "RemoveMember", "Group", groupId, JsonUtil.Serialize(new { userId })); tx.Commit();
    }

    public static List<long> GetGroupIdsForUser(long userId)
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT GroupId FROM GroupMembers WHERE UserId=@u;"; cmd.Parameters.AddWithValue("@u", userId);
        using var r = cmd.ExecuteReader(); var list = new List<long>(); while (r.Read()) list.Add(r.GetInt64(0)); return list;
    }

    public static List<TestEntity> GetTestsByTeacher(long teacherId)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,Title,Description,CreatedByTeacherId,Status,PassPercent,CreatedAt FROM Tests WHERE CreatedByTeacherId=@t ORDER BY Id DESC;";
        cmd.Parameters.AddWithValue("@t", teacherId); return ReadTests(cmd);
    }

    public static List<TestEntity> ReadTests(SqliteCommand cmd)
    {
        using var r = cmd.ExecuteReader(); var list = new List<TestEntity>();
        while (r.Read()) list.Add(new TestEntity { Id = r.GetInt64(0), Title = r.GetString(1), Description = r.IsDBNull(2) ? null : r.GetString(2), CreatedByTeacherId = r.GetInt64(3), Status = Enum.Parse<TestStatus>(r.GetString(4)), PassPercent = r.GetInt32(5), CreatedAt = r.GetString(6) });
        return list;
    }

    public static TestEntity GetTest(long id)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,Title,Description,CreatedByTeacherId,Status,PassPercent,CreatedAt FROM Tests WHERE Id=@id;"; cmd.Parameters.AddWithValue("@id", id);
        var list = ReadTests(cmd); Guard.True(list.Count == 1, "Test not found"); return list[0];
    }

    public static long AddTest(TestEntity t, long actorId)
    {
        using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO Tests(Title,Description,CreatedByTeacherId,Status,PassPercent,CreatedAt) VALUES(@ti,@d,@c,@s,@p,@ca); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@ti", t.Title); cmd.Parameters.AddWithValue("@d", (object?)t.Description ?? DBNull.Value); cmd.Parameters.AddWithValue("@c", t.CreatedByTeacherId);
        cmd.Parameters.AddWithValue("@s", t.Status.ToString()); cmd.Parameters.AddWithValue("@p", t.PassPercent); cmd.Parameters.AddWithValue("@ca", Clock.ToDb(Clock.UtcNow()));
        var id = (long)(cmd.ExecuteScalar() ?? 0L); InsertAudit(c, tx, actorId, "Create", "Test", id, JsonUtil.Serialize(new { t.Title })); tx.Commit(); return id;
    }

    public static void UpdateTestStatus(long testId, TestStatus status, long actorId)
    {
        using var c = Open(); using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "UPDATE Tests SET Status=@s WHERE Id=@id;"; cmd.Parameters.AddWithValue("@s", status.ToString()); cmd.Parameters.AddWithValue("@id", testId); cmd.ExecuteNonQuery();
        InsertAudit(c, tx, actorId, "Status", "Test", testId, JsonUtil.Serialize(new { status = status.ToString() })); tx.Commit();
    }

    public static long CloneTestToDraft(long testId, long actorId)
    {
        var src = GetTest(testId);
        using var c = Open(); using var tx = c.BeginTransaction();
        long newTestId;
        using (var ins = c.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO Tests(Title,Description,CreatedByTeacherId,Status,PassPercent,CreatedAt) VALUES(@t,@d,@c,'Draft',@p,@ca); SELECT last_insert_rowid();";
            ins.Parameters.AddWithValue("@t", src.Title + " (copy)"); ins.Parameters.AddWithValue("@d", (object?)src.Description ?? DBNull.Value); ins.Parameters.AddWithValue("@c", src.CreatedByTeacherId); ins.Parameters.AddWithValue("@p", src.PassPercent); ins.Parameters.AddWithValue("@ca", Clock.ToDb(Clock.UtcNow()));
            newTestId = (long)(ins.ExecuteScalar() ?? 0L);
        }
        var questions = GetQuestions(testId);
        foreach (var q in questions)
        {
            long nq;
            using (var iq = c.CreateCommand())
            {
                iq.Transaction = tx; iq.CommandText = "INSERT INTO Questions(TestId,Type,Text,Points,SettingsJson,CreatedAt) VALUES(@t,@ty,@x,@p,@s,@ca); SELECT last_insert_rowid();";
                iq.Parameters.AddWithValue("@t", newTestId); iq.Parameters.AddWithValue("@ty", q.Type.ToString()); iq.Parameters.AddWithValue("@x", q.Text); iq.Parameters.AddWithValue("@p", q.Points); iq.Parameters.AddWithValue("@s", q.SettingsJson); iq.Parameters.AddWithValue("@ca", Clock.ToDb(Clock.UtcNow()));
                nq = (long)(iq.ExecuteScalar() ?? 0L);
            }
            foreach (var op in GetOptions(q.Id))
            {
                using var io = c.CreateCommand(); io.Transaction = tx;
                io.CommandText = "INSERT INTO Options(QuestionId,Text,IsCorrect,SortOrder) VALUES(@q,@t,@c,@s);";
                io.Parameters.AddWithValue("@q", nq); io.Parameters.AddWithValue("@t", op.Text); io.Parameters.AddWithValue("@c", op.IsCorrect ? 1 : 0); io.Parameters.AddWithValue("@s", op.SortOrder); io.ExecuteNonQuery();
            }
        }
        InsertAudit(c, tx, actorId, "Clone", "Test", newTestId, JsonUtil.Serialize(new { from = testId })); tx.Commit(); return newTestId;
    }

    public static List<QuestionEntity> GetQuestions(long testId)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,TestId,Type,Text,Points,SettingsJson,CreatedAt FROM Questions WHERE TestId=@t ORDER BY Id;"; cmd.Parameters.AddWithValue("@t", testId);
        using var r = cmd.ExecuteReader(); var list = new List<QuestionEntity>();
        while (r.Read()) list.Add(new QuestionEntity { Id = r.GetInt64(0), TestId = r.GetInt64(1), Type = Enum.Parse<QuestionType>(r.GetString(2)), Text = r.GetString(3), Points = r.GetDouble(4), SettingsJson = r.GetString(5), CreatedAt = r.GetString(6) });
        return list;
    }

    public static List<OptionEntity> GetOptions(long questionId)
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT Id,QuestionId,Text,IsCorrect,SortOrder FROM Options WHERE QuestionId=@q ORDER BY SortOrder, Id;"; cmd.Parameters.AddWithValue("@q", questionId);
        using var r = cmd.ExecuteReader(); var list = new List<OptionEntity>(); while (r.Read()) list.Add(new OptionEntity { Id = r.GetInt64(0), QuestionId = r.GetInt64(1), Text = r.GetString(2), IsCorrect = r.GetInt32(3) == 1, SortOrder = r.GetInt32(4) }); return list;
    }

    public static long AddQuestionWithOptions(QuestionEntity q, List<OptionEntity> options, long actorId)
    {
        var test = GetTest(q.TestId);
        Guard.True(test.Status == TestStatus.Draft, "Published/Archived test locked. Clone or move to Draft.");
        using var c = Open(); using var tx = c.BeginTransaction();
        long qid;
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx; cmd.CommandText = "INSERT INTO Questions(TestId,Type,Text,Points,SettingsJson,CreatedAt) VALUES(@t,@ty,@tx,@p,@s,@ca); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@t", q.TestId); cmd.Parameters.AddWithValue("@ty", q.Type.ToString()); cmd.Parameters.AddWithValue("@tx", q.Text); cmd.Parameters.AddWithValue("@p", q.Points); cmd.Parameters.AddWithValue("@s", q.SettingsJson); cmd.Parameters.AddWithValue("@ca", Clock.ToDb(Clock.UtcNow()));
            qid = (long)(cmd.ExecuteScalar() ?? 0L);
        }
        foreach (var op in options)
        {
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO Options(QuestionId,Text,IsCorrect,SortOrder) VALUES(@q,@t,@c,@s);";
            cmd.Parameters.AddWithValue("@q", qid); cmd.Parameters.AddWithValue("@t", op.Text); cmd.Parameters.AddWithValue("@c", op.IsCorrect ? 1 : 0); cmd.Parameters.AddWithValue("@s", op.SortOrder); cmd.ExecuteNonQuery();
        }
        InsertAudit(c, tx, actorId, "Create", "Question", qid, JsonUtil.Serialize(new { q.TestId, q.Type })); tx.Commit(); return qid;
    }

    public static List<Assignment> GetAssignmentsByTest(long testId)
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT Id,TestId,TargetType,TargetId,AvailableFrom,Deadline,AttemptLimit,TimeLimitMinutes,ShuffleQuestions,ShuffleOptions,ShowScoreAfter,ShowCorrectAfter,IsActive FROM Assignments WHERE TestId=@t ORDER BY Id DESC;"; cmd.Parameters.AddWithValue("@t", testId); return ReadAssignments(cmd);
    }

    public static Assignment GetAssignment(long id)
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT Id,TestId,TargetType,TargetId,AvailableFrom,Deadline,AttemptLimit,TimeLimitMinutes,ShuffleQuestions,ShuffleOptions,ShowScoreAfter,ShowCorrectAfter,IsActive FROM Assignments WHERE Id=@id;"; cmd.Parameters.AddWithValue("@id", id);
        var list = ReadAssignments(cmd); Guard.True(list.Count == 1, "Assignment not found"); return list[0];
    }

    private static List<Assignment> ReadAssignments(SqliteCommand cmd)
    {
        using var r = cmd.ExecuteReader(); var list = new List<Assignment>();
        while (r.Read()) list.Add(new Assignment { Id = r.GetInt64(0), TestId = r.GetInt64(1), TargetType = Enum.Parse<AssignmentTargetType>(r.GetString(2)), TargetId = r.GetInt64(3), AvailableFrom = r.IsDBNull(4) ? null : r.GetString(4), Deadline = r.IsDBNull(5) ? null : r.GetString(5), AttemptLimit = r.GetInt32(6), TimeLimitMinutes = r.IsDBNull(7) ? null : r.GetInt32(7), ShuffleQuestions = r.GetInt32(8) == 1, ShuffleOptions = r.GetInt32(9) == 1, ShowScoreAfter = r.GetInt32(10) == 1, ShowCorrectAfter = r.GetInt32(11) == 1, IsActive = r.GetInt32(12) == 1 });
        return list;
    }

    public static long AddAssignment(Assignment a, long actorId)
    {
        using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO Assignments(TestId,TargetType,TargetId,AvailableFrom,Deadline,AttemptLimit,TimeLimitMinutes,ShuffleQuestions,ShuffleOptions,ShowScoreAfter,ShowCorrectAfter,IsActive)
VALUES(@t,@tt,@tid,@af,@dl,@al,@tm,@sq,@so,@ss,@sc,@ia); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@t", a.TestId); cmd.Parameters.AddWithValue("@tt", a.TargetType.ToString()); cmd.Parameters.AddWithValue("@tid", a.TargetId);
        cmd.Parameters.AddWithValue("@af", (object?)a.AvailableFrom ?? DBNull.Value); cmd.Parameters.AddWithValue("@dl", (object?)a.Deadline ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@al", a.AttemptLimit); cmd.Parameters.AddWithValue("@tm", (object?)a.TimeLimitMinutes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sq", a.ShuffleQuestions ? 1 : 0); cmd.Parameters.AddWithValue("@so", a.ShuffleOptions ? 1 : 0);
        cmd.Parameters.AddWithValue("@ss", a.ShowScoreAfter ? 1 : 0); cmd.Parameters.AddWithValue("@sc", a.ShowCorrectAfter ? 1 : 0); cmd.Parameters.AddWithValue("@ia", a.IsActive ? 1 : 0);
        var id = (long)(cmd.ExecuteScalar() ?? 0L); InsertAudit(c, tx, actorId, "Create", "Assignment", id, JsonUtil.Serialize(new { a.TestId, a.TargetType, a.TargetId })); tx.Commit(); return id;
    }

    public static Attempt GetAttempt(long attemptId)
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT Id,AssignmentId,UserId,StartedAt,EndsAt,SubmittedAt,Status,SnapshotJson FROM Attempts WHERE Id=@id;"; cmd.Parameters.AddWithValue("@id", attemptId);
        using var r = cmd.ExecuteReader(); Guard.True(r.Read(), "Attempt not found");
        return new Attempt { Id = r.GetInt64(0), AssignmentId = r.GetInt64(1), UserId = r.GetInt64(2), StartedAt = r.GetString(3), EndsAt = r.IsDBNull(4) ? null : r.GetString(4), SubmittedAt = r.IsDBNull(5) ? null : r.GetString(5), Status = Enum.Parse<AttemptStatus>(r.GetString(6)), SnapshotJson = r.GetString(7) };
    }

    public static List<Attempt> GetAttemptsByAssignmentAndUser(long assignmentId, long userId)
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT Id,AssignmentId,UserId,StartedAt,EndsAt,SubmittedAt,Status,SnapshotJson FROM Attempts WHERE AssignmentId=@a AND UserId=@u ORDER BY Id;";
        cmd.Parameters.AddWithValue("@a", assignmentId); cmd.Parameters.AddWithValue("@u", userId);
        using var r = cmd.ExecuteReader(); var list = new List<Attempt>();
        while (r.Read()) list.Add(new Attempt { Id = r.GetInt64(0), AssignmentId = r.GetInt64(1), UserId = r.GetInt64(2), StartedAt = r.GetString(3), EndsAt = r.IsDBNull(4) ? null : r.GetString(4), SubmittedAt = r.IsDBNull(5) ? null : r.GetString(5), Status = Enum.Parse<AttemptStatus>(r.GetString(6)), SnapshotJson = r.GetString(7) });
        return list;
    }

    public static List<Attempt> GetAttemptsByTest(long testId)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT a.Id,a.AssignmentId,a.UserId,a.StartedAt,a.EndsAt,a.SubmittedAt,a.Status,a.SnapshotJson
FROM Attempts a JOIN Assignments s ON s.Id=a.AssignmentId WHERE s.TestId=@t ORDER BY a.Id DESC;";
        cmd.Parameters.AddWithValue("@t", testId);
        using var r = cmd.ExecuteReader(); var list = new List<Attempt>();
        while (r.Read()) list.Add(new Attempt { Id = r.GetInt64(0), AssignmentId = r.GetInt64(1), UserId = r.GetInt64(2), StartedAt = r.GetString(3), EndsAt = r.IsDBNull(4) ? null : r.GetString(4), SubmittedAt = r.IsDBNull(5) ? null : r.GetString(5), Status = Enum.Parse<AttemptStatus>(r.GetString(6)), SnapshotJson = r.GetString(7) });
        return list;
    }

    public static AttemptResult? GetAttemptResult(long attemptId)
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT AttemptId,Score,MaxScore,Percent,Passed,CheckedAt,DetailsJson FROM AttemptResults WHERE AttemptId=@a;"; cmd.Parameters.AddWithValue("@a", attemptId);
        using var r = cmd.ExecuteReader(); if (!r.Read()) return null;
        return new AttemptResult { AttemptId = r.GetInt64(0), Score = r.GetDouble(1), MaxScore = r.GetDouble(2), Percent = r.GetDouble(3), Passed = r.GetInt32(4) == 1, CheckedAt = r.GetString(5), DetailsJson = r.GetString(6) };
    }

    public static Dictionary<long, string> GetUserNamesMap()
    {
        var m = new Dictionary<long, string>();
        foreach (var u in GetUsers()) m[u.Id] = u.Name;
        return m;
    }

    public static List<AuditEvent> GetAuditLast(int n)
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT Id,ActorUserId,Action,EntityType,EntityId,At,MetaJson FROM AuditLog ORDER BY Id DESC LIMIT @n;"; cmd.Parameters.AddWithValue("@n", n);
        using var r = cmd.ExecuteReader(); var list = new List<AuditEvent>();
        while (r.Read()) list.Add(new AuditEvent { Id = r.GetInt64(0), ActorUserId = r.GetInt64(1), Action = r.GetString(2), EntityType = r.GetString(3), EntityId = r.GetInt64(4), At = r.GetString(5), MetaJson = r.GetString(6) });
        return list;
    }

    public static void InsertAudit(SqliteConnection c, SqliteTransaction tx, long actorId, string action, string entity, long entityId, string meta)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "INSERT INTO AuditLog(ActorUserId,Action,EntityType,EntityId,At,MetaJson) VALUES(@a,@ac,@e,@id,@at,@m);";
        cmd.Parameters.AddWithValue("@a", actorId); cmd.Parameters.AddWithValue("@ac", action); cmd.Parameters.AddWithValue("@e", entity); cmd.Parameters.AddWithValue("@id", entityId); cmd.Parameters.AddWithValue("@at", Clock.ToDb(Clock.UtcNow())); cmd.Parameters.AddWithValue("@m", meta); cmd.ExecuteNonQuery();
    }
}
