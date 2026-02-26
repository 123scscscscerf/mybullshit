using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PlatformApp;

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
    private readonly TextBox _login = new() { Width = 260 };
    private readonly TextBox _password = new() { Width = 260, PasswordChar = '•' };
    private readonly Dictionary<string, (int count, DateTime until)> _fails = new();

    public LoginForm()
    {
        Theme.Apply(this);
        Text = "Платформа тестирования — Вход";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Size = new Size(460, 320);
        MinimumSize = new Size(460, 320);
        MaximumSize = new Size(460, 320);

        var card = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24), BackColor = Theme.Panel };
        var lay = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        lay.Controls.Add(new Label { Text = "Вход", Font = Theme.HeaderFont, AutoSize = true });
        lay.Controls.Add(new Label { Text = "Login" }); lay.Controls.Add(_login);
        lay.Controls.Add(new Label { Text = "Password" }); lay.Controls.Add(_password);
        lay.Controls.Add(Theme.Btn("Войти", OnLogin, true));
        lay.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(71, 85, 105),
            Margin = new Padding(0, 10, 0, 0),
            Text = "Demo: admin/admin123, teacher1/teacher123, student1..6/student123"
        });
        card.Controls.Add(lay);
        Controls.Add(card);
    }

    private void OnLogin(object? s, EventArgs e)
    {
        var key = _login.Text.Trim().ToLowerInvariant();
        if (_fails.TryGetValue(key, out var f) && f.until > DateTime.UtcNow)
        {
            MessageBox.Show("Слишком много попыток. Подождите 2 минуты.");
            return;
        }

        var u = EngineDb.FindByLogin(_login.Text.Trim());
        if (u == null || !u.IsActive || !Security.Verify(_password.Text, u.PasswordSalt, u.PasswordHash))
        {
            var c = _fails.TryGetValue(key, out var old) ? old.count + 1 : 1;
            _fails[key] = c >= 5 ? (c, DateTime.UtcNow.AddMinutes(2)) : (c, DateTime.MinValue);
            using var cn = EngineDb.Open(); using var tx = cn.BeginTransaction(); EngineDb.InsertAudit(cn, tx, u?.Id ?? 1, "login.fail", "User", u?.Id ?? 0, "{}"); tx.Commit();
            MessageBox.Show("Неверный логин/пароль");
            return;
        }

        using (var cn = EngineDb.Open()) { using var tx = cn.BeginTransaction(); EngineDb.InsertAudit(cn, tx, u.Id, "login.success", "User", u.Id, "{}"); tx.Commit(); }
        Hide();
        using var shell = new MainShellForm(new SessionUser { Id = u.Id, Login = u.Login, DisplayName = u.DisplayName, Role = u.Role });
        shell.ShowDialog();
        Show();
    }
}

public sealed class MainShellForm : Form
{
    private readonly SessionUser _me;
    private readonly Panel _work = new() { Dock = DockStyle.Fill, BackColor = Color.White };
    private readonly ListBox _menu = new() { Dock = DockStyle.Fill };
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusLabel = new("Ready");

    public MainShellForm(SessionUser me)
    {
        _me = me;
        Theme.Apply(this);
        WindowState = FormWindowState.Maximized;
        Text = "Платформа тестирования";

        var header = new Panel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(12), BackColor = Theme.Panel };
        header.Controls.Add(new Label { Text = $"{me.DisplayName} ({me.Role})", Dock = DockStyle.Left, AutoSize = true, Font = Theme.HeaderFont });
        var logout = Theme.Btn("Выйти", (_, _) => Close()); logout.Dock = DockStyle.Right; header.Controls.Add(logout);
        var pass = Theme.Btn("Сменить пароль", (_, _) => ChangePassword()); pass.Dock = DockStyle.Right; header.Controls.Add(pass);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 240, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.BackColor = Theme.Panel; split.Panel1.Padding = new Padding(8);
        split.Panel1.Controls.Add(_menu);
        split.Panel2.Padding = new Padding(12);
        split.Panel2.Controls.Add(_work);

