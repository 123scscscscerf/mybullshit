using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace PlatformApp;

public static class EngineDb
{
    private static readonly string DbPath = Path.Combine(AppContext.BaseDirectory, "app.db");
    private static readonly string Cn = new SqliteConnectionStringBuilder { DataSource = DbPath }.ToString();

    public static SqliteConnection Open()
    {
        var c = new SqliteConnection(Cn);
        c.Open();
        using var p = c.CreateCommand(); p.CommandText = "PRAGMA foreign_keys=ON;"; p.ExecuteNonQuery();
        return c;
    }

    public static void Init()
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        void Exec(string sql) { using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql; cmd.ExecuteNonQuery(); }

        Exec(@"CREATE TABLE IF NOT EXISTS Users(
Id INTEGER PRIMARY KEY,
Login TEXT NOT NULL UNIQUE,
DisplayName TEXT NOT NULL,
Role TEXT NOT NULL,
PasswordSalt BLOB NOT NULL,
PasswordHash BLOB NOT NULL,
IsActive INTEGER NOT NULL DEFAULT 1,
CreatedAt TEXT NOT NULL
);");
        Exec(@"CREATE TABLE IF NOT EXISTS Groups(Id INTEGER PRIMARY KEY,Name TEXT NOT NULL UNIQUE);");
        Exec(@"CREATE TABLE IF NOT EXISTS GroupMembers(GroupId INTEGER NOT NULL,UserId INTEGER NOT NULL,PRIMARY KEY(GroupId,UserId),FOREIGN KEY(GroupId) REFERENCES Groups(Id) ON DELETE CASCADE,FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE);");
        Exec(@"CREATE TABLE IF NOT EXISTS Tests(Id INTEGER PRIMARY KEY,Title TEXT NOT NULL,Description TEXT NOT NULL,CreatedByTeacherId INTEGER NOT NULL,Status TEXT NOT NULL,PassPercent INTEGER NOT NULL DEFAULT 60,DefaultAttemptLimit INTEGER NOT NULL DEFAULT 1,DefaultTimeLimitMinutes INTEGER,DefaultShuffleQuestions INTEGER NOT NULL DEFAULT 1,DefaultShuffleOptions INTEGER NOT NULL DEFAULT 1,DefaultShowScoreAfter INTEGER NOT NULL DEFAULT 1,DefaultShowCorrectAfter INTEGER NOT NULL DEFAULT 0,CreatedAt TEXT NOT NULL,FOREIGN KEY(CreatedByTeacherId) REFERENCES Users(Id));");
        Exec(@"CREATE TABLE IF NOT EXISTS Questions(Id INTEGER PRIMARY KEY,TestId INTEGER NOT NULL,Type TEXT NOT NULL,Text TEXT NOT NULL,Points REAL NOT NULL,SettingsJson TEXT NOT NULL,FOREIGN KEY(TestId) REFERENCES Tests(Id) ON DELETE CASCADE);");
        Exec(@"CREATE TABLE IF NOT EXISTS Options(Id INTEGER PRIMARY KEY,QuestionId INTEGER NOT NULL,Text TEXT NOT NULL,IsCorrect INTEGER NOT NULL DEFAULT 0,SortOrder INTEGER NOT NULL DEFAULT 0,FOREIGN KEY(QuestionId) REFERENCES Questions(Id) ON DELETE CASCADE);");
        Exec(@"CREATE TABLE IF NOT EXISTS Assignments(Id INTEGER PRIMARY KEY,TestId INTEGER NOT NULL,TargetType TEXT NOT NULL,TargetId INTEGER NOT NULL,AvailableFrom TEXT,Deadline TEXT,AttemptLimit INTEGER NOT NULL DEFAULT 1,TimeLimitMinutes INTEGER,ShuffleQuestions INTEGER NOT NULL DEFAULT 1,ShuffleOptions INTEGER NOT NULL DEFAULT 1,ShowScoreAfter INTEGER NOT NULL DEFAULT 1,ShowCorrectAfter INTEGER NOT NULL DEFAULT 0,IsActive INTEGER NOT NULL DEFAULT 1,FOREIGN KEY(TestId) REFERENCES Tests(Id) ON DELETE CASCADE);");
        Exec(@"CREATE TABLE IF NOT EXISTS Attempts(Id INTEGER PRIMARY KEY,AssignmentId INTEGER NOT NULL,UserId INTEGER NOT NULL,StartedAt TEXT NOT NULL,EndsAt TEXT,SubmittedAt TEXT,Status TEXT NOT NULL,SnapshotJson TEXT NOT NULL,FOREIGN KEY(AssignmentId) REFERENCES Assignments(Id),FOREIGN KEY(UserId) REFERENCES Users(Id));");
        Exec(@"CREATE TABLE IF NOT EXISTS AttemptAnswers(Id INTEGER PRIMARY KEY,AttemptId INTEGER NOT NULL,QuestionId INTEGER NOT NULL,AnswerJson TEXT NOT NULL,SavedAt TEXT NOT NULL,FOREIGN KEY(AttemptId) REFERENCES Attempts(Id) ON DELETE CASCADE);");
        Exec(@"CREATE TABLE IF NOT EXISTS AttemptResults(AttemptId INTEGER PRIMARY KEY,Score REAL NOT NULL,MaxScore REAL NOT NULL,Percent REAL NOT NULL,Passed INTEGER NOT NULL,CheckedAt TEXT NOT NULL,DetailsJson TEXT NOT NULL,FOREIGN KEY(AttemptId) REFERENCES Attempts(Id) ON DELETE CASCADE);");
        Exec(@"CREATE TABLE IF NOT EXISTS ManualChecks(Id INTEGER PRIMARY KEY,AttemptId INTEGER NOT NULL,QuestionId INTEGER NOT NULL,TeacherId INTEGER NOT NULL,ScoreGiven REAL NOT NULL,Comment TEXT,CheckedAt TEXT NOT NULL,FOREIGN KEY(AttemptId) REFERENCES Attempts(Id) ON DELETE CASCADE,FOREIGN KEY(TeacherId) REFERENCES Users(Id));");
        Exec(@"CREATE TABLE IF NOT EXISTS AuditLog(Id INTEGER PRIMARY KEY,ActorUserId INTEGER NOT NULL,Action TEXT NOT NULL,EntityType TEXT NOT NULL,EntityId INTEGER NOT NULL,At TEXT NOT NULL,MetaJson TEXT NOT NULL,FOREIGN KEY(ActorUserId) REFERENCES Users(Id));");

