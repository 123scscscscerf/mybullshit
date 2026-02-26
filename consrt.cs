using System;
using System.Collections.Generic;
using System.Drawing;
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

    private readonly List<QuestionEntity> _list = new();
    private readonly Dictionary<int, List<OptionEntity>> _optMap = new();

    public ConstructorForm(SessionUser teacher, long testId)
    {
        _teacher = teacher; _testId = testId;
        Theme.Apply(this);
        Text = "Конструктор теста";

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 280 };
        var leftTop = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8) };
        leftTop.Controls.Add(Theme.Btn("Add Question", (_, _) => AddQuestion(), true));
        leftTop.Controls.Add(Theme.Btn("Delete", (_, _) => DeleteQuestion()));
        leftTop.Controls.Add(Theme.Btn("Up", (_, _) => Move(-1)));
        leftTop.Controls.Add(Theme.Btn("Down", (_, _) => Move(1)));
        split.Panel1.Controls.Add(_questions); split.Panel1.Controls.Add(leftTop);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 10 };
        right.RowStyles.Clear(); for (int i = 0; i < 10; i++) right.RowStyles.Add(new RowStyle(SizeType.AutoSize)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        right.Controls.Add(new Label { Text = "Title", Font = Theme.HeaderFont, AutoSize = true });
        right.Controls.Add(_title);
        right.Controls.Add(new Label { Text = "Description", AutoSize = true });
        right.Controls.Add(_desc);
        right.Controls.Add(new FlowLayoutPanel { AutoSize = true, Controls = { new Label { Text = "PassPercent" }, _pass } });
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
        right.Controls.Add(new Label { Text = "Answers (choose correct)", AutoSize = true });
        right.Controls.Add(answers);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(12) };
        bottom.Controls.Add(Theme.Btn("Сохранить Draft", (_, _) => SaveDraft(), true));
        split.Panel2.Controls.Add(right);
        Controls.Add(split);
        Controls.Add(bottom);

        _questions.SelectedIndexChanged += (_, _) => LoadSelected();

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
        _questions.DataSource = _list.Select((x, i) => $"{i + 1}. {x.Text}").ToList();
        if (_questions.Items.Count > 0) _questions.SelectedIndex = Math.Clamp(_questions.SelectedIndex, 0, _questions.Items.Count - 1);
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

    private void DeleteQuestion()
    {
        var i = _questions.SelectedIndex; if (i < 0) return;
        _list.RemoveAt(i); _optMap.Remove(i);
        var n = new Dictionary<int, List<OptionEntity>>();
        for (int k = 0; k < _list.Count; k++) n[k] = _optMap.ContainsKey(k) ? _optMap[k] : new List<OptionEntity>();
        _optMap.Clear(); foreach (var p in n) _optMap[p.Key] = p.Value;
        Rebind();
    }

    private void Move(int d)
    {
        var i = _questions.SelectedIndex; if (i < 0) return;
        var j = i + d; if (j < 0 || j >= _list.Count) return;
        (_list[i], _list[j]) = (_list[j], _list[i]);
        (_optMap[i], _optMap[j]) = (_optMap[j], _optMap[i]);
        Rebind(); _questions.SelectedIndex = j;
    }

    private void LoadSelected()
    {
        var i = _questions.SelectedIndex; if (i < 0) return;
        var q = _list[i];
        _qText.Text = q.Text; _points.Value = (decimal)q.Points;
        var ops = _optMap.GetValueOrDefault(i) ?? new List<OptionEntity>();
        while (ops.Count < 4) ops.Add(new OptionEntity { Text = "", SortOrder = ops.Count + 1 });
        for (int k = 0; k < 4; k++) { _opts[k].Text = ops[k].Text; _corr[k].Checked = ops[k].IsCorrect; }
        _optMap[i] = ops;
    }

    private bool SaveCurrentEditorToModel()
    {
        var i = _questions.SelectedIndex; if (i < 0) return true;
        if (string.IsNullOrWhiteSpace(_qText.Text)) { MessageBox.Show("Введите текст вопроса"); return false; }
        var texts = _opts.Select(x => x.Text.Trim()).ToArray();
        if (texts.Any(string.IsNullOrWhiteSpace)) { MessageBox.Show("Все 4 ответа обязательны"); return false; }
        var ci = Array.FindIndex(_corr, x => x.Checked);
        if (ci < 0) { MessageBox.Show("Выберите правильный ответ"); return false; }
        _list[i].Text = _qText.Text.Trim(); _list[i].Points = (double)_points.Value; _list[i].Type = QuestionType.SingleChoice;
        _optMap[i] = Enumerable.Range(0, 4).Select(k => new OptionEntity { Text = texts[k], IsCorrect = k == ci, SortOrder = k + 1 }).ToList();
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
            cmd.Parameters.AddWithValue("@t", _title.Text.Trim()); cmd.Parameters.AddWithValue("@d", _desc.Text.Trim()); cmd.Parameters.AddWithValue("@p", (int)_pass.Value); cmd.Parameters.AddWithValue("@id", _testId);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();

        EngineDb.ReplaceTestQuestions(_testId, _list, _optMap, _teacher.Id);
        MessageBox.Show("Сохранено");
    }
}