        _status.Items.Add(_statusLabel);
        Controls.Add(split); Controls.Add(header); Controls.Add(_status);

        LoadMenu();
        _menu.SelectedIndexChanged += (_, _) => Render();
        if (_menu.Items.Count > 0) _menu.SelectedIndex = 0;
    }

    private void LoadMenu()
    {
        if (_me.Role == UserRole.Admin) _menu.Items.AddRange(new object[] { "Users", "Groups", "AuditLog" });
        if (_me.Role == UserRole.Teacher) _menu.Items.AddRange(new object[] { "Tests", "Assignments", "Results", "Analytics" });
        if (_me.Role == UserRole.Student) _menu.Items.AddRange(new object[] { "Available Tests", "My Attempts" });
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    private void Render()
    {
        _work.Controls.Clear();
        if (_menu.SelectedItem == null) return;
        var item = _menu.SelectedItem.ToString()!;
        if (item == "Users") BuildAdminUsers();
        else if (item == "Groups") BuildAdminGroups();
        else if (item == "AuditLog") BuildAudit();
        else if (item == "Tests") BuildTeacherTests();
        else if (item == "Assignments") BuildAssignments();
        else if (item == "Results") BuildResults();
        else if (item == "Analytics") BuildAnalytics();
        else if (item == "Available Tests") BuildAvailable();
        else if (item == "My Attempts") BuildMyAttempts();
    }

    private void BuildAdminUsers()
    {
        var g = Theme.Grid();
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(4) };
        var login = new TextBox { Width = 120 }; var name = new TextBox { Width = 150 }; var role = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList }; role.DataSource = Enum.GetValues<UserRole>();
        top.Controls.AddRange(new Control[] { new Label { Text = "Login" }, login, new Label { Text = "Name" }, name, role,
            Theme.Btn("Add", (_,_)=>{ if(string.IsNullOrWhiteSpace(login.Text)||string.IsNullOrWhiteSpace(name.Text)){MessageBox.Show("Введите данные");return;} using var d=new PasswordInputDialog("Пароль"); if(d.ShowDialog()!=DialogResult.OK)return; EngineDb.AddUser(login.Text.Trim(),name.Text.Trim(),(UserRole)role.SelectedItem!,d.Value,_me.Id); Reload(); SetStatus("Сохранено");}),
            Theme.Btn("Edit",(_,_)=>{ if(g.CurrentRow?.DataBoundItem is not User u)return; var nm=Microsoft.VisualBasic.Interaction.InputBox("DisplayName","Edit",u.DisplayName); if(string.IsNullOrWhiteSpace(nm))return; EngineDb.UpdateUser(u.Id,nm,u.Role,u.IsActive,_me.Id); Reload();}),
            Theme.Btn("Reset Password",(_,_)=>{ if(g.CurrentRow?.DataBoundItem is not User u)return; using var d=new PasswordInputDialog("Новый пароль"); if(d.ShowDialog()!=DialogResult.OK)return; EngineDb.SetPassword(u.Id,d.Value,_me.Id); SetStatus("Пароль обновлён");}),
            Theme.Btn("Enable/Disable",(_,_)=>{ if(g.CurrentRow?.DataBoundItem is not User u)return; EngineDb.UpdateUser(u.Id,u.DisplayName,u.Role,!u.IsActive,_me.Id); Reload();}),
            Theme.Btn("Delete",(_,_)=>{ if(g.CurrentRow?.DataBoundItem is not User u)return; EngineDb.SoftDeleteUser(u.Id,_me.Id); Reload(); }) });
        _work.Controls.Add(g); _work.Controls.Add(top);
        void Reload() => g.DataSource = EngineDb.GetUsers().Select(u => new { u.Id, u.Login, u.DisplayName, u.Role, Active = u.IsActive }).ToList();
        Reload();
    }

    private void BuildAdminGroups()
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 320 };
        var gl = Theme.Grid();
        var st = Theme.Grid();

        var leftTop = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42 };
        var gname = new TextBox { Width = 150 };

        var rt = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42 };
        var filter = new TextBox { Width = 140 };
        var cmb = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };

        void ReloadMembers()
        {
            if (gl.CurrentRow?.DataBoundItem is Group g)
                st.DataSource = EngineDb.GetGroupStudents(g.Id);
        }

        void LoadStudents()
        {
            var query = EngineDb.GetUsers().Where(x => x.Role == UserRole.Student && x.IsActive);
            var ftxt = filter.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(ftxt))
                query = query.Where(x => x.DisplayName.Contains(ftxt, StringComparison.OrdinalIgnoreCase) || x.Login.Contains(ftxt, StringComparison.OrdinalIgnoreCase));
            cmb.DataSource = query.ToList();
            cmb.DisplayMember = "DisplayName";
        }

        void ReloadGroups()
        {
            gl.DataSource = EngineDb.GetGroups();
            LoadStudents();
            ReloadMembers();
        }

        var createBtn = Theme.Btn("Create", (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(gname.Text)) return;
            EngineDb.AddGroup(gname.Text.Trim(), _me.Id);
            gname.Clear();
            ReloadGroups();
        });

        var renameBtn = Theme.Btn("Rename", (_, _) =>
        {
            if (gl.CurrentRow?.DataBoundItem is not Group g) return;
            var n = Microsoft.VisualBasic.Interaction.InputBox("Name", "Rename", g.Name);
            if (string.IsNullOrWhiteSpace(n)) return;
            EngineDb.RenameGroup(g.Id, n.Trim(), _me.Id);
            ReloadGroups();
        });

        var deleteBtn = Theme.Btn("Delete", (_, _) =>
        {
            if (gl.CurrentRow?.DataBoundItem is not Group g) return;
            EngineDb.DeleteGroup(g.Id, _me.Id);
            ReloadGroups();
        });

        var addBtn = Theme.Btn("Add", (_, _) =>
        {
            if (gl.CurrentRow?.DataBoundItem is not Group g || cmb.SelectedItem is not User u) return;
            EngineDb.SetGroupMember(g.Id, u.Id, true, _me.Id);
            ReloadMembers();
        });

        var removeBtn = Theme.Btn("Remove", (_, _) =>
        {
            if (gl.CurrentRow?.DataBoundItem is not Group g || st.CurrentRow?.DataBoundItem is not User u) return;
            EngineDb.SetGroupMember(g.Id, u.Id, false, _me.Id);
            ReloadMembers();
        });

        leftTop.Controls.AddRange(new Control[] { gname, createBtn, renameBtn, deleteBtn });
        rt.Controls.AddRange(new Control[] { new Label { Text = "Filter" }, filter, cmb, addBtn, removeBtn });

        split.Panel1.Controls.Add(gl);
        split.Panel1.Controls.Add(leftTop);
        split.Panel2.Controls.Add(st);
        split.Panel2.Controls.Add(rt);
        _work.Controls.Add(split);

        gl.SelectionChanged += (_, _) => ReloadMembers();
        filter.TextChanged += (_, _) => LoadStudents();

        ReloadGroups();
    }

    private void BuildAudit()
    {
        var g = Theme.Grid();
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42 };
        var actor = new TextBox { Width = 110 }; var action = new TextBox { Width = 120 }; var from = new DateTimePicker { Width = 150 }; var to = new DateTimePicker { Width = 150 };
        top.Controls.AddRange(new Control[] { new Label { Text = "Actor" }, actor, new Label { Text = "Action" }, action, from, to, Theme.Btn("Refresh", (_, _) => g.DataSource = EngineDb.GetAudit(actor.Text, action.Text, from.Value.Date, to.Value.Date.AddDays(1).AddSeconds(-1))) });
        _work.Controls.Add(g); _work.Controls.Add(top);
        g.DataSource = EngineDb.GetAudit();
    }

    private void BuildTeacherTests()
    {
        var g = Theme.Grid();
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46 };
        var title = new TextBox { Width = 180 };
        top.Controls.AddRange(new Control[] { title,
            Theme.Btn("Create",(_,_)=>{ if(string.IsNullOrWhiteSpace(title.Text))return; var id=EngineDb.AddTest(new TestEntity{Title=title.Text,Description="",CreatedByTeacherId=_me.Id,Status=TestStatus.Draft},_me.Id); Reload(); SetStatus($"Test #{id} created"); }, true),
            Theme.Btn("Open in Constructor",(_,_)=>{ if(g.CurrentRow?.DataBoundItem is not TestEntity t)return; using var f=new ConstructorForm(_me,t.Id); f.ShowDialog(); Reload();}),
            Theme.Btn("Publish",(_,_)=>{ if(g.CurrentRow?.DataBoundItem is TestEntity t){ EngineDb.UpdateTestStatus(t.Id,TestStatus.Published,_me.Id); Reload();}}),
            Theme.Btn("Archive",(_,_)=>{ if(g.CurrentRow?.DataBoundItem is TestEntity t){ EngineDb.UpdateTestStatus(t.Id,TestStatus.Archived,_me.Id); Reload();}}),
            Theme.Btn("Clone",(_,_)=>{ if(g.CurrentRow?.DataBoundItem is TestEntity t){ EngineDb.CloneTest(t.Id,_me.Id); Reload();}})
        });
        _work.Controls.Add(g); _work.Controls.Add(top);
        void Reload() => g.DataSource = EngineDb.GetTestsByTeacher(_me.Id);
        Reload();
    }

    private void BuildAssignments()
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 340 };
        var tg = Theme.Grid(); var ag = Theme.Grid();
        split.Panel1.Controls.Add(tg); split.Panel2.Controls.Add(ag);
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50 };
        var tt = new ComboBox { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList }; tt.DataSource = Enum.GetValues<TargetType>();
        var target = new ComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
        var al = new NumericUpDown { Width = 60, Minimum = 1, Maximum = 10, Value = 1 };
        var tm = new NumericUpDown { Width = 70, Minimum = 0, Maximum = 600, Value = 30 };
        var dl = new DateTimePicker { Width = 170, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm" };
        var sq = new CheckBox { Text = "ShuffleQ", Checked = true }; var so = new CheckBox { Text = "ShuffleO", Checked = true }; var ss = new CheckBox { Text = "ScoreAfter", Checked = true }; var sc = new CheckBox { Text = "CorrectAfter" };
        top.Controls.AddRange(new Control[] { tt, target, al, tm, dl, sq, so, ss, sc, Theme.Btn("Assign", (_, _) => { if (tg.CurrentRow?.DataBoundItem is not TestEntity t || target.SelectedItem == null) return; var tid = target.SelectedItem is Group g ? g.Id : ((User)target.SelectedItem).Id; EngineDb.AddAssignment(new Assignment { TestId = t.Id, TargetType = (TargetType)tt.SelectedItem!, TargetId = tid, AvailableFrom = TimeUtil.Iso(TimeUtil.UtcNow), Deadline = TimeUtil.Iso(dl.Value.ToUniversalTime()), AttemptLimit = (int)al.Value, TimeLimitMinutes = (int)tm.Value == 0 ? null : (int)tm.Value, ShuffleQuestions = sq.Checked, ShuffleOptions = so.Checked, ShowScoreAfter = ss.Checked, ShowCorrectAfter = sc.Checked, IsActive = true }, _me.Id); ReloadA(); }, true), Theme.Btn("Activate/Deactivate", (_, _) => { if (ag.CurrentRow?.DataBoundItem is not Assignment a) return; EngineDb.SetAssignmentActive(a.Id, !a.IsActive, _me.Id); ReloadA(); }) });
        _work.Controls.Add(split); _work.Controls.Add(top);
        void LoadTarget() { if ((TargetType)tt.SelectedItem! == TargetType.Group) { target.DataSource = EngineDb.GetGroups(); target.DisplayMember = "Name"; } else { target.DataSource = EngineDb.GetUsers().Where(x => x.Role == UserRole.Student && x.IsActive).ToList(); target.DisplayMember = "DisplayName"; } }
        void ReloadA() { if (tg.CurrentRow?.DataBoundItem is TestEntity t) ag.DataSource = EngineDb.GetAssignmentsByTest(t.Id); }
        tg.DataSource = EngineDb.GetTestsByTeacher(_me.Id);
        tt.SelectedIndexChanged += (_, _) => LoadTarget(); tg.SelectionChanged += (_, _) => ReloadA(); LoadTarget();
    }

    private void BuildResults()
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 320 };
        var tg = Theme.Grid(); var ag = Theme.Grid();
        tg.DataSource = EngineDb.GetTestsByTeacher(_me.Id);
        split.Panel1.Controls.Add(tg); split.Panel2.Controls.Add(ag);
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46 };
        top.Controls.AddRange(new Control[] { Theme.Btn("Open Attempt", (_, _) => { if (ag.CurrentRow?.Cells["Id"].Value is not long id) return; MessageBox.Show(JsonUtil.To(AttemptLogic.GetReview(_me, id))); }), Theme.Btn("Export CSV", (_, _) => { if (tg.CurrentRow?.DataBoundItem is not TestEntity t) return; using var sfd = new SaveFileDialog { Filter = "CSV|*.csv", FileName = $"results_{t.Id}.csv" }; if (sfd.ShowDialog() == DialogResult.OK) { AttemptLogic.ExportCsv(_me.Id, t.Id, sfd.FileName); SetStatus("Exported"); } }) });
        _work.Controls.Add(split); _work.Controls.Add(top);
        tg.SelectionChanged += (_, _) => { if (tg.CurrentRow?.DataBoundItem is not TestEntity t) return; ag.DataSource = EngineDb.GetAttemptsByTest(t.Id).Select(a => new { a.Id, Student = EngineDb.GetUsers().First(x => x.Id == a.UserId).DisplayName, a.Status, Result = EngineDb.GetResult(a.Id)?.Score, a.StartedAt, a.SubmittedAt }).ToList(); };
        if (tg.Rows.Count > 0) tg.Rows[0].Selected = true;
    }

    private void BuildAnalytics()
    {
        var cmb = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        cmb.DataSource = EngineDb.GetTestsByTeacher(_me.Id); cmb.DisplayMember = "Title";
        var box = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both };
        var btn = Theme.Btn("Calculate", (_, _) => { if (cmb.SelectedItem is not TestEntity t) return; box.Text = JsonUtil.To(AttemptLogic.Analytics(_me.Id, t.Id)); }, true); btn.Dock = DockStyle.Top;
        _work.Controls.Add(box); _work.Controls.Add(btn); _work.Controls.Add(cmb);
    }

    private void BuildAvailable()
    {
        var g = Theme.Grid(); var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46 };
        top.Controls.Add(Theme.Btn("Start", (_, _) => { if (g.CurrentRow?.DataBoundItem is not AvailableAssignment a) return; var id = AttemptLogic.StartAttempt(_me.Id, a.AssignmentId); using var f = new AttemptPlayerForm(_me, id); f.ShowDialog(); Reload(); }, true));
        top.Controls.Add(Theme.Btn("Refresh", (_, _) => Reload()));
        _work.Controls.Add(g); _work.Controls.Add(top);
        void Reload() => g.DataSource = AttemptLogic.ListAvailableAssignments(_me.Id);
        Reload();
    }

    private void BuildMyAttempts()
    {
        var g = Theme.Grid(); var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46 };
        top.Controls.AddRange(new Control[] { Theme.Btn("Refresh", (_, _) => Reload()), Theme.Btn("Review", (_, _) => { if (g.CurrentRow?.Cells["Id"].Value is not long id) return; var a = EngineDb.GetAttempt(id); if (a.Status == AttemptStatus.Active) { using var f = new AttemptPlayerForm(_me, id); f.ShowDialog(); } else MessageBox.Show(JsonUtil.To(AttemptLogic.GetReview(_me, id))); Reload(); }) });
        _work.Controls.Add(g); _work.Controls.Add(top);
        void Reload()
        {
            var rows = new List<object>();
            foreach (var a in AttemptLogic.ListAvailableAssignments(_me.Id)) foreach (var at in EngineDb.GetAttemptsByAssignmentAndUser(a.AssignmentId, _me.Id)) { var r = EngineDb.GetResult(at.Id); rows.Add(new { at.Id, Test = a.TestTitle, at.Status, Score = r?.Score, Percent = r?.Percent, at.StartedAt, at.SubmittedAt }); }
            g.DataSource = rows.OrderByDescending(x => ((dynamic)x).Id).ToList();
        }
        Reload();
    }

    private void ChangePassword()
    {
        using var d = new ChangePasswordForm(_me);
        d.ShowDialog();
    }
}

