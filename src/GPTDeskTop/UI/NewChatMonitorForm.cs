namespace GPTDeskTop.UI;

public sealed class NewChatMonitorForm : Form
{
    private readonly TextBox _initialMessageBox = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        AcceptsReturn = true,
        ScrollBars = ScrollBars.Vertical
    };

    private readonly TextBox _monitorReplyBox = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        AcceptsReturn = true,
        ScrollBars = ScrollBars.Vertical
    };

    private readonly Button _createButton = new() { Text = "Create Chat + Start Monitor", AutoSize = true };
    private readonly Button _cancelButton = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };

    public string InitialChatMessage => _initialMessageBox.Text.Trim();
    public string MonitorAutoReply => _monitorReplyBox.Text.Trim();

    public NewChatMonitorForm(string initialMessage, string monitorAutoReply)
    {
        Text = "New Chat + Monitor";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(720, 560);
        ClientSize = new Size(860, 650);

        _initialMessageBox.Text = initialMessage ?? string.Empty;
        _monitorReplyBox.Text = monitorAutoReply ?? string.Empty;

        BuildUi();
        ConfigureAccessibility();
        WireEvents();
        FluentTheme.Apply(this);
        FluentTheme.StyleButton(_createButton, primary: true);
        AcceptButton = _createButton;
        CancelButton = _cancelButton;
    }

    public static bool Edit(
        IWin32Window owner,
        string initialMessage,
        string monitorAutoReply,
        out string updatedInitialMessage,
        out string updatedMonitorAutoReply)
    {
        using var form = new NewChatMonitorForm(initialMessage, monitorAutoReply);
        if (form.ShowDialog(owner) != DialogResult.OK)
        {
            updatedInitialMessage = initialMessage;
            updatedMonitorAutoReply = monitorAutoReply;
            return false;
        }

        updatedInitialMessage = form.InitialChatMessage;
        updatedMonitorAutoReply = form.MonitorAutoReply;
        return true;
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18),
            BackColor = FluentTheme.Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = FluentTheme.Background
        };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(new Label
        {
            Text = "Create a fresh ChatGPT conversation and monitor it automatically",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Display", 15F, FontStyle.Bold),
            ForeColor = FluentTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 0, 0);
        header.Controls.Add(FluentTheme.CreateMutedLabel(
            "Step 1 sends the initial message. After ChatGPT exposes a stable conversation URL, Step 2 creates and starts a monitor that uses a separate auto-reply message."), 0, 1);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(CreateMessageSection(
            "1. Initial Chat Message",
            "Sent once to the brand-new ChatGPT conversation. GPTDeskTop verifies the user-message receipt before continuing.",
            _initialMessageBox), 0, 1);
        root.Controls.Add(CreateMessageSection(
            "2. Monitor Auto Reply",
            "Saved on the new monitor and sent after stable assistant responses according to the monitor delay/timer defaults.",
            _monitorReplyBox), 0, 2);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0),
            BackColor = FluentTheme.Background
        };
        actions.Controls.Add(_cancelButton);
        actions.Controls.Add(_createButton);
        root.Controls.Add(actions, 0, 3);

        Controls.Add(root);
    }

    private static Control CreateMessageSection(string title, string description, Control editor)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = FluentTheme.Surface,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(14),
            Margin = new Padding(0, 4, 0, 8)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = FluentTheme.Surface
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(FluentTheme.CreateSectionTitle(title), 0, 0);
        layout.Controls.Add(FluentTheme.CreateMutedLabel(description), 0, 1);
        editor.Margin = new Padding(0, 4, 0, 0);
        layout.Controls.Add(editor, 0, 2);
        card.Controls.Add(layout);
        return card;
    }

    private void ConfigureAccessibility()
    {
        AccessibleName = "New Chat and Monitor workflow";
        AccessibleDescription = "Creates a new ChatGPT conversation, sends an initial message, then starts a monitor with a separate automatic reply.";
        _initialMessageBox.AccessibleName = "Initial Chat Message";
        _initialMessageBox.AccessibleDescription = "Message sent once to the newly created ChatGPT conversation.";
        _initialMessageBox.TabIndex = 0;
        _monitorReplyBox.AccessibleName = "Monitor Auto Reply";
        _monitorReplyBox.AccessibleDescription = "Separate automatic reply used by the new monitor after assistant responses.";
        _monitorReplyBox.TabIndex = 1;
        _createButton.AccessibleName = "Create Chat and Start Monitor";
        _createButton.TabIndex = 2;
        _cancelButton.AccessibleName = "Cancel new chat monitor workflow";
        _cancelButton.TabIndex = 3;
    }

    private void WireEvents()
    {
        _createButton.Click += (_, _) => TrySaveAndClose();
        Shown += (_, _) =>
        {
            _initialMessageBox.Focus();
            _initialMessageBox.SelectionStart = _initialMessageBox.TextLength;
        };
    }

    private void TrySaveAndClose()
    {
        if (string.IsNullOrWhiteSpace(_initialMessageBox.Text))
        {
            _initialMessageBox.Focus();
            MessageBox.Show(this, "Initial Chat Message cannot be empty.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(_monitorReplyBox.Text))
        {
            _monitorReplyBox.Focus();
            MessageBox.Show(this, "Monitor Auto Reply cannot be empty.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
