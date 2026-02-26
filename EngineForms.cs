using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TestingPlatform;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        ApplicationConfiguration.Initialize();
        EngineDb.Init();
        Application.Run(new LoginForm());
    }
}

public sealed class LoginForm : Form
{
    private readonly ComboBox _users = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _login = new() { Dock = DockStyle.Top, Height = 36, Text = "Войти" };

    public LoginForm()
    {
        Text = "Login"; Width = 400; Height = 180;
        Controls.Add(_login); Controls.Add(_users);
        Load += (_, _) => { _users.DataSource = EngineDb.GetUsers(); _users.DisplayMember = "Name"; };
        _login.Click += (_, _) =>
        {
            if (_users.SelectedItem is not User u) return;
            Hide();
            using var mf = new MainForm(u);
            mf.ShowDialog();
            Show();
        };
    }
}

public sealed class MainForm : Form
{
    private readonly User _current;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };

    public MainForm(User user)
    {
        _current = user;
        Text = $"Платформа тестирования - {user.Name} ({user.Role})";
        Width = 1200; Height = 780;
        Controls.Add(_tabs);
        BuildTabs();
    }

    private void BuildTabs()
    {
        if (_current.Role == UserRole.Admin) BuildAdminTabs();
        if (_current.Role == UserRole.Teacher) BuildTeacherTabs();
        if (_current.Role == UserRole.Student) BuildStudentTabs();
    }

    private void BuildAdminTabs()
    {
        var usersTab = new TabPage("Users");
        var usersGrid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true };
        var usersTop = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40 };
        var name = new TextBox { Width = 140 }; var role = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
        role.DataSource = Enum.GetValues<UserRole>();
        var add = new Button { Text = "Add" }; var del = new Button { Text = "Delete selected" };
        usersTop.Controls.AddRange(new Control[] { new Label { Text = "Name" }, name, new Label { Text = "Role" }, role, add, del });
        usersTab.Controls.Add(usersGrid); usersTab.Controls.Add(usersTop);
        void ReloadUsers() => usersGrid.DataSource = EngineDb.GetUsers();
        ReloadUsers();
        add.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(name.Text)) { EngineDb.AddUser(name.Text.Trim(), (UserRole)role.SelectedItem!, _current.Id); name.Clear(); ReloadUsers(); } };
        del.Click += (_, _) => { if (usersGrid.CurrentRow?.DataBoundItem is User u && u.Id != _current.Id) { EngineDb.DeleteUser(u.Id, _current.Id); ReloadUsers(); } };

        var groupsTab = new TabPage("Groups");
        var groupsGrid = new DataGridView { Dock = DockStyle.Left, Width = 350, ReadOnly = true, AutoGenerateColumns = true };
        var membersGrid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true };
        var grpTop = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40 };
        var gname = new TextBox { Width = 150 }; var gadd = new Button { Text = "Create group" };
        var memberCmb = new ComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList }; var addM = new Button { Text = "Add member" }; var remM = new Button { Text = "Remove selected" };
        grpTop.Controls.AddRange(new Control[] { gname, gadd, memberCmb, addM, remM });
        groupsTab.Controls.Add(membersGrid); groupsTab.Controls.Add(groupsGrid); groupsTab.Controls.Add(grpTop);
        void ReloadGroups() { groupsGrid.DataSource = EngineDb.GetGroups(); memberCmb.DataSource = EngineDb.GetUsers().Where(u => u.Role == UserRole.Student).ToList(); memberCmb.DisplayMember = "Name"; }
        void ReloadMembers()
        {
            if (groupsGrid.CurrentRow?.DataBoundItem is not Group g) return;
            var ids = EngineDb.GetGroupIdsForUser(0); // noop placeholder to keep methods centralized
            using var c = EngineDb.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT u.Id,u.Name,u.Role FROM Users u JOIN GroupMembers gm ON gm.UserId=u.Id WHERE gm.GroupId=@g ORDER BY u.Name;";
            cmd.Parameters.AddWithValue("@g", g.Id);
            using var r = cmd.ExecuteReader();
            var list = new List<User>();
            while (r.Read()) list.Add(new User { Id = r.GetInt64(0), Name = r.GetString(1), Role = Enum.Parse<UserRole>(r.GetString(2)) });
            membersGrid.DataSource = list;
        }
        ReloadGroups(); groupsGrid.SelectionChanged += (_, _) => ReloadMembers();
        gadd.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(gname.Text)) { EngineDb.AddGroup(gname.Text.Trim(), _current.Id); gname.Clear(); ReloadGroups(); } };
        addM.Click += (_, _) => { if (groupsGrid.CurrentRow?.DataBoundItem is Group g && memberCmb.SelectedItem is User u) { EngineDb.SetGroupMember(g.Id, u.Id, true, _current.Id); ReloadMembers(); } };
        remM.Click += (_, _) => { if (groupsGrid.CurrentRow?.DataBoundItem is Group g && membersGrid.CurrentRow?.DataBoundItem is User u) { EngineDb.SetGroupMember(g.Id, u.Id, false, _current.Id); ReloadMembers(); } };

        var auditTab = new TabPage("AuditLog");
        var auditGrid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true };
        var auditRefresh = new Button { Text = "Refresh", Dock = DockStyle.Top };
        auditTab.Controls.Add(auditGrid); auditTab.Controls.Add(auditRefresh);
        void ReloadAudit() => auditGrid.DataSource = EngineDb.GetAuditLast(200);
        ReloadAudit(); auditRefresh.Click += (_, _) => ReloadAudit();

        _tabs.TabPages.Add(usersTab); _tabs.TabPages.Add(groupsTab); _tabs.TabPages.Add(auditTab);
    }

    private void BuildTeacherTabs()
    {
        var testsTab = new TabPage("Tests");
        var testsGrid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true };
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40 };
        var title = new TextBox { Width = 150 }; var pass = new NumericUpDown { Width = 70, Minimum = 1, Maximum = 100, Value = 60 };
        var add = new Button { Text = "Create" }; var pub = new Button { Text = "Publish" }; var arch = new Button { Text = "Archive" }; var toDraft = new Button { Text = "Move to Draft" }; var clone = new Button { Text = "Clone" };
        top.Controls.AddRange(new Control[] { title, pass, add, pub, arch, toDraft, clone });
        testsTab.Controls.Add(testsGrid); testsTab.Controls.Add(top);
        void ReloadTests() => testsGrid.DataSource = EngineDb.GetTestsByTeacher(_current.Id);
        ReloadTests();
        add.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(title.Text)) { EngineDb.AddTest(new TestEntity { Title = title.Text.Trim(), CreatedByTeacherId = _current.Id, Status = TestStatus.Draft, PassPercent = (int)pass.Value }, _current.Id); title.Clear(); ReloadTests(); } };
        pub.Click += (_, _) => { if (testsGrid.CurrentRow?.DataBoundItem is TestEntity t) { EngineDb.UpdateTestStatus(t.Id, TestStatus.Published, _current.Id); ReloadTests(); } };
        arch.Click += (_, _) => { if (testsGrid.CurrentRow?.DataBoundItem is TestEntity t) { EngineDb.UpdateTestStatus(t.Id, TestStatus.Archived, _current.Id); ReloadTests(); } };
        toDraft.Click += (_, _) => { if (testsGrid.CurrentRow?.DataBoundItem is TestEntity t) { EngineDb.UpdateTestStatus(t.Id, TestStatus.Draft, _current.Id); ReloadTests(); } };
        clone.Click += (_, _) => { if (testsGrid.CurrentRow?.DataBoundItem is TestEntity t) { EngineDb.CloneTestToDraft(t.Id, _current.Id); ReloadTests(); } };

        var questionsTab = new TabPage("Questions editor");
        var split = new SplitContainer { Dock = DockStyle.Fill };
        var tGrid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true };
        var qGrid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true };
        split.Panel1.Controls.Add(tGrid); split.Panel2.Controls.Add(qGrid);
        var qTop = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40 };
        var qText = new TextBox { Width = 230 }; var qType = new ComboBox { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList }; qType.DataSource = Enum.GetValues<QuestionType>();
        var qPts = new NumericUpDown { Width = 70, DecimalPlaces = 2, Minimum = 0, Maximum = 100, Value = 1 };
        var optText = new TextBox { Width = 150 }; var optCorr = new CheckBox { Text = "Correct" }; var addQ = new Button { Text = "Add question" };
        qTop.Controls.AddRange(new Control[] { qText, qType, qPts, new Label { Text = "Option" }, optText, optCorr, addQ });
        questionsTab.Controls.Add(split); questionsTab.Controls.Add(qTop);
        void ReloadTQ() => tGrid.DataSource = EngineDb.GetTestsByTeacher(_current.Id);
        void ReloadQ() { if (tGrid.CurrentRow?.DataBoundItem is TestEntity t) qGrid.DataSource = EngineDb.GetQuestions(t.Id); }
        ReloadTQ(); tGrid.SelectionChanged += (_, _) => ReloadQ();
        addQ.Click += (_, _) =>
        {
            if (tGrid.CurrentRow?.DataBoundItem is not TestEntity t || string.IsNullOrWhiteSpace(qText.Text)) return;
            var qt = (QuestionType)qType.SelectedItem!;
            var q = new QuestionEntity { TestId = t.Id, Type = qt, Text = qText.Text.Trim(), Points = (double)qPts.Value, SettingsJson = "{}" };
            var opts = new List<OptionEntity>();
            if (qt == QuestionType.Text)
                q.SettingsJson = JsonUtil.Serialize(new TextSettings { Accepted = new() { "demo" }, AllowManualCheck = true, CaseInsensitive = true, Trim = true });
            if (qt == QuestionType.Numeric)
                q.SettingsJson = JsonUtil.Serialize(new NumericSettings { Correct = 1, Tolerance = 0.01 });
            if ((qt == QuestionType.SingleChoice || qt == QuestionType.MultipleChoice) && !string.IsNullOrWhiteSpace(optText.Text))
                opts.Add(new OptionEntity { Text = optText.Text.Trim(), IsCorrect = optCorr.Checked, SortOrder = 1 });
            EngineDb.AddQuestionWithOptions(q, opts, _current.Id);
            qText.Clear(); optText.Clear(); optCorr.Checked = false; ReloadQ();
        };

        var assignTab = new TabPage("Assignments");
        var aSplit = new SplitContainer { Dock = DockStyle.Fill };
        var atestGrid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true };
        var assGrid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true };
        aSplit.Panel1.Controls.Add(atestGrid); aSplit.Panel2.Controls.Add(assGrid);
        var aTop = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46 };
        var targetType = new ComboBox { Width = 90, DropDownStyle = ComboBoxStyle.DropDownList }; targetType.DataSource = Enum.GetValues<AssignmentTargetType>();
        var target = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        var limit = new NumericUpDown { Width = 60, Minimum = 1, Maximum = 10, Value = 1 };
        var minutes = new NumericUpDown { Width = 70, Minimum = 0, Maximum = 600, Value = 30 };
        var deadline = new DateTimePicker { Width = 170, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm" };
        var shQ = new CheckBox { Text = "Shuffle Q", Checked = true }; var shO = new CheckBox { Text = "Shuffle O", Checked = true };
        var ss = new CheckBox { Text = "ShowScore", Checked = true }; var sc = new CheckBox { Text = "ShowCorrect" };
        var addA = new Button { Text = "Assign" };
        aTop.Controls.AddRange(new Control[] { targetType, target, limit, minutes, deadline, shQ, shO, ss, sc, addA });
        assignTab.Controls.Add(aSplit); assignTab.Controls.Add(aTop);
        void ReloadAT() => atestGrid.DataSource = EngineDb.GetTestsByTeacher(_current.Id);
        void ReloadAss() { if (atestGrid.CurrentRow?.DataBoundItem is TestEntity t) assGrid.DataSource = EngineDb.GetAssignmentsByTest(t.Id); }
        void ReloadTarget()
        {
            if ((AssignmentTargetType)targetType.SelectedItem! == AssignmentTargetType.Group) { target.DataSource = EngineDb.GetGroups(); target.DisplayMember = "Name"; }
            else { target.DataSource = EngineDb.GetUsers().Where(u => u.Role == UserRole.Student).ToList(); target.DisplayMember = "Name"; }
        }
        ReloadAT(); ReloadTarget(); atestGrid.SelectionChanged += (_, _) => ReloadAss(); targetType.SelectedIndexChanged += (_, _) => ReloadTarget();
        addA.Click += (_, _) =>
        {
            if (atestGrid.CurrentRow?.DataBoundItem is not TestEntity t || target.SelectedItem == null) return;
            var tid = target.SelectedItem is Group g ? g.Id : ((User)target.SelectedItem).Id;
            var a = new Assignment { TestId = t.Id, TargetType = (AssignmentTargetType)targetType.SelectedItem!, TargetId = tid, AvailableFrom = Clock.ToDb(Clock.UtcNow()), Deadline = Clock.ToDb(deadline.Value.ToUniversalTime()), AttemptLimit = (int)limit.Value, TimeLimitMinutes = (int)minutes.Value <= 0 ? null : (int)minutes.Value, ShuffleQuestions = shQ.Checked, ShuffleOptions = shO.Checked, ShowScoreAfter = ss.Checked, ShowCorrectAfter = sc.Checked, IsActive = true };
            EngineDb.AddAssignment(a, _current.Id); ReloadAss();
        };

        var resultsTab = new TabPage("Results");
        var rSplit = new SplitContainer { Dock = DockStyle.Fill };
        var rTestGrid = new DataGridView { Dock = DockStyle.Left, Width = 320, ReadOnly = true, AutoGenerateColumns = true };
        var rAttempts = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true };
        var rTop = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40 };
        var review = new Button { Text = "Open attempt" }; var export = new Button { Text = "Export CSV" };
        rTop.Controls.AddRange(new Control[] { review, export });
        rSplit.Panel1.Controls.Add(rTestGrid); rSplit.Panel2.Controls.Add(rAttempts);
        resultsTab.Controls.Add(rSplit); resultsTab.Controls.Add(rTop);
        void ReloadRT() => rTestGrid.DataSource = EngineDb.GetTestsByTeacher(_current.Id);
        void ReloadRA()
        {
            if (rTestGrid.CurrentRow?.DataBoundItem is not TestEntity t) return;
            var list = EngineDb.GetAttemptsByTest(t.Id).Select(a =>
            {
                var rs = EngineDb.GetAttemptResult(a.Id);
                var pending = rs != null && JsonUtil.Deserialize<GradeDetails>(rs.DetailsJson).PendingManual;
                return new { a.Id, Student = EngineDb.GetUser(a.UserId).Name, a.Status, PendingManual = pending, Score = rs?.Score, Percent = rs?.Percent, a.StartedAt, a.SubmittedAt };
            }).ToList();
            rAttempts.DataSource = list;
        }
        ReloadRT(); rTestGrid.SelectionChanged += (_, _) => ReloadRA();
        review.Click += (_, _) =>
        {
            if (rAttempts.CurrentRow?.Cells["Id"].Value is not long aid) return;
            using var f = new AttemptReviewForm(_current, aid);
            f.ShowDialog();
            ReloadRA();
        };
        export.Click += (_, _) =>
        {
            if (rTestGrid.CurrentRow?.DataBoundItem is not TestEntity t) return;
            using var sfd = new SaveFileDialog { Filter = "CSV|*.csv", FileName = $"results_test_{t.Id}.csv" };
            if (sfd.ShowDialog() == DialogResult.OK) { AttemptLogic.ExportResultsCsv(_current.Id, t.Id, sfd.FileName); MessageBox.Show("Exported"); }
        };

        var analyticsTab = new TabPage("Analytics");
        var anTest = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        var anBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true };
        var anBtn = new Button { Dock = DockStyle.Top, Height = 36, Text = "Calculate" };
        analyticsTab.Controls.Add(anBox); analyticsTab.Controls.Add(anBtn); analyticsTab.Controls.Add(anTest);
        anTest.DataSource = EngineDb.GetTestsByTeacher(_current.Id); anTest.DisplayMember = "Title";
        anBtn.Click += (_, _) =>
        {
            if (anTest.SelectedItem is not TestEntity t) return;
            var a = AttemptLogic.GetAnalyticsForTeacher(_current.Id, t.Id);
            anBox.Text = JsonUtil.Serialize(a);
        };

        _tabs.TabPages.Add(testsTab); _tabs.TabPages.Add(questionsTab); _tabs.TabPages.Add(assignTab); _tabs.TabPages.Add(resultsTab); _tabs.TabPages.Add(analyticsTab);
    }

    private void BuildStudentTabs()
    {
        var availTab = new TabPage("Available tests");
        var g = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true };
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40 };
        var start = new Button { Text = "Начать" }; var refresh = new Button { Text = "Refresh" };
        top.Controls.AddRange(new Control[] { start, refresh });
        availTab.Controls.Add(g); availTab.Controls.Add(top);
        void ReloadAvail() => g.DataSource = AttemptLogic.ListAvailableAssignments(_current.Id);
        ReloadAvail(); refresh.Click += (_, _) => ReloadAvail();
        start.Click += (_, _) =>
        {
            if (g.CurrentRow?.DataBoundItem is not AvailableAssignmentView a) return;
            var id = AttemptLogic.StartAttempt(_current.Id, a.AssignmentId);
            using var form = new AttemptForm(_current, id);
            form.ShowDialog();
            ReloadAvail();
        };

        var myTab = new TabPage("My attempts");
        var mg = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true };
        var mtop = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40 };
        var mref = new Button { Text = "Refresh" }; var mopen = new Button { Text = "Open" };
        mtop.Controls.AddRange(new Control[] { mref, mopen });
        myTab.Controls.Add(mg); myTab.Controls.Add(mtop);
        void ReloadMy()
        {
            var list = new List<object>();
            foreach (var a in AttemptLogic.ListAvailableAssignments(_current.Id))
                foreach (var at in EngineDb.GetAttemptsByAssignmentAndUser(a.AssignmentId, _current.Id))
                {
                    var res = EngineDb.GetAttemptResult(at.Id);
                    var pending = res != null && JsonUtil.Deserialize<GradeDetails>(res.DetailsJson).PendingManual;
                    list.Add(new { at.Id, a.TestTitle, at.Status, PendingManual = pending, Score = res?.Score, Percent = res?.Percent, at.StartedAt, at.SubmittedAt });
                }
            mg.DataSource = list.OrderByDescending(x => ((dynamic)x).Id).ToList();
        }
        ReloadMy(); mref.Click += (_, _) => ReloadMy();
        mopen.Click += (_, _) =>
        {
            if (mg.CurrentRow?.Cells["Id"].Value is not long id) return;
            var at = EngineDb.GetAttempt(id);
            if (at.Status == AttemptStatus.Active)
            {
                using var form = new AttemptForm(_current, id);
                form.ShowDialog();
            }
            else
            {
                var review = AttemptLogic.GetAttemptReview(_current.Id, id);
                MessageBox.Show(JsonUtil.Serialize(review));
            }
            ReloadMy();
        };

        _tabs.TabPages.Add(availTab); _tabs.TabPages.Add(myTab);
    }
}