        Exec("CREATE INDEX IF NOT EXISTS IX_Attempts_User_Assignment ON Attempts(UserId,AssignmentId);");
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
        using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM Users";
        if (Convert.ToInt64(cmd.ExecuteScalar()) > 0) return;
        using var tx = c.BeginTransaction();

        long AddUser(string login, string name, UserRole role, string pwd)
        {
            var sec = Security.HashPassword(pwd);
            using var i = c.CreateCommand(); i.Transaction = tx;
            i.CommandText = "INSERT INTO Users(Login,DisplayName,Role,PasswordSalt,PasswordHash,IsActive,CreatedAt) VALUES(@l,@d,@r,@s,@h,1,@at); SELECT last_insert_rowid();";
            i.Parameters.AddWithValue("@l", login); i.Parameters.AddWithValue("@d", name); i.Parameters.AddWithValue("@r", role.ToString()); i.Parameters.Add("@s", SqliteType.Blob).Value = sec.Salt; i.Parameters.Add("@h", SqliteType.Blob).Value = sec.Hash; i.Parameters.AddWithValue("@at", TimeUtil.Iso(TimeUtil.UtcNow));
            return (long)(i.ExecuteScalar() ?? 0L);
        }

        var admin = AddUser("admin", "Administrator", UserRole.Admin, "admin123");
        var teacher = AddUser("teacher1", "Teacher One", UserRole.Teacher, "teacher123");
        var students = Enumerable.Range(1, 6).Select(i => AddUser($"student{i}", $"Student {i}", UserRole.Student, "student123")).ToList();

        long gA, gB;
        using (var g = c.CreateCommand()) { g.Transaction = tx; g.CommandText = "INSERT INTO Groups(Name) VALUES('Group A'); SELECT last_insert_rowid();"; gA = (long)(g.ExecuteScalar() ?? 0L); }
        using (var g = c.CreateCommand()) { g.Transaction = tx; g.CommandText = "INSERT INTO Groups(Name) VALUES('Group B'); SELECT last_insert_rowid();"; gB = (long)(g.ExecuteScalar() ?? 0L); }
        for (int i = 0; i < students.Count; i++)
        {
            using var gm = c.CreateCommand(); gm.Transaction = tx;
            gm.CommandText = "INSERT INTO GroupMembers(GroupId,UserId) VALUES(@g,@u)";
            gm.Parameters.AddWithValue("@g", i < 3 ? gA : gB); gm.Parameters.AddWithValue("@u", students[i]); gm.ExecuteNonQuery();
        }

        long testId;
        using (var t = c.CreateCommand())
        {
            t.Transaction = tx;
            t.CommandText = "INSERT INTO Tests(Title,Description,CreatedByTeacherId,Status,PassPercent,DefaultAttemptLimit,DefaultTimeLimitMinutes,DefaultShuffleQuestions,DefaultShuffleOptions,DefaultShowScoreAfter,DefaultShowCorrectAfter,CreatedAt) VALUES(@t,@d,@cb,'Published',60,2,30,1,1,1,0,@at); SELECT last_insert_rowid();";
            t.Parameters.AddWithValue("@t", "Network Basics"); t.Parameters.AddWithValue("@d", "Demo test"); t.Parameters.AddWithValue("@cb", teacher); t.Parameters.AddWithValue("@at", TimeUtil.Iso(TimeUtil.UtcNow));
            testId = (long)(t.ExecuteScalar() ?? 0L);
        }

