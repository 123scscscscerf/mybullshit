using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PlatformApp;

public sealed class ConstructorForm : Form
{
    private readonly SessionUser _teacher;
    private readonly long _testId;
    private readonly ListBox _questions = new() { Dock = DockStyle.Left, Width = 260 };
    private readonly TextBox _title = new() { Width = 260 };
    private readonly TextBox _desc = new() { Width = 260 };
    private readonly NumericUpDown _pass = new() { Width = 80, Minimum = 1, Maximum = 100, Value = 60 };
    private readonly NumericUpDown _points = new() { Width = 80, DecimalPlaces = 1, Minimum = 1, Maximum = 100, Value = 1 };
    private readonly TextBox _qText = new() { Width = 520 };
    private readonly TextBox[] _opts = new[] { new TextBox { Width = 380 }, new TextBox { Width = 380 }, new TextBox { Width = 380 }, new TextBox { Width = 380 } };
    private readonly RadioButton[] _corr = new[] { new RadioButton(), new RadioButton(), new RadioButton(), new RadioButton() };
    private readonly ComboBox _qType = new() { Width = 170, DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly List<QuestionEntity> _list = new();
    private readonly Dictionary<int, List<OptionEntity>> _optMap = new();

    private sealed class ConstructorDump
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public int PassPercent { get; set; }
        public List<QuestionEntity> Questions { get; set; } = new();
        public Dictionary<int, List<OptionEntity>> Options { get; set; } = new();
    }

