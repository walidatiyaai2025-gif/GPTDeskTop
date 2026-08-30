using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.UI;

/// <summary>
/// Premium editable catalog for the exact development-plan message variants consumed by
/// DevelopmentTaskEngine. Mutations are persisted atomically before the UI reports success so
/// the runtime and the catalog always observe the same canonical message set.
/// </summary>
public sealed class DevelopmentMessageCatalogControl : UserControl
{
    private readonly string _catalogPath;
    private readonly ListBox _messages = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true, IntegralHeight = false };
    private readonly TextBox _editor = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        AcceptsReturn = true,
        ScrollBars = ScrollBars.Vertical,
        WordWrap = true,
        AutoSize = false,
        MinimumSize = new Size(0, 120),
        AccessibleName = "Development message editor",
        AccessibleDescription = "Multiline editor for the selected persisted development message."
    };
    private readonly Button _add = new() { Text = "Add", AutoSize = true };
    private readonly Button _update = new() { Text = "Update", AutoSize = true };
    private readonly Button _remove = new() { Text = "Remove", AutoSize = true };
    private readonly Button _up = new() { Text = "Move Up", AutoSize = true };
    private readonly Button _down = new() { Text = "Move Down", AutoSize = true };
    private readonly Button _import = new() { Text = "Import File", AutoSize = true };
    private readonly Button _paste = new() { Text = "Paste Plan", AutoSize = true };
    private readonly Button _export = new() { Text = "Export", AutoSize = true };
    private readonly Button _save = new() { Text = "Save Catalog", AutoSize = true };
    private readonly Label _count = new() { Dock = DockStyle.Fill, ForeColor = FluentTheme.Muted, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly Label _status = new() { Dock = DockStyle.Fill, ForeColor = FluentTheme.Muted, TextAlign = ContentAlignment.MiddleRight, AutoEllipsis = true };
    private List<string> _items = new();

    public DevelopmentMessageCatalogControl(string? catalogPath = null)
    {
        _catalogPath = catalogPath ?? Path.Combine(AppContext.BaseDirectory, "data", "development-task-messages.json");
        Name = "DevelopmentMessageCatalog";
        AccessibleName = "Development message catalog";
        Dock = DockStyle.Fill;
        BackColor = FluentTheme.Surface;
        Padding = new Padding(2);
        BuildUi();
        WireEvents();
        StyleControls();
        LoadCatalog();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            BackColor = FluentTheme.Surface,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        var listTitle = FluentTheme.CreateEyebrowLabel("Message variants");
        listTitle.Dock = DockStyle.Fill;
        listTitle.TextAlign = ContentAlignment.MiddleLeft;
        var editorTitle = FluentTheme.CreateEyebrowLabel("Selected message — multiline");
        editorTitle.Dock = DockStyle.Fill;
        editorTitle.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(listTitle, 0, 0);
        root.Controls.Add(editorTitle, 1, 0);

        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = FluentTheme.SurfaceAlt, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(6), Margin = new Padding(0, 0, 6, 0) };
        listHost.Controls.Add(_messages);
        var editorHost = new Panel { Dock = DockStyle.Fill, BackColor = FluentTheme.SurfaceAlt, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(6), Margin = new Padding(6, 0, 0, 0) };
        editorHost.Controls.Add(_editor);
        root.Controls.Add(listHost, 0, 1);
        root.Controls.Add(editorHost, 1, 1);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = FluentTheme.Surface, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoScroll = true, BackColor = FluentTheme.Surface, Padding = new Padding(0, 5, 0, 0), Margin = Padding.Empty };
        buttons.Controls.AddRange(new Control[] { _add, _update, _remove, _up, _down, _import, _paste, _export, _save });
        var statusHost = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = FluentTheme.Surface, Width = 245, Padding = new Padding(8, 4, 0, 0) };
        statusHost.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        statusHost.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        statusHost.Controls.Add(_count, 0, 0);
        statusHost.Controls.Add(_status, 0, 1);
        footer.Controls.Add(buttons, 0, 0);
        footer.Controls.Add(statusHost, 1, 0);
        root.Controls.Add(footer, 0, 2);
        root.SetColumnSpan(footer, 2);
        Controls.Add(root);
    }

    private void StyleControls()
    {
        _messages.BackColor = FluentTheme.SurfaceAlt;
        _messages.ForeColor = FluentTheme.Text;
        _messages.BorderStyle = BorderStyle.None;
        _messages.AccessibleName = "Persisted development message variants";
        _editor.BackColor = FluentTheme.SurfaceAlt;
        _editor.ForeColor = FluentTheme.Text;
        _editor.BorderStyle = BorderStyle.None;
        _editor.Font = new Font("Segoe UI Variable Text", 9.5F);
        FluentTheme.StyleButton(_add);
        FluentTheme.StyleButton(_update, primary: true);
        FluentTheme.StyleButton(_remove, danger: true);
        FluentTheme.StyleButton(_up);
        FluentTheme.StyleButton(_down);
        FluentTheme.StyleButton(_import);
        FluentTheme.StyleButton(_paste);
        FluentTheme.StyleButton(_export);
        FluentTheme.StyleButton(_save, primary: true);
    }

    private void WireEvents()
    {
        _messages.SelectedIndexChanged += (_, _) => LoadSelectedIntoEditor();
        _add.Click += (_, _) => AddMessage();
        _update.Click += (_, _) => UpdateMessage();
        _remove.Click += (_, _) => RemoveMessage();
        _up.Click += (_, _) => MoveMessage(-1);
        _down.Click += (_, _) => MoveMessage(1);
        _import.Click += (_, _) => ImportFile();
        _paste.Click += (_, _) => PastePlan();
        _export.Click += (_, _) => ExportPlan();
        _save.Click += (_, _) => SaveCatalog();
        _editor.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Enter)
            {
                UpdateMessage();
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.S)
            {
                SaveCatalog();
                e.SuppressKeyPress = true;
            }
        };
    }

    private void LoadCatalog()
    {
        try
        {
            if (!File.Exists(_catalogPath))
            {
                _items = new List<string>();
                RefreshList();
                _status.Text = "No catalog file found.";
                return;
            }

            _items = DevelopmentMessagePlanCodec.Parse(File.ReadAllText(_catalogPath)).ToList();
            RefreshList();
            _status.Text = "Loaded from runtime catalog.";
        }
        catch (Exception ex)
        {
            _status.Text = "Load failed.";
            MessageBox.Show(FindForm(), ex.Message, "Message Catalog", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshList(int selected = -1)
    {
        _messages.BeginUpdate();
        try
        {
            _messages.Items.Clear();
            for (var i = 0; i < _items.Count; i++)
            {
                var preview = _items[i].Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
                if (preview.Length > 92) preview = preview[..89] + "…";
                _messages.Items.Add($"{i + 1:00}  {preview}");
            }
        }
        finally { _messages.EndUpdate(); }

        _count.Text = $"{_items.Count} persisted message{(_items.Count == 1 ? string.Empty : "s")}";
        if (_items.Count > 0)
            _messages.SelectedIndex = Math.Clamp(selected < 0 ? 0 : selected, 0, _items.Count - 1);
        else
            _editor.Clear();
    }

    private void LoadSelectedIntoEditor()
    {
        var index = _messages.SelectedIndex;
        _editor.Text = index >= 0 && index < _items.Count ? _items[index] : string.Empty;
        _status.Text = index >= 0 ? $"Editing message {index + 1}." : "Select a message.";
    }

    private void AddMessage()
    {
        var text = _editor.Text.Trim();
        if (text.Length == 0) return;
        var previous = _items.ToList();
        _items.Add(text);
        PersistMutation(previous, _items.Count - 1, "Added and persisted for runtime.");
    }

    private void UpdateMessage()
    {
        var index = _messages.SelectedIndex;
        var text = _editor.Text.Trim();
        if (index < 0 || index >= _items.Count || text.Length == 0) return;
        var previous = _items.ToList();
        _items[index] = text;
        PersistMutation(previous, index, "Updated and persisted for runtime.");
    }

    private void RemoveMessage()
    {
        if (_items.Count <= 1)
        {
            MessageBox.Show(FindForm(), "At least one development message must remain.", "Message Catalog", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var index = _messages.SelectedIndex;
        if (index < 0) return;
        var previous = _items.ToList();
        _items.RemoveAt(index);
        PersistMutation(previous, Math.Max(0, index - 1), "Removed and persisted for runtime.");
    }

    private void MoveMessage(int delta)
    {
        var index = _messages.SelectedIndex;
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _items.Count) return;
        var previous = _items.ToList();
        (_items[index], _items[target]) = (_items[target], _items[index]);
        PersistMutation(previous, target, "Order persisted for runtime.");
    }

    private void ImportFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Import Development Messages Plan",
            Filter = "Development plan (*.json;*.txt)|*.json;*.txt|JSON (*.json)|*.json|Text (*.txt)|*.txt|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        ImportPlanText(File.ReadAllText(dialog.FileName), $"Imported from {Path.GetFileName(dialog.FileName)}.");
    }

    private void PastePlan()
    {
        if (!Clipboard.ContainsText())
        {
            MessageBox.Show(FindForm(), "The clipboard does not contain a development plan.", "Paste Plan", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ImportPlanText(Clipboard.GetText(), "Clipboard plan imported and persisted.");
    }

    private void ImportPlanText(string text, string successStatus)
    {
        var parsed = DevelopmentMessagePlanCodec.Parse(text).ToList();
        if (parsed.Count == 0)
        {
            MessageBox.Show(FindForm(), "No non-empty development messages were found.", "Import Plan", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_items.Count > 0 && MessageBox.Show(
                FindForm(),
                $"Replace the current {_items.Count} message(s) with {parsed.Count} imported message(s)?",
                "Replace Development Plan",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        var previous = _items.ToList();
        _items = parsed;
        PersistMutation(previous, 0, successStatus);
    }

    private void ExportPlan()
    {
        if (_items.Count == 0) return;
        using var dialog = new SaveFileDialog
        {
            Title = "Export Development Messages Plan",
            Filter = "JSON development plan (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = "json",
            AddExtension = true,
            FileName = "development-messages-plan.json"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        File.WriteAllText(dialog.FileName, DevelopmentMessagePlanCodec.Serialize(_items));
        _status.Text = $"Exported {DateTime.Now:t}.";
    }

    private void SaveCatalog()
    {
        try
        {
            PersistCatalogCore();
            RefreshList(_messages.SelectedIndex);
            _status.Text = $"Saved {DateTime.Now:t}.";
        }
        catch (Exception ex)
        {
            _status.Text = "Save failed.";
            MessageBox.Show(FindForm(), ex.Message, "Message Catalog", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PersistMutation(List<string> previous, int selected, string successStatus)
    {
        try
        {
            PersistCatalogCore();
            RefreshList(selected);
            _status.Text = successStatus;
        }
        catch (Exception ex)
        {
            _items = previous;
            RefreshList(Math.Min(Math.Max(0, selected), Math.Max(0, _items.Count - 1)));
            _status.Text = "Save failed — change rolled back.";
            MessageBox.Show(FindForm(), ex.Message, "Message Catalog", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PersistCatalogCore()
    {
        if (_items.Count == 0) throw new InvalidOperationException("The development message catalog cannot be empty.");
        if (_items.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException("Empty messages are not allowed.");

        var directory = Path.GetDirectoryName(_catalogPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var temp = _catalogPath + ".tmp";
        File.WriteAllText(temp, DevelopmentMessagePlanCodec.Serialize(_items));
        File.Move(temp, _catalogPath, true);
    }
}