        long AddQ(QuestionType qt, string text, double pts, string settings)
        {
            using var q = c.CreateCommand(); q.Transaction = tx;
            q.CommandText = "INSERT INTO Questions(TestId,Type,Text,Points,SettingsJson) VALUES(@t,@ty,@tx,@p,@s); SELECT last_insert_rowid();";
            q.Parameters.AddWithValue("@t", testId); q.Parameters.AddWithValue("@ty", qt.ToString()); q.Parameters.AddWithValue("@tx", text); q.Parameters.AddWithValue("@p", pts); q.Parameters.AddWithValue("@s", settings);
            return (long)(q.ExecuteScalar() ?? 0L);
        }
        void AddO(long qid, string txt, bool crr, int sort) { using var o = c.CreateCommand(); o.Transaction = tx; o.CommandText = "INSERT INTO Options(QuestionId,Text,IsCorrect,SortOrder) VALUES(@q,@t,@c,@s)"; o.Parameters.AddWithValue("@q", qid); o.Parameters.AddWithValue("@t", txt); o.Parameters.AddWithValue("@c", crr ? 1 : 0); o.Parameters.AddWithValue("@s", sort); o.ExecuteNonQuery(); }

        var q1 = AddQ(QuestionType.SingleChoice, "How many layers in OSI?", 2, "{}"); AddO(q1, "5", false, 1); AddO(q1, "7", true, 2); AddO(q1, "9", false, 3); AddO(q1, "6", false, 4);
        var q2 = AddQ(QuestionType.MultipleChoice, "Select transport protocols", 4, "{}"); AddO(q2, "TCP", true, 1); AddO(q2, "UDP", true, 2); AddO(q2, "HTTP", false, 3); AddO(q2, "ARP", false, 4);
        AddQ(QuestionType.Text, "Expand TCP", 2, JsonUtil.To(new TextSettings { Accepted = new() { "tcp", "transmission control protocol" } }));
        AddQ(QuestionType.Numeric, "Pi ~ ?", 2, JsonUtil.To(new NumericSettings { Correct = 3.14, Tolerance = 0.02 }));

        using (var a = c.CreateCommand())
        {
            a.Transaction = tx;
            a.CommandText = "INSERT INTO Assignments(TestId,TargetType,TargetId,AvailableFrom,Deadline,AttemptLimit,TimeLimitMinutes,ShuffleQuestions,ShuffleOptions,ShowScoreAfter,ShowCorrectAfter,IsActive) VALUES(@t,'Group',@gid,@af,@dl,2,30,1,1,1,0,1)";
            a.Parameters.AddWithValue("@t", testId); a.Parameters.AddWithValue("@gid", gA); a.Parameters.AddWithValue("@af", TimeUtil.Iso(TimeUtil.UtcNow.AddDays(-1))); a.Parameters.AddWithValue("@dl", TimeUtil.Iso(TimeUtil.UtcNow.AddDays(30))); a.ExecuteNonQuery();
        }