public sealed class AttemptPlayerForm : Form
{
    private readonly SessionUser _u;
    private readonly long _attemptId;
    private readonly AttemptSnapshot _snap;
    private int _idx;
    private readonly Label _timer = new() { Dock = DockStyle.Top, Height = 32, Font = Theme.HeaderFont };
    private readonly ListBox _nav = new() { Dock = DockStyle.Left, Width = 220 };
    private readonly Panel _qPanel = new() { Dock = DockStyle.Fill, Padding = new Padding(10) };
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };

    public AttemptPlayerForm(SessionUser u, long attemptId)
    {
        _u = u; _attemptId = attemptId;
        Theme.Apply(this); Text = "Прохождение теста";
        var a = EngineDb.GetAttempt(attemptId); _snap = JsonUtil.From<AttemptSnapshot>(a.SnapshotJson);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48 };
        bottom.Controls.AddRange(new Control[] { Theme.Btn("Back", (_, _) => NavigateQuestion(-1)), Theme.Btn("Next", (_, _) => { SaveCurrent(); NavigateQuestion(1); }), Theme.Btn("Save", (_, _) => SaveCurrent()), Theme.Btn("Submit", (_, _) => { SaveCurrent(); AttemptLogic.SubmitAttempt(_attemptId, "manual"); Close(); }, true) });
        Controls.Add(_qPanel); Controls.Add(_nav); Controls.Add(bottom); Controls.Add(_timer);
        _nav.Items.AddRange(_snap.Questions.Select((x, i) => $"{i + 1}. {x.Type}").ToArray());
        _nav.SelectedIndexChanged += (_, _) => { if (_nav.SelectedIndex >= 0) { _idx = _nav.SelectedIndex; RenderQuestion(); } };
        _tick.Tick += (_, _) => Tick(); _tick.Start();
        FormClosing += OnClosing;
        _nav.SelectedIndex = 0;
    }

    private void Tick()
    {
        var a = EngineDb.GetAttempt(_attemptId);
        if (a.EndsAt == null) { _timer.Text = _snap.TestTitle; return; }
        var left = TimeUtil.Parse(a.EndsAt) - TimeUtil.UtcNow;
        _timer.Text = left <= TimeSpan.Zero ? "Время вышло" : $"{_snap.TestTitle} — осталось {left:hh\\:mm\\:ss}";
        if (left <= TimeSpan.Zero && a.Status == AttemptStatus.Active) { AttemptLogic.SubmitAttempt(_attemptId, "timeExpired"); MessageBox.Show("Время вышло. Попытка отправлена."); Close(); }
    }

    private void OnClosing(object? s, FormClosingEventArgs e)
    {
        var a = EngineDb.GetAttempt(_attemptId);
        if (a.Status != AttemptStatus.Active) return;
        var r = MessageBox.Show("Сдать попытку сейчас? (Нет — продолжить позже)", "Выход", MessageBoxButtons.YesNoCancel);
        if (r == DialogResult.Cancel) e.Cancel = true;
        if (r == DialogResult.Yes) { SaveCurrent(); AttemptLogic.SubmitAttempt(_attemptId, "manual"); }
    }

    private void NavigateQuestion(int d) { var n = _idx + d; if (n < 0 || n >= _snap.Questions.Count) return; _nav.SelectedIndex = n; }

    private void RenderQuestion()
    {
        _qPanel.Controls.Clear();
        var q = _snap.Questions[_idx];
        _qPanel.Controls.Add(new Label { Text = q.Text, Dock = DockStyle.Top, Height = 60, Font = Theme.HeaderFont });
        if (q.Type == QuestionType.SingleChoice)
        {
            var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Tag = "single" };
            foreach (var o in q.Options.Take(4)) p.Controls.Add(new RadioButton { Text = o.Text, Tag = o.Id, AutoSize = true });
            _qPanel.Controls.Add(p);
        }
        else if (q.Type == QuestionType.MultipleChoice)
        {
            var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Tag = "multi" };
            foreach (var o in q.Options) p.Controls.Add(new CheckBox { Text = o.Text, Tag = o.Id, AutoSize = true });
            _qPanel.Controls.Add(p);
        }
        else if (q.Type == QuestionType.Text) _qPanel.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, Tag = "txt" });
        else _qPanel.Controls.Add(new NumericUpDown { Dock = DockStyle.Top, DecimalPlaces = 4, Maximum = 100000, Minimum = -100000, Tag = "num" });
    }

    private void SaveCurrent()
    {
        var q = _snap.Questions[_idx];
        string json = "{}";
        if (_qPanel.Controls.OfType<FlowLayoutPanel>().FirstOrDefault() is FlowLayoutPanel p)
        {
            if ((p.Tag as string) == "single") json = JsonUtil.To(new { selectedOptionId = p.Controls.OfType<RadioButton>().FirstOrDefault(x => x.Checked)?.Tag as long? ?? 0L });
            if ((p.Tag as string) == "multi") json = JsonUtil.To(new { selectedOptionIds = p.Controls.OfType<CheckBox>().Where(x => x.Checked).Select(x => x.Tag is long l ? l : 0L).Where(x => x != 0L).ToArray() });
        }
        if (_qPanel.Controls.OfType<TextBox>().FirstOrDefault() is TextBox tb) json = JsonUtil.To(new { text = tb.Text });
        if (_qPanel.Controls.OfType<NumericUpDown>().FirstOrDefault() is NumericUpDown n) json = JsonUtil.To(new { value = (double)n.Value });
        AttemptLogic.SaveAnswer(_attemptId, q.Id, json);
    }
}