public sealed class AttemptReviewForm : Form
{
    public AttemptReviewForm(User teacher, long attemptId)
    {
        Text = $"Attempt #{attemptId}"; Width = 900; Height = 650;
        var box = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, ReadOnly = true, Font = new Font("Consolas", 10) };
        var btn = new Button { Dock = DockStyle.Top, Height = 36, Text = "Apply manual score to selected Text question" };
        Controls.Add(box); Controls.Add(btn);
        void Reload() => box.Text = JsonUtil.Serialize(AttemptLogic.GetAttemptReview(teacher.Id, attemptId));
        Reload();
        btn.Click += (_, _) =>
        {
            var a = EngineDb.GetAttempt(attemptId);
            var snap = JsonUtil.Deserialize<AttemptSnapshot>(a.SnapshotJson);
            var q = snap.Questions.FirstOrDefault(x => x.Type == QuestionType.Text);
            if (q == null) { MessageBox.Show("No text questions"); return; }
            var score = Microsoft.VisualBasic.Interaction.InputBox("Score", "Manual check", "0");
            if (double.TryParse(score, out var s)) { AttemptLogic.ApplyManualCheck(attemptId, q.Id, teacher.Id, s, "manual by teacher"); Reload(); }
        };
    }
}

public sealed class AttemptForm : Form
{
    private readonly User _student;
    private readonly long _attemptId;
    private readonly AttemptSnapshot _snapshot;
    private int _idx;
    private readonly Label _timer = new() { Dock = DockStyle.Top, Height = 30, TextAlign = ContentAlignment.MiddleCenter };
    private readonly ListBox _nav = new() { Dock = DockStyle.Left, Width = 170 };
    private readonly Panel _panel = new() { Dock = DockStyle.Fill };
    private readonly Button _save = new() { Text = "Сохранить" };
    private readonly Button _prev = new() { Text = "Назад" };
    private readonly Button _next = new() { Text = "Далее" };
    private readonly Button _submit = new() { Text = "Отправить" };
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };

    public AttemptForm(User student, long attemptId)
    {
        _student = student; _attemptId = attemptId;
        var attempt = EngineDb.GetAttempt(attemptId);
        _snapshot = JsonUtil.Deserialize<AttemptSnapshot>(attempt.SnapshotJson);
        Text = $"Attempt #{attemptId} - {_snapshot.TestTitle}"; Width = 900; Height = 650;
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44 };
        bottom.Controls.AddRange(new Control[] { _prev, _next, _save, _submit });
        Controls.Add(_panel); Controls.Add(_nav); Controls.Add(bottom); Controls.Add(_timer);

        _nav.Items.AddRange(_snapshot.Questions.Select((q, i) => $"Q{i + 1}: {q.Type}").Cast<object>().ToArray());
        _nav.SelectedIndexChanged += (_, _) => { if (_nav.SelectedIndex >= 0) { _idx = _nav.SelectedIndex; RenderQuestion(); } };
        _prev.Click += (_, _) => { if (_idx > 0) { _idx--; _nav.SelectedIndex = _idx; } };
        _next.Click += (_, _) => { if (_idx < _snapshot.Questions.Count - 1) { _idx++; _nav.SelectedIndex = _idx; } };
        _save.Click += (_, _) => SaveCurrent();
        _submit.Click += (_, _) => { SaveCurrent(); AttemptLogic.SubmitAttempt(_attemptId, "manual"); Close(); };
        FormClosing += AttemptForm_FormClosing;

        _tick.Tick += (_, _) => CheckTimer(); _tick.Start();
        _nav.SelectedIndex = 0;
    }

    private void CheckTimer()
    {
        var a = EngineDb.GetAttempt(_attemptId);
        if (!string.IsNullOrWhiteSpace(a.EndsAt))
        {
            var left = Clock.FromDb(a.EndsAt!) - Clock.UtcNow();
            _timer.Text = left <= TimeSpan.Zero ? "Time is over" : $"Осталось: {left:hh\\:mm\\:ss}";
            if (left <= TimeSpan.Zero && a.Status == AttemptStatus.Active)
            {
                AttemptLogic.SubmitAttempt(_attemptId, "timeExpired");
                MessageBox.Show("Время вышло. Попытка отправлена автоматически.");
                Close();
            }
        }
        else _timer.Text = "Без ограничения времени";
    }

    private void AttemptForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        var a = EngineDb.GetAttempt(_attemptId);
        if (a.Status != AttemptStatus.Active) return;
        var ans = MessageBox.Show("Сабмитнуть попытку перед выходом?", "Выход", MessageBoxButtons.YesNoCancel);
        if (ans == DialogResult.Cancel) { e.Cancel = true; return; }
        if (ans == DialogResult.Yes) { SaveCurrent(); AttemptLogic.SubmitAttempt(_attemptId, "manual"); }
    }

    private void RenderQuestion()
    {
        _panel.Controls.Clear();
        var q = _snapshot.Questions[_idx];
        var label = new Label { Dock = DockStyle.Top, Height = 70, Text = q.Text };
        _panel.Controls.Add(label);

        switch (q.Type)
        {
            case QuestionType.SingleChoice:
                var rg = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoScroll = true, Tag = "single" };
                foreach (var o in q.Options) rg.Controls.Add(new RadioButton { Text = o.Text, Tag = o.Id, AutoSize = true });
                _panel.Controls.Add(rg);
                break;
            case QuestionType.MultipleChoice:
                var cg = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoScroll = true, Tag = "multi" };
                foreach (var o in q.Options) cg.Controls.Add(new CheckBox { Text = o.Text, Tag = o.Id, AutoSize = true });
                _panel.Controls.Add(cg);
                break;
            case QuestionType.Text:
                _panel.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, Tag = "text" });
                break;
            case QuestionType.Numeric:
                _panel.Controls.Add(new NumericUpDown { Dock = DockStyle.Top, DecimalPlaces = 4, Minimum = -100000, Maximum = 100000, Tag = "num" });
                break;
        }
    }

    private void SaveCurrent()
    {
        var q = _snapshot.Questions[_idx];
        string json = "{}";
        if (_panel.Controls.OfType<FlowLayoutPanel>().FirstOrDefault() is FlowLayoutPanel p)
        {
            if ((string)p.Tag == "single")
            {
                var id = p.Controls.OfType<RadioButton>().FirstOrDefault(x => x.Checked)?.Tag as long? ?? 0;
                json = JsonUtil.Serialize(new { selectedOptionId = id });
            }
            if ((string)p.Tag == "multi")
            {
                var ids = p.Controls.OfType<CheckBox>().Where(x => x.Checked).Select(x => (long)x.Tag).ToList();
                json = JsonUtil.Serialize(new { selectedOptionIds = ids });
            }
        }
        if (_panel.Controls.OfType<TextBox>().FirstOrDefault() is TextBox tb) json = JsonUtil.Serialize(new { text = tb.Text });
        if (_panel.Controls.OfType<NumericUpDown>().FirstOrDefault() is NumericUpDown nu) json = JsonUtil.Serialize(new { value = (double)nu.Value });
        AttemptLogic.SaveAnswer(_attemptId, q.Id, json);
    }
}