        InsertAudit(c, tx, admin, "seed", "system", 1, "{}");
        tx.Commit();
    }

    public static void InsertAudit(SqliteConnection c, SqliteTransaction tx, long actorId, string action, string type, long id, string meta)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO AuditLog(ActorUserId,Action,EntityType,EntityId,At,MetaJson) VALUES(@a,@ac,@et,@id,@at,@m)";
        cmd.Parameters.AddWithValue("@a", actorId); cmd.Parameters.AddWithValue("@ac", action); cmd.Parameters.AddWithValue("@et", type); cmd.Parameters.AddWithValue("@id", id); cmd.Parameters.AddWithValue("@at", TimeUtil.Iso(TimeUtil.UtcNow)); cmd.Parameters.AddWithValue("@m", meta); cmd.ExecuteNonQuery();
    }

    public static User? FindByLogin(string login)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,Login,DisplayName,Role,PasswordSalt,PasswordHash,IsActive,CreatedAt FROM Users WHERE Login=@l"; cmd.Parameters.AddWithValue("@l", login);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new User { Id = r.GetInt64(0), Login = r.GetString(1), DisplayName = r.GetString(2), Role = Enum.Parse<UserRole>(r.GetString(3)), PasswordSalt = (byte[])r[4], PasswordHash = (byte[])r[5], IsActive = r.GetInt32(6) == 1, CreatedAt = r.GetString(7) };
    }

    public static List<User> GetUsers() { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT Id,Login,DisplayName,Role,PasswordSalt,PasswordHash,IsActive,CreatedAt FROM Users ORDER BY Login"; using var r = cmd.ExecuteReader(); var list = new List<User>(); while (r.Read()) list.Add(new User { Id = r.GetInt64(0), Login = r.GetString(1), DisplayName = r.GetString(2), Role = Enum.Parse<UserRole>(r.GetString(3)), PasswordSalt = (byte[])r[4], PasswordHash = (byte[])r[5], IsActive = r.GetInt32(6) == 1, CreatedAt = r.GetString(7) }); return list; }

    public static long AddUser(string login, string displayName, UserRole role, string password, long actor)
    {
        var sec = Security.HashPassword(password);
        using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO Users(Login,DisplayName,Role,PasswordSalt,PasswordHash,IsActive,CreatedAt) VALUES(@l,@d,@r,@s,@h,1,@at); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@l", login); cmd.Parameters.AddWithValue("@d", displayName); cmd.Parameters.AddWithValue("@r", role.ToString()); cmd.Parameters.Add("@s", SqliteType.Blob).Value = sec.Salt; cmd.Parameters.Add("@h", SqliteType.Blob).Value = sec.Hash; cmd.Parameters.AddWithValue("@at", TimeUtil.Iso(TimeUtil.UtcNow));
        var id = (long)(cmd.ExecuteScalar() ?? 0L); InsertAudit(c, tx, actor, "user.add", "User", id, JsonUtil.To(new { login, role })); tx.Commit(); return id;
    }

    public static void UpdateUser(long id, string displayName, UserRole role, bool active, long actor)
    {
        using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "UPDATE Users SET DisplayName=@d,Role=@r,IsActive=@a WHERE Id=@id"; cmd.Parameters.AddWithValue("@d", displayName); cmd.Parameters.AddWithValue("@r", role.ToString()); cmd.Parameters.AddWithValue("@a", active ? 1 : 0); cmd.Parameters.AddWithValue("@id", id); cmd.ExecuteNonQuery();
        InsertAudit(c, tx, actor, "user.update", "User", id, "{}"); tx.Commit();
    }

    public static void SetPassword(long userId, string password, long actor)
    {
        var sec = Security.HashPassword(password);
        using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "UPDATE Users SET PasswordSalt=@s,PasswordHash=@h WHERE Id=@id"; cmd.Parameters.Add("@s", SqliteType.Blob).Value = sec.Salt; cmd.Parameters.Add("@h", SqliteType.Blob).Value = sec.Hash; cmd.Parameters.AddWithValue("@id", userId); cmd.ExecuteNonQuery();
        InsertAudit(c, tx, actor, "user.password", "User", userId, "{}"); tx.Commit();
    }

    public static void SoftDeleteUser(long id, long actor) => UpdateUser(id, GetUsers().First(x => x.Id == id).DisplayName, GetUsers().First(x => x.Id == id).Role, false, actor);

    public static List<Group> GetGroups() { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT Id,Name FROM Groups ORDER BY Name"; using var r = cmd.ExecuteReader(); var list = new List<Group>(); while (r.Read()) list.Add(new Group { Id = r.GetInt64(0), Name = r.GetString(1) }); return list; }
    public static long AddGroup(string name, long actor) { using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "INSERT INTO Groups(Name) VALUES(@n); SELECT last_insert_rowid();"; cmd.Parameters.AddWithValue("@n", name); var id = (long)(cmd.ExecuteScalar() ?? 0L); InsertAudit(c, tx, actor, "group.add", "Group", id, "{}"); tx.Commit(); return id; }
    public static void RenameGroup(long id, string name, long actor) { using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "UPDATE Groups SET Name=@n WHERE Id=@id"; cmd.Parameters.AddWithValue("@n", name); cmd.Parameters.AddWithValue("@id", id); cmd.ExecuteNonQuery(); InsertAudit(c, tx, actor, "group.rename", "Group", id, "{}"); tx.Commit(); }
    public static void DeleteGroup(long id, long actor) { using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "DELETE FROM Groups WHERE Id=@id"; cmd.Parameters.AddWithValue("@id", id); cmd.ExecuteNonQuery(); InsertAudit(c, tx, actor, "group.delete", "Group", id, "{}"); tx.Commit(); }
    public static List<User> GetGroupStudents(long gid) { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT u.Id,u.Login,u.DisplayName,u.Role,u.PasswordSalt,u.PasswordHash,u.IsActive,u.CreatedAt FROM Users u JOIN GroupMembers gm ON gm.UserId=u.Id WHERE gm.GroupId=@g ORDER BY u.DisplayName"; cmd.Parameters.AddWithValue("@g", gid); using var r = cmd.ExecuteReader(); var l = new List<User>(); while (r.Read()) l.Add(new User { Id = r.GetInt64(0), Login = r.GetString(1), DisplayName = r.GetString(2), Role = Enum.Parse<UserRole>(r.GetString(3)), PasswordSalt = (byte[])r[4], PasswordHash = (byte[])r[5], IsActive = r.GetInt32(6) == 1, CreatedAt = r.GetString(7) }); return l; }
    public static void SetGroupMember(long gid, long uid, bool add, long actor) { using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = add ? "INSERT OR IGNORE INTO GroupMembers(GroupId,UserId) VALUES(@g,@u)" : "DELETE FROM GroupMembers WHERE GroupId=@g AND UserId=@u"; cmd.Parameters.AddWithValue("@g", gid); cmd.Parameters.AddWithValue("@u", uid); cmd.ExecuteNonQuery(); InsertAudit(c, tx, actor, add ? "group.member.add" : "group.member.remove", "Group", gid, JsonUtil.To(new { uid })); tx.Commit(); }
    public static List<long> GetGroupIdsByUser(long uid) { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT GroupId FROM GroupMembers WHERE UserId=@u"; cmd.Parameters.AddWithValue("@u", uid); using var r = cmd.ExecuteReader(); var l = new List<long>(); while (r.Read()) l.Add(r.GetInt64(0)); return l; }

    public static List<TestEntity> GetTestsByTeacher(long tid)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,Title,Description,CreatedByTeacherId,Status,PassPercent,DefaultAttemptLimit,DefaultTimeLimitMinutes,DefaultShuffleQuestions,DefaultShuffleOptions,DefaultShowScoreAfter,DefaultShowCorrectAfter,CreatedAt FROM Tests WHERE CreatedByTeacherId=@t ORDER BY Id DESC"; cmd.Parameters.AddWithValue("@t", tid);
        return ReadTests(cmd);
    }
    public static List<TestEntity> ReadTests(SqliteCommand cmd) { using var r = cmd.ExecuteReader(); var l = new List<TestEntity>(); while (r.Read()) l.Add(new TestEntity { Id = r.GetInt64(0), Title = r.GetString(1), Description = r.GetString(2), CreatedByTeacherId = r.GetInt64(3), Status = Enum.Parse<TestStatus>(r.GetString(4)), PassPercent = r.GetInt32(5), DefaultAttemptLimit = r.GetInt32(6), DefaultTimeLimitMinutes = r.IsDBNull(7) ? null : r.GetInt32(7), DefaultShuffleQuestions = r.GetInt32(8) == 1, DefaultShuffleOptions = r.GetInt32(9) == 1, DefaultShowScoreAfter = r.GetInt32(10) == 1, DefaultShowCorrectAfter = r.GetInt32(11) == 1, CreatedAt = r.GetString(12) }); return l; }
    public static TestEntity GetTest(long id) { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT Id,Title,Description,CreatedByTeacherId,Status,PassPercent,DefaultAttemptLimit,DefaultTimeLimitMinutes,DefaultShuffleQuestions,DefaultShuffleOptions,DefaultShowScoreAfter,DefaultShowCorrectAfter,CreatedAt FROM Tests WHERE Id=@id"; cmd.Parameters.AddWithValue("@id", id); var l = ReadTests(cmd); Guard.True(l.Count == 1, "test not found"); return l[0]; }
    public static long AddTest(TestEntity t, long actor) { using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "INSERT INTO Tests(Title,Description,CreatedByTeacherId,Status,PassPercent,DefaultAttemptLimit,DefaultTimeLimitMinutes,DefaultShuffleQuestions,DefaultShuffleOptions,DefaultShowScoreAfter,DefaultShowCorrectAfter,CreatedAt) VALUES(@ti,@d,@cb,@s,@p,@al,@tm,@sq,@so,@ss,@sc,@at); SELECT last_insert_rowid();"; cmd.Parameters.AddWithValue("@ti", t.Title); cmd.Parameters.AddWithValue("@d", t.Description); cmd.Parameters.AddWithValue("@cb", t.CreatedByTeacherId); cmd.Parameters.AddWithValue("@s", t.Status.ToString()); cmd.Parameters.AddWithValue("@p", t.PassPercent); cmd.Parameters.AddWithValue("@al", t.DefaultAttemptLimit); cmd.Parameters.AddWithValue("@tm", (object?)t.DefaultTimeLimitMinutes ?? DBNull.Value); cmd.Parameters.AddWithValue("@sq", t.DefaultShuffleQuestions ? 1 : 0); cmd.Parameters.AddWithValue("@so", t.DefaultShuffleOptions ? 1 : 0); cmd.Parameters.AddWithValue("@ss", t.DefaultShowScoreAfter ? 1 : 0); cmd.Parameters.AddWithValue("@sc", t.DefaultShowCorrectAfter ? 1 : 0); cmd.Parameters.AddWithValue("@at", TimeUtil.Iso(TimeUtil.UtcNow)); var id = (long)(cmd.ExecuteScalar() ?? 0L); InsertAudit(c, tx, actor, "test.add", "Test", id, "{}"); tx.Commit(); return id; }
    public static void UpdateTestStatus(long id, TestStatus s, long actor) { using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "UPDATE Tests SET Status=@s WHERE Id=@id"; cmd.Parameters.AddWithValue("@s", s.ToString()); cmd.Parameters.AddWithValue("@id", id); cmd.ExecuteNonQuery(); InsertAudit(c, tx, actor, "test.status", "Test", id, JsonUtil.To(new { s })); tx.Commit(); }
    public static long CloneTest(long id, long actor)
    {
        var src = GetTest(id);
        using var c = Open(); using var tx = c.BeginTransaction();
        long nid;
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx; cmd.CommandText = "INSERT INTO Tests(Title,Description,CreatedByTeacherId,Status,PassPercent,DefaultAttemptLimit,DefaultTimeLimitMinutes,DefaultShuffleQuestions,DefaultShuffleOptions,DefaultShowScoreAfter,DefaultShowCorrectAfter,CreatedAt) VALUES(@t,@d,@cb,'Draft',@p,@al,@tm,@sq,@so,@ss,@sc,@at); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@t", src.Title + " (copy)"); cmd.Parameters.AddWithValue("@d", src.Description); cmd.Parameters.AddWithValue("@cb", src.CreatedByTeacherId); cmd.Parameters.AddWithValue("@p", src.PassPercent); cmd.Parameters.AddWithValue("@al", src.DefaultAttemptLimit); cmd.Parameters.AddWithValue("@tm", (object?)src.DefaultTimeLimitMinutes ?? DBNull.Value); cmd.Parameters.AddWithValue("@sq", src.DefaultShuffleQuestions ? 1 : 0); cmd.Parameters.AddWithValue("@so", src.DefaultShuffleOptions ? 1 : 0); cmd.Parameters.AddWithValue("@ss", src.DefaultShowScoreAfter ? 1 : 0); cmd.Parameters.AddWithValue("@sc", src.DefaultShowCorrectAfter ? 1 : 0); cmd.Parameters.AddWithValue("@at", TimeUtil.Iso(TimeUtil.UtcNow));
            nid = (long)(cmd.ExecuteScalar() ?? 0L);
        }
        foreach (var q in GetQuestions(id))
        {
            long qid;
            using (var iq = c.CreateCommand())
            {
                iq.Transaction = tx; iq.CommandText = "INSERT INTO Questions(TestId,Type,Text,Points,SettingsJson) VALUES(@t,@ty,@tx,@p,@s); SELECT last_insert_rowid();";
                iq.Parameters.AddWithValue("@t", nid); iq.Parameters.AddWithValue("@ty", q.Type.ToString()); iq.Parameters.AddWithValue("@tx", q.Text); iq.Parameters.AddWithValue("@p", q.Points); iq.Parameters.AddWithValue("@s", q.SettingsJson);
                qid = (long)(iq.ExecuteScalar() ?? 0L);
            }
            foreach (var o in GetOptions(q.Id)) { using var io = c.CreateCommand(); io.Transaction = tx; io.CommandText = "INSERT INTO Options(QuestionId,Text,IsCorrect,SortOrder) VALUES(@q,@t,@c,@s)"; io.Parameters.AddWithValue("@q", qid); io.Parameters.AddWithValue("@t", o.Text); io.Parameters.AddWithValue("@c", o.IsCorrect ? 1 : 0); io.Parameters.AddWithValue("@s", o.SortOrder); io.ExecuteNonQuery(); }
        }
        InsertAudit(c, tx, actor, "test.clone", "Test", nid, JsonUtil.To(new { from = id })); tx.Commit(); return nid;
    }

    public static List<QuestionEntity> GetQuestions(long testId) { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT Id,TestId,Type,Text,Points,SettingsJson FROM Questions WHERE TestId=@t ORDER BY Id"; cmd.Parameters.AddWithValue("@t", testId); using var r = cmd.ExecuteReader(); var l = new List<QuestionEntity>(); while (r.Read()) l.Add(new QuestionEntity { Id = r.GetInt64(0), TestId = r.GetInt64(1), Type = Enum.Parse<QuestionType>(r.GetString(2)), Text = r.GetString(3), Points = r.GetDouble(4), SettingsJson = r.GetString(5) }); return l; }
    public static List<OptionEntity> GetOptions(long qid) { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT Id,QuestionId,Text,IsCorrect,SortOrder FROM Options WHERE QuestionId=@q ORDER BY SortOrder,Id"; cmd.Parameters.AddWithValue("@q", qid); using var r = cmd.ExecuteReader(); var l = new List<OptionEntity>(); while (r.Read()) l.Add(new OptionEntity { Id = r.GetInt64(0), QuestionId = r.GetInt64(1), Text = r.GetString(2), IsCorrect = r.GetInt32(3) == 1, SortOrder = r.GetInt32(4) }); return l; }
    public static void ReplaceTestQuestions(long testId, IEnumerable<QuestionEntity> qs, Dictionary<int, List<OptionEntity>> opts, long actor)
    {
        var test = GetTest(testId);
        Guard.True(test.Status == TestStatus.Draft, "Published tests are read-only. Clone first.");
        using var c = Open(); using var tx = c.BeginTransaction();
        using (var d1 = c.CreateCommand()) { d1.Transaction = tx; d1.CommandText = "DELETE FROM Questions WHERE TestId=@t"; d1.Parameters.AddWithValue("@t", testId); d1.ExecuteNonQuery(); }
        int i = 0;
        foreach (var q in qs)
        {
            long nq;
            using (var iq = c.CreateCommand())
            {
                iq.Transaction = tx; iq.CommandText = "INSERT INTO Questions(TestId,Type,Text,Points,SettingsJson) VALUES(@t,@ty,@tx,@p,@s); SELECT last_insert_rowid();";
                iq.Parameters.AddWithValue("@t", testId); iq.Parameters.AddWithValue("@ty", q.Type.ToString()); iq.Parameters.AddWithValue("@tx", q.Text); iq.Parameters.AddWithValue("@p", q.Points); iq.Parameters.AddWithValue("@s", q.SettingsJson);
                nq = (long)(iq.ExecuteScalar() ?? 0L);
            }
            if (opts.TryGetValue(i, out var lo)) foreach (var o in lo) { using var io = c.CreateCommand(); io.Transaction = tx; io.CommandText = "INSERT INTO Options(QuestionId,Text,IsCorrect,SortOrder) VALUES(@q,@t,@c,@s)"; io.Parameters.AddWithValue("@q", nq); io.Parameters.AddWithValue("@t", o.Text); io.Parameters.AddWithValue("@c", o.IsCorrect ? 1 : 0); io.Parameters.AddWithValue("@s", o.SortOrder); io.ExecuteNonQuery(); }
            i++;
        }
        InsertAudit(c, tx, actor, "test.questions.replace", "Test", testId, "{}"); tx.Commit();
    }

    public static List<Assignment> GetAssignmentsByTest(long t) { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT Id,TestId,TargetType,TargetId,AvailableFrom,Deadline,AttemptLimit,TimeLimitMinutes,ShuffleQuestions,ShuffleOptions,ShowScoreAfter,ShowCorrectAfter,IsActive FROM Assignments WHERE TestId=@t ORDER BY Id DESC"; cmd.Parameters.AddWithValue("@t", t); return ReadAssignments(cmd); }
    public static Assignment GetAssignment(long id) { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT Id,TestId,TargetType,TargetId,AvailableFrom,Deadline,AttemptLimit,TimeLimitMinutes,ShuffleQuestions,ShuffleOptions,ShowScoreAfter,ShowCorrectAfter,IsActive FROM Assignments WHERE Id=@id"; cmd.Parameters.AddWithValue("@id", id); var l = ReadAssignments(cmd); Guard.True(l.Count == 1, "assignment not found"); return l[0]; }
    private static List<Assignment> ReadAssignments(SqliteCommand cmd) { using var r = cmd.ExecuteReader(); var l = new List<Assignment>(); while (r.Read()) l.Add(new Assignment { Id = r.GetInt64(0), TestId = r.GetInt64(1), TargetType = Enum.Parse<TargetType>(r.GetString(2)), TargetId = r.GetInt64(3), AvailableFrom = r.IsDBNull(4) ? null : r.GetString(4), Deadline = r.IsDBNull(5) ? null : r.GetString(5), AttemptLimit = r.GetInt32(6), TimeLimitMinutes = r.IsDBNull(7) ? null : r.GetInt32(7), ShuffleQuestions = r.GetInt32(8) == 1, ShuffleOptions = r.GetInt32(9) == 1, ShowScoreAfter = r.GetInt32(10) == 1, ShowCorrectAfter = r.GetInt32(11) == 1, IsActive = r.GetInt32(12) == 1 }); return l; }
    public static long AddAssignment(Assignment a, long actor) { using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "INSERT INTO Assignments(TestId,TargetType,TargetId,AvailableFrom,Deadline,AttemptLimit,TimeLimitMinutes,ShuffleQuestions,ShuffleOptions,ShowScoreAfter,ShowCorrectAfter,IsActive) VALUES(@t,@tt,@tid,@af,@dl,@al,@tm,@sq,@so,@ss,@sc,@ia); SELECT last_insert_rowid();"; cmd.Parameters.AddWithValue("@t", a.TestId); cmd.Parameters.AddWithValue("@tt", a.TargetType.ToString()); cmd.Parameters.AddWithValue("@tid", a.TargetId); cmd.Parameters.AddWithValue("@af", (object?)a.AvailableFrom ?? DBNull.Value); cmd.Parameters.AddWithValue("@dl", (object?)a.Deadline ?? DBNull.Value); cmd.Parameters.AddWithValue("@al", a.AttemptLimit); cmd.Parameters.AddWithValue("@tm", (object?)a.TimeLimitMinutes ?? DBNull.Value); cmd.Parameters.AddWithValue("@sq", a.ShuffleQuestions ? 1 : 0); cmd.Parameters.AddWithValue("@so", a.ShuffleOptions ? 1 : 0); cmd.Parameters.AddWithValue("@ss", a.ShowScoreAfter ? 1 : 0); cmd.Parameters.AddWithValue("@sc", a.ShowCorrectAfter ? 1 : 0); cmd.Parameters.AddWithValue("@ia", a.IsActive ? 1 : 0); var id = (long)(cmd.ExecuteScalar() ?? 0L); InsertAudit(c, tx, actor, "assignment.add", "Assignment", id, "{}"); tx.Commit(); return id; }
    public static void SetAssignmentActive(long id, bool active, long actor) { using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "UPDATE Assignments SET IsActive=@a WHERE Id=@id"; cmd.Parameters.AddWithValue("@a", active ? 1 : 0); cmd.Parameters.AddWithValue("@id", id); cmd.ExecuteNonQuery(); InsertAudit(c, tx, actor, "assignment.active", "Assignment", id, JsonUtil.To(new { active })); tx.Commit(); }

    public static Attempt GetAttempt(long id) { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT Id,AssignmentId,UserId,StartedAt,EndsAt,SubmittedAt,Status,SnapshotJson FROM Attempts WHERE Id=@id"; cmd.Parameters.AddWithValue("@id", id); using var r = cmd.ExecuteReader(); Guard.True(r.Read(), "attempt not found"); return new Attempt { Id = r.GetInt64(0), AssignmentId = r.GetInt64(1), UserId = r.GetInt64(2), StartedAt = r.GetString(3), EndsAt = r.IsDBNull(4) ? null : r.GetString(4), SubmittedAt = r.IsDBNull(5) ? null : r.GetString(5), Status = Enum.Parse<AttemptStatus>(r.GetString(6)), SnapshotJson = r.GetString(7) }; }
    public static List<Attempt> GetAttemptsByAssignmentAndUser(long aid, long uid) { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT Id,AssignmentId,UserId,StartedAt,EndsAt,SubmittedAt,Status,SnapshotJson FROM Attempts WHERE AssignmentId=@a AND UserId=@u ORDER BY Id"; cmd.Parameters.AddWithValue("@a", aid); cmd.Parameters.AddWithValue("@u", uid); using var r = cmd.ExecuteReader(); var l = new List<Attempt>(); while (r.Read()) l.Add(new Attempt { Id = r.GetInt64(0), AssignmentId = r.GetInt64(1), UserId = r.GetInt64(2), StartedAt = r.GetString(3), EndsAt = r.IsDBNull(4) ? null : r.GetString(4), SubmittedAt = r.IsDBNull(5) ? null : r.GetString(5), Status = Enum.Parse<AttemptStatus>(r.GetString(6)), SnapshotJson = r.GetString(7) }); return l; }
    public static List<Attempt> GetAttemptsByTest(long tid) { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT a.Id,a.AssignmentId,a.UserId,a.StartedAt,a.EndsAt,a.SubmittedAt,a.Status,a.SnapshotJson FROM Attempts a JOIN Assignments s ON s.Id=a.AssignmentId WHERE s.TestId=@t ORDER BY a.Id DESC"; cmd.Parameters.AddWithValue("@t", tid); using var r = cmd.ExecuteReader(); var l = new List<Attempt>(); while (r.Read()) l.Add(new Attempt { Id = r.GetInt64(0), AssignmentId = r.GetInt64(1), UserId = r.GetInt64(2), StartedAt = r.GetString(3), EndsAt = r.IsDBNull(4) ? null : r.GetString(4), SubmittedAt = r.IsDBNull(5) ? null : r.GetString(5), Status = Enum.Parse<AttemptStatus>(r.GetString(6)), SnapshotJson = r.GetString(7) }); return l; }
    public static Dictionary<long, string> GetAttemptAnswers(long attemptId) { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT QuestionId,AnswerJson FROM AttemptAnswers WHERE AttemptId=@a"; cmd.Parameters.AddWithValue("@a", attemptId); using var r = cmd.ExecuteReader(); var m = new Dictionary<long, string>(); while (r.Read()) m[r.GetInt64(0)] = r.GetString(1); return m; }
    public static AttemptResult? GetResult(long attemptId) { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT AttemptId,Score,MaxScore,Percent,Passed,CheckedAt,DetailsJson FROM AttemptResults WHERE AttemptId=@a"; cmd.Parameters.AddWithValue("@a", attemptId); using var r = cmd.ExecuteReader(); if (!r.Read()) return null; return new AttemptResult { AttemptId = r.GetInt64(0), Score = r.GetDouble(1), MaxScore = r.GetDouble(2), Percent = r.GetDouble(3), Passed = r.GetInt32(4) == 1, CheckedAt = r.GetString(5), DetailsJson = r.GetString(6) }; }
    public static List<AuditEvent> GetAudit(string? actor = null, string? action = null, DateTime? from = null, DateTime? to = null)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT l.Id,l.ActorUserId,l.Action,l.EntityType,l.EntityId,l.At,l.MetaJson FROM AuditLog l JOIN Users u ON u.Id=l.ActorUserId WHERE (@actor='' OR u.Login LIKE @actorLike) AND (@action='' OR l.Action LIKE @actionLike) AND (@from='' OR l.At>=@from) AND (@to='' OR l.At<=@to) ORDER BY l.Id DESC LIMIT 500";
        cmd.Parameters.AddWithValue("@actor", actor ?? ""); cmd.Parameters.AddWithValue("@actorLike", $"%{actor}%"); cmd.Parameters.AddWithValue("@action", action ?? ""); cmd.Parameters.AddWithValue("@actionLike", $"%{action}%"); cmd.Parameters.AddWithValue("@from", from.HasValue ? TimeUtil.Iso(from.Value) : ""); cmd.Parameters.AddWithValue("@to", to.HasValue ? TimeUtil.Iso(to.Value) : "");
        using var r = cmd.ExecuteReader(); var l = new List<AuditEvent>(); while (r.Read()) l.Add(new AuditEvent { Id = r.GetInt64(0), ActorUserId = r.GetInt64(1), Action = r.GetString(2), EntityType = r.GetString(3), EntityId = r.GetInt64(4), At = r.GetString(5), MetaJson = r.GetString(6) }); return l;
    }
}