public sealed class ChangePasswordForm : Form
{
    public ChangePasswordForm(SessionUser me)
    {
        Theme.Apply(this); Text = "Смена пароля"; Width = 420; Height = 280;
        var old = new TextBox { PasswordChar = '•', Width = 220 }; var nw = new TextBox { PasswordChar = '•', Width = 220 }; var cf = new TextBox { PasswordChar = '•', Width = 220 };
        var lay = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(16), WrapContents = false };
        lay.Controls.AddRange(new Control[] { new Label { Text = "Старый пароль" }, old, new Label { Text = "Новый пароль" }, nw, new Label { Text = "Подтверждение" }, cf,
            Theme.Btn("Сохранить",(_,_)=>{
                var u=EngineDb.GetUsers().First(x=>x.Id==me.Id);
                if(!Security.Verify(old.Text,u.PasswordSalt,u.PasswordHash)){MessageBox.Show("Неверный старый пароль"); return;}
                if(string.IsNullOrWhiteSpace(nw.Text)||nw.Text!=cf.Text){MessageBox.Show("Проверьте новый пароль"); return;}
                EngineDb.SetPassword(me.Id,nw.Text,me.Id); MessageBox.Show("Пароль обновлён"); Close();
            },true)
        });
        Controls.Add(lay);
    }
}

public sealed class PasswordInputDialog : Form
{
    private readonly TextBox _tb = new() { PasswordChar = '•', Width = 220 };
    public string Value => _tb.Text;
    public PasswordInputDialog(string title)
    {
        Theme.Apply(this); Text = title; Width = 330; Height = 170;
        var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(12), WrapContents = false };
        p.Controls.Add(_tb); p.Controls.Add(Theme.Btn("OK", (_, _) => { if (string.IsNullOrWhiteSpace(_tb.Text)) { MessageBox.Show("Пароль пустой"); return; } DialogResult = DialogResult.OK; }, true));
        Controls.Add(p);
    }
}
