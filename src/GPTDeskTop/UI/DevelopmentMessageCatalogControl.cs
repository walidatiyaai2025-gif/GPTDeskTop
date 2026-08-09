using System.Text.Json;

namespace GPTDeskTop.UI;

/// <summary>
/// Editable catalog for development-plan message variants. Changes are persisted
/// atomically to the same catalog consumed by DevelopmentTaskEngine.
/// </summary>
public sealed class DevelopmentMessageCatalogControl : UserControl
{
    private readonly string _catalogPath;
    private readonly ListBox _messages = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private readonly TextBox _editor = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly Button _add = new() { Text = "Add", AutoSize = true };
    private readonly Button _update = new() { Text = "Update", AutoSize = true };
    private readonly Button _remove = new() { Text = "Remove", AutoSize = true };
    private readonly Button _up = new() { Text = "Move Up", AutoSize = true };
    private readonly Button _down = new() { Text = "Move Down", AutoSize = true };
    private readonly Button _save = new() { Text = "Save", AutoSize = true };
    private readonly Label _count = new() { AutoSize = true };
    private List<string> _items = new();

    public DevelopmentMessageCatalogControl(string? catalogPath = null)
    {
        _catalogPath = catalogPath ?? Path.Combine(AppContext.BaseDirectory, "data", "development-task-messages.json");
        Dock = DockStyle.Fill;
        Padding = new Padding(10);
        BuildUi();
        WireEvents();
        LoadCatalog();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        root.Controls.Add(new Label { Text = "Development Messages", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        root.Controls.Add(_count, 1, 0);
        root.Controls.Add(_messages, 0, 1);
        root.Controls.Add(_editor, 1, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.AddRange(new Control[] { _add, _update, _remove, _up, _down, _save });
        root.Controls.Add(buttons, 0, 2);
        root.SetColumnSpan(buttons, 2);
        Controls.Add(root);
    }

    private void WireEvents()
    {
        _messages.SelectedIndexChanged += (_, _) => LoadSelectedIntoEditor();
        _add.Click += (_, _) => AddMessage();
        _update.Click += (_, _) => UpdateMessage();
        _remove.Click += (_, _) => RemoveMessage();
        _up.Click += (_, _) => Move(-1);
        _down.Click += (_, _) => Move(1);
        _save.Click += (_, _) => SaveCatalog();
    }

    private void LoadCatalog()
    {
        try
        {
            if (!File.Exists(_catalogPath))
            {
                _items = new List<string>();
                RefreshList();
                return;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(_catalogPath));
            _items = document.RootElement.TryGetProperty("messages", out var messages)
                ? messages.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList()
                : new List<string>();
            RefreshList();
        }
        catch (Exception ex)
        {
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
                _messages.Items.Add($"{i + 1}. {_items[i]}");
        }
        finally { _messages.EndUpdate(); }

        _count.Text = $"{_items.Count} messages — add as many variants as needed";
        if (_items.Count > 0)
            _messages.SelectedIndex = Math.Clamp(selected < 0 ? 0 : selected, 0, _items.Count - 1);
        else
            _editor.Clear();
    }

    private void LoadSelectedIntoEditor()
    {
        var index = _messages.SelectedIndex;
        _editor.Text = index >= 0 && index < _items.Count ? _items[index] : string.Empty;
    }

    private void AddMessage()
    {
        var text = _editor.Text.Trim();
        if (text.Length == 0) return;
        _items.Add(text);
        RefreshList(_items.Count - 1);
    }

    private void UpdateMessage()
    {
        var index = _messages.SelectedIndex;
        var text = _editor.Text.Trim();
        if (index < 0 || index >= _items.Count || text.Length == 0) return;
        _items[index] = text;
        RefreshList(index);
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
        _items.RemoveAt(index);
        RefreshList(Math.Max(0, index - 1));
    }

    private void Move(int delta)
    {
        var index = _messages.SelectedIndex;
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _items.Count) return;
        (_items[index], _items[target]) = (_items[target], _items[index]);
        RefreshList(target);
    }

    private void SaveCatalog()
    {
        try
        {
            if (_items.Count == 0) throw new InvalidOperationException("The development message catalog cannot be empty.");
            if (_items.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException("Empty messages are not allowed.");

            var directory = Path.GetDirectoryName(_catalogPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            var temp = _catalogPath + ".tmp";
            var json = JsonSerializer.Serialize(new { messages = _items }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(temp, json);
            File.Move(temp, _catalogPath, true);
            MessageBox.Show(FindForm(), "Message catalog saved.", "Message Catalog", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), ex.Message, "Message Catalog", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