    public ConstructorForm(SessionUser teacher, long testId)
    {
        _teacher = teacher; _testId = testId;
        Theme.Apply(this);
        Text = "Конструктор теста";

        _qType.DataSource = Enum.GetValues<QuestionType>();

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 280 };
        var leftTop = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8) };
        leftTop.Controls.Add(Theme.Btn("Add", (_, _) => AddQuestion(), true));
        leftTop.Controls.Add(Theme.Btn("Clone", (_, _) => CloneQuestion()));
        leftTop.Controls.Add(Theme.Btn("Delete", (_, _) => DeleteQuestion()));
        leftTop.Controls.Add(Theme.Btn("Up", (_, _) => MoveQuestion(-1)));
        leftTop.Controls.Add(Theme.Btn("Down", (_, _) => MoveQuestion(1)));
        split.Panel1.Controls.Add(_questions); split.Panel1.Controls.Add(leftTop);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 12 };
        right.RowStyles.Clear(); for (int i = 0; i < 12; i++) right.RowStyles.Add(new RowStyle(SizeType.AutoSize)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        right.Controls.Add(new Label { Text = "Title", Font = Theme.HeaderFont, AutoSize = true });
        right.Controls.Add(_title);
        right.Controls.Add(new Label { Text = "Description", AutoSize = true });
        right.Controls.Add(_desc);
        right.Controls.Add(new FlowLayoutPanel { AutoSize = true, Controls = { new Label { Text = "PassPercent" }, _pass } });
        right.Controls.Add(new FlowLayoutPanel { AutoSize = true, Controls = { new Label { Text = "Question type" }, _qType } });
        right.Controls.Add(new Label { Text = "Question text", AutoSize = true });
        right.Controls.Add(_qText);
        right.Controls.Add(new FlowLayoutPanel { AutoSize = true, Controls = { new Label { Text = "Points" }, _points } });

        var answers = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, AutoSize = true };
        for (int i = 0; i < 4; i++)
        {
            answers.Controls.Add(new Label { Text = (i + 1).ToString(), AutoSize = true }, 0, i);
            answers.Controls.Add(_opts[i], 1, i);
            answers.Controls.Add(_corr[i], 2, i);
        }
        right.Controls.Add(new Label { Text = "Answers (for choice)", AutoSize = true });
        right.Controls.Add(answers);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(12) };
        bottom.Controls.Add(Theme.Btn("Save Draft", (_, _) => SaveDraft(), true));
        bottom.Controls.Add(Theme.Btn("Export JSON", (_, _) => ExportJson()));
        bottom.Controls.Add(Theme.Btn("Import JSON", (_, _) => ImportJson()));
        bottom.Controls.Add(Theme.Btn("Generate 10 MCQ", (_, _) => GenerateTemplate()));

        split.Panel2.Controls.Add(right);
        Controls.Add(split);
        Controls.Add(bottom);

        _questions.SelectedIndexChanged += (_, _) => LoadSelected();
        _qType.SelectedIndexChanged += (_, _) => UpdateTypeEditorState();

        LoadData();
    }

    private void LoadData()
    {
        var t = EngineDb.GetTest(_testId);
        _title.Text = t.Title; _desc.Text = t.Description; _pass.Value = t.PassPercent;
        _list.Clear(); _list.AddRange(EngineDb.GetQuestions(_testId));
        _optMap.Clear();
        for (int i = 0; i < _list.Count; i++) _optMap[i] = EngineDb.GetOptions(_list[i].Id);
        Rebind();
    }

    private void Rebind()
    {
        _questions.DataSource = null;
        _questions.DataSource = _list.Select((x, i) => $"{i + 1}. [{x.Type}] {x.Text}").ToList();
        if (_questions.Items.Count > 0)
        {
            var idx = Math.Clamp(_questions.SelectedIndex < 0 ? 0 : _questions.SelectedIndex, 0, _questions.Items.Count - 1);
            _questions.SelectedIndex = idx;
        }
        else
        {
            _qText.Clear();
            for (var i = 0; i < 4; i++) { _opts[i].Clear(); _corr[i].Checked = false; }
        }
    }

    private void AddQuestion()
    {
        _list.Add(new QuestionEntity { Type = QuestionType.SingleChoice, Text = "Новый вопрос", Points = 1, SettingsJson = "{}" });
        _optMap[_list.Count - 1] = new List<OptionEntity>
        {
            new() { Text = "A", IsCorrect = true, SortOrder = 1 }, new() { Text = "B", SortOrder = 2 }, new() { Text = "C", SortOrder = 3 }, new() { Text = "D", SortOrder = 4 }
        };
        Rebind();
    }

    private void CloneQuestion()
    {
        var i = _questions.SelectedIndex; if (i < 0) return;
        if (!SaveCurrentEditorToModel()) return;
        var src = _list[i];
        var q = new QuestionEntity { Type = src.Type, Text = src.Text + " (copy)", Points = src.Points, SettingsJson = src.SettingsJson };
        _list.Insert(i + 1, q);
        var ops = _optMap.GetValueOrDefault(i, new List<OptionEntity>()).Select(x => new OptionEntity { Text = x.Text, IsCorrect = x.IsCorrect, SortOrder = x.SortOrder }).ToList();

        var newMap = new Dictionary<int, List<OptionEntity>>();
        for (int k = 0; k < _list.Count; k++)
        {
            if (k <= i) newMap[k] = _optMap.GetValueOrDefault(k, new List<OptionEntity>());
            else if (k == i + 1) newMap[k] = ops;
            else newMap[k] = _optMap.GetValueOrDefault(k - 1, new List<OptionEntity>());
        }
        _optMap.Clear(); foreach (var p in newMap) _optMap[p.Key] = p.Value;
        Rebind(); _questions.SelectedIndex = i + 1;
    }

    private void DeleteQuestion()
    {
        var i = _questions.SelectedIndex; if (i < 0) return;
        _list.RemoveAt(i);
        _optMap.Remove(i);
        ReindexMap();
        Rebind();
    }

    private void MoveQuestion(int d)
    {
        var i = _questions.SelectedIndex; if (i < 0) return;
        var j = i + d; if (j < 0 || j >= _list.Count) return;
        (_list[i], _list[j]) = (_list[j], _list[i]);
        (_optMap[i], _optMap[j]) = (_optMap[j], _optMap[i]);
        Rebind(); _questions.SelectedIndex = j;
    }

    private void ReindexMap()
    {
        var n = new Dictionary<int, List<OptionEntity>>();
        for (int k = 0; k < _list.Count; k++) n[k] = _optMap.ContainsKey(k) ? _optMap[k] : new List<OptionEntity>();
        _optMap.Clear(); foreach (var p in n) _optMap[p.Key] = p.Value;
    }

    private void LoadSelected()
    {
        var i = _questions.SelectedIndex; if (i < 0) return;
        var q = _list[i];
        _qText.Text = q.Text; _points.Value = (decimal)q.Points; _qType.SelectedItem = q.Type;

        var ops = _optMap.GetValueOrDefault(i) ?? new List<OptionEntity>();
        while (ops.Count < 4) ops.Add(new OptionEntity { Text = "", SortOrder = ops.Count + 1 });
        for (int k = 0; k < 4; k++) { _opts[k].Text = ops[k].Text; _corr[k].Checked = ops[k].IsCorrect; }
        _optMap[i] = ops;
        UpdateTypeEditorState();
    }

    private void UpdateTypeEditorState()
    {
        var qt = (QuestionType?)_qType.SelectedItem ?? QuestionType.SingleChoice;
        var choiceMode = qt == QuestionType.SingleChoice || qt == QuestionType.MultipleChoice;
        foreach (var tb in _opts) tb.Enabled = choiceMode;
        foreach (var rb in _corr) rb.Enabled = qt == QuestionType.SingleChoice;
    }

    private bool SaveCurrentEditorToModel()
    {
        var i = _questions.SelectedIndex; if (i < 0) return true;
        if (string.IsNullOrWhiteSpace(_qText.Text)) { MessageBox.Show("Введите текст вопроса"); return false; }

        var type = (QuestionType?)_qType.SelectedItem ?? QuestionType.SingleChoice;
        _list[i].Text = _qText.Text.Trim();
        _list[i].Points = (double)_points.Value;
        _list[i].Type = type;

        if (type == QuestionType.SingleChoice)
        {
            var texts = _opts.Select(x => x.Text.Trim()).ToArray();
            if (texts.Any(string.IsNullOrWhiteSpace)) { MessageBox.Show("Все 4 ответа обязательны"); return false; }
            var ci = Array.FindIndex(_corr, x => x.Checked);
            if (ci < 0) { MessageBox.Show("Выберите правильный ответ"); return false; }
            _optMap[i] = Enumerable.Range(0, 4).Select(k => new OptionEntity { Text = texts[k], IsCorrect = k == ci, SortOrder = k + 1 }).ToList();
            _list[i].SettingsJson = "{}";
        }
        else if (type == QuestionType.MultipleChoice)
        {
            var texts = _opts.Select(x => x.Text.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            if (texts.Length < 2) { MessageBox.Show("Минимум 2 варианта ответа"); return false; }
            _optMap[i] = texts.Select((t, idx) => new OptionEntity { Text = t, IsCorrect = idx == 0, SortOrder = idx + 1 }).ToList();
            _list[i].SettingsJson = "{}";
        }
        else if (type == QuestionType.Text)
        {
            _optMap[i] = new List<OptionEntity>();
            _list[i].SettingsJson = JsonUtil.To(new TextSettings { Accepted = new List<string> { "пример" }, CaseInsensitive = true, Trim = true, AllowManualCheck = true });
        }
        else if (type == QuestionType.Numeric)
        {
            _optMap[i] = new List<OptionEntity>();
            _list[i].SettingsJson = JsonUtil.To(new NumericSettings { Correct = 0, Tolerance = 0.1 });
        }

        Rebind();
        return true;
    }

    private void SaveDraft()
    {
        if (!SaveCurrentEditorToModel()) return;
        if (string.IsNullOrWhiteSpace(_title.Text)) { MessageBox.Show("Title required"); return; }
        var t = EngineDb.GetTest(_testId);
        if (t.Status != TestStatus.Draft) { MessageBox.Show("Published test is locked. Clone test first."); return; }

        using var c = EngineDb.Open(); using var tx = c.BeginTransaction();
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE Tests SET Title=@t,Description=@d,PassPercent=@p WHERE Id=@id";
            cmd.Parameters.AddWithValue("@t", _title.Text.Trim());
            cmd.Parameters.AddWithValue("@d", _desc.Text.Trim());
            cmd.Parameters.AddWithValue("@p", (int)_pass.Value);
            cmd.Parameters.AddWithValue("@id", _testId);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();

        EngineDb.ReplaceTestQuestions(_testId, _list, _optMap, _teacher.Id);
        MessageBox.Show("Сохранено");
    }

    private void ExportJson()
    {
        if (!SaveCurrentEditorToModel()) return;
        using var sfd = new SaveFileDialog { Filter = "JSON|*.json", FileName = "constructor_export.json" };
        if (sfd.ShowDialog() != DialogResult.OK) return;
        var dump = new ConstructorDump { Title = _title.Text.Trim(), Description = _desc.Text.Trim(), PassPercent = (int)_pass.Value, Questions = _list, Options = _optMap };
        File.WriteAllText(sfd.FileName, JsonUtil.To(dump));
        MessageBox.Show("Экспортировано");
    }

    private void ImportJson()
    {
        using var ofd = new OpenFileDialog { Filter = "JSON|*.json" };
        if (ofd.ShowDialog() != DialogResult.OK) return;
        var dump = JsonUtil.From<ConstructorDump>(File.ReadAllText(ofd.FileName));
        _title.Text = dump.Title;
        _desc.Text = dump.Description;
        _pass.Value = Math.Clamp(dump.PassPercent, 1, 100);
        _list.Clear(); _list.AddRange(dump.Questions ?? new List<QuestionEntity>());
        _optMap.Clear();
        if (dump.Options != null)
            foreach (var kv in dump.Options) _optMap[kv.Key] = kv.Value;
        ReindexMap();
        Rebind();
        MessageBox.Show("Импортировано");
    }

    private void GenerateTemplate()
    {
        var start = _list.Count;
        for (int i = 1; i <= 10; i++)
        {
            _list.Add(new QuestionEntity { Type = QuestionType.SingleChoice, Text = $"Вопрос {start + i}", Points = 1, SettingsJson = "{}" });
            _optMap[_list.Count - 1] = new List<OptionEntity>
            {
                new() { Text = "Вариант A", IsCorrect = true, SortOrder = 1 },
                new() { Text = "Вариант B", IsCorrect = false, SortOrder = 2 },
                new() { Text = "Вариант C", IsCorrect = false, SortOrder = 3 },
                new() { Text = "Вариант D", IsCorrect = false, SortOrder = 4 }
            };
        }
        Rebind();
    }
}
