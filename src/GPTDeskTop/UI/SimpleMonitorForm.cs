using System.Text.Json;
using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

public sealed class SimpleMonitorForm : Form
{
    private const string ProfileSetting = "SimpleMonitor.ProfileKey";
    private const string ConversationSetting = "SimpleMonitor.ConversationUrl";
    private const string MessagesSetting = "SimpleMonitor.MessagesJson";
    private const string DelaySetting = "SimpleMonitor.DelaySeconds";
    private const string PlanSetting = "SimpleMonitor.MessagePlanJson";

    private readonly LocalDatabase _database;
    private readonly SimpleMonitorRunner _runner;
    private SimpleMonitorProfileSession? _session;
    private SimpleMonitorMessagePlan? _loadedPlan;

    private readonly RadioButton _currentModeRadio = new() { Text = "Current GPTDeskTop", AutoSize = true };
    private readonly RadioButton _monitorModeRadio = new() { Text = "Monitor Only — Same Chat", AutoSize = true, Checked = true };
    private readonly ComboBox _profileCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Button _connectButton = new() { Text = "Connect Profile", AutoSize = true };
    private readonly Button _refreshChatsButton = new() { Text = "Refresh Chats", AutoSize = true };
    private readonly ComboBox _conversationCombo = new() { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill };

    private readonly ListBox _messagesList = new()
    {
        Dock = DockStyle.Fill,
        IntegralHeight = false,
        DrawMode = DrawMode.OwnerDrawFixed,
        ItemHeight = 24,
        HorizontalScrollbar = true
    };
    private readonly TextBox _messageEditor = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        AcceptsReturn = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = true,
        MinimumSize = new Size(0, 100)
    };
    private readonly Button _addMessageButton = new() { Text = "Add", AutoSize = true };
    private readonly Button _updateMessageButton = new() { Text = "Update", AutoSize = true };
    private readonly Button _removeMessageButton = new() { Text = "Delete Selected", AutoSize = true };
    private readonly Button _moveUpButton = new() { Text = "Move Up", AutoSize = true };
    private readonly Button _moveDownButton = new() { Text = "Move Down", AutoSize = true };

    private readonly Button _loadPlanButton = new() { Text = "Load JSON Plan", AutoSize = true };
    private readonly Button _downloadSampleButton = new() { Text = "Download Sample JSON", AutoSize = true };
    private readonly Button _copyPromptButton = new() { Text = "Copy ChatGPT Prompt", AutoSize = true };
    private readonly Button _previewPlanButton = new() { Text = "Preview / Validate", AutoSize = true, Enabled = false };
    private readonly Button _clearPlanButton = new() { Text = "Clear JSON Plan", AutoSize = true, Enabled = false };
    private readonly Label _planSummaryLabel = new()
    {
        Dock = DockStyle.Fill,
        Text = "Manual message mode — or load a JSON plan generated from the sample.",
        ForeColor = FluentTheme.Muted,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
    };

    private readonly NumericUpDown _delaySeconds = new() { Minimum = 15, Maximum = 3600, Value = 15, Width = 90 };
    private readonly Button _startButton = new() { Text = "Start Monitor", AutoSize = true };
    private readonly Button _stopButton = new() { Text = "Stop", AutoSize = true, Enabled = false };
    private readonly Label _statusLabel = new()
    {
        Dock = DockStyle.Fill,
        Text = "Ready. Select a Chrome profile and the exact ChatGPT conversation.",
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Label _cycleLabel = new()
    {
        Dock = DockStyle.Fill,
        Text = "Message: —",
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleRight
    };

    private readonly Label _inspectorState = InspectorValue("State: Idle");
    private readonly Label _inspectorMessage = InspectorValue("Current: —");
    private readonly Label _inspectorProgress = InspectorValue("Sent: 0  •  Pending: 0");
    private readonly Label _inspectorRetries = InspectorValue("CDP retries: 0");
    private readonly Label _inspectorCdp = InspectorValue("Last CDP: Idle");
    private readonly Label _inspectorError = new()
    {
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        ForeColor = FluentTheme.Muted,
        Text = "Last error: —",
        TextAlign = ContentAlignment.MiddleLeft
    };

    private readonly TableLayoutPanel _root = new();
    private readonly FlowLayoutPanel _planButtons = new();
    private readonly SplitContainer _messageSplit = new();
    private bool _closing;

    public SimpleMonitorForm(LocalDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _runner = new SimpleMonitorRunner(_database);

        Text = "GPTDeskTop — Monitor Only";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(860, 640);
        ClientSize = new Size(1280, 900);

        BuildUi();
        WireEvents();
        ConfigureAccessibility();
        FluentTheme.Apply(this);
        FluentTheme.StyleButton(_connectButton, primary: true);
        FluentTheme.StyleButton(_startButton, primary: true);
        FluentTheme.StyleButton(_loadPlanButton, primary: true);
        FluentTheme.StyleButton(_removeMessageButton, danger: true);

        _runner.StatusChanged += OnRunnerStatusChanged;
        _runner.MessageChanged += OnRunnerMessageChanged;
        _runner.MessageSent += OnRunnerMessageSent;
        _runner.InspectorChanged += OnInspectorChanged;

        Resize += (_, _) => ApplyResponsiveLayout();
        Shown += async (_, _) =>
        {
            ApplyResponsiveLayout();
            await LoadSavedStateAsync();
        };
        FormClosed += async (_, _) =>
        {
            _closing = true;
            await _runner.StopAsync();
            if (_session is not null) await _session.DisposeAsync();
            await _runner.DisposeAsync();
        };
    }

    private void BuildUi()
    {
        _root.Dock = DockStyle.Fill;
        _root.ColumnCount = 1;
        _root.RowCount = 6;
        _root.Padding = new Padding(14);
        _root.BackColor = FluentTheme.Background;
        _root.AutoScroll = true;
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 154));
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        _root.Controls.Add(BuildHeader(), 0, 0);
        _root.Controls.Add(BuildTargetCard(), 0, 1);
        _root.Controls.Add(BuildMessagesCard(), 0, 2);
        _root.Controls.Add(BuildRuntimeCard(), 0, 3);
        _root.Controls.Add(BuildInspectorCard(), 0, 4);
        _root.Controls.Add(BuildStatusBar(), 0, 5);
        Controls.Add(_root);
    }

    private Control BuildHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = FluentTheme.Surface,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(14, 8, 14, 8),
            Margin = new Padding(0, 0, 0, 8)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = FluentTheme.Surface };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));

        var title = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = FluentTheme.Surface };
        title.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        title.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        title.Controls.Add(new Label
        {
            Text = "Monitor Only",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Display", 17F, FontStyle.Bold),
            TextAlign = ContentAlignment.BottomLeft
        }, 0, 0);
        title.Controls.Add(new Label
        {
            Text = "Durable message plan • sent-state resume • same ChatGPT conversation only",
            Dock = DockStyle.Fill,
            ForeColor = FluentTheme.Muted,
            TextAlign = ContentAlignment.TopLeft,
            AutoEllipsis = true
        }, 0, 1);

        var modes = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true,
            BackColor = FluentTheme.Surface,
            Padding = new Padding(0, 12, 0, 0)
        };
        modes.Controls.Add(_monitorModeRadio);
        modes.Controls.Add(_currentModeRadio);
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(modes, 1, 0);
        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildTargetCard()
    {
        var group = new GroupBox
        {
            Text = "1. Chrome profile and same-chat target",
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            Margin = new Padding(0, 0, 0, 8)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(CreateRowLabel("Chrome profile"), 0, 0);
        var profileRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        profileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        profileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        profileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        profileRow.Controls.Add(_profileCombo, 0, 0);
        profileRow.Controls.Add(_connectButton, 1, 0);
        profileRow.Controls.Add(_refreshChatsButton, 2, 0);
        layout.Controls.Add(profileRow, 1, 0);

        layout.Controls.Add(CreateRowLabel("ChatGPT conversation"), 0, 1);
        layout.Controls.Add(_conversationCombo, 1, 1);

        var hardRule = new Label
        {
            Text = "LOCKED RULE: Same Chat = ON   •   New Chat = OFF   •   Rotation = OFF   •   fallback to another chat = OFF",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold),
            ForeColor = FluentTheme.Success,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        layout.Controls.Add(hardRule, 0, 2);
        layout.SetColumnSpan(hardRule, 2);

        var note = new Label
        {
            Text = "GPTDeskTop keeps an automation-safe persistent Chrome session for the selected profile. Confirmed RUN ONCE messages are checkpointed before any later browser read, so a restart resumes from the next unsent message.",
            Dock = DockStyle.Fill,
            ForeColor = FluentTheme.Muted,
            AutoEllipsis = true
        };
        layout.Controls.Add(note, 0, 3);
        layout.SetColumnSpan(note, 2);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildMessagesCard()
    {
        var group = new GroupBox
        {
            Text = "2. Message sequence / JSON Message Plan",
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            Margin = new Padding(0, 0, 0, 8)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _planButtons.Dock = DockStyle.Fill;
        _planButtons.WrapContents = true;
        _planButtons.AutoSize = true;
        _planButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _planButtons.Padding = new Padding(0, 2, 0, 3);
        _planButtons.Controls.Add(_loadPlanButton);
        _planButtons.Controls.Add(_downloadSampleButton);
        _planButtons.Controls.Add(_copyPromptButton);
        _planButtons.Controls.Add(_previewPlanButton);
        _planButtons.Controls.Add(_clearPlanButton);
        layout.Controls.Add(_planButtons, 0, 0);
        layout.Controls.Add(_planSummaryLabel, 0, 1);

        _messageSplit.Dock = DockStyle.Fill;
        _messageSplit.Orientation = Orientation.Vertical;
        _messageSplit.Panel1MinSize = 0;
        _messageSplit.Panel2MinSize = 0;

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.Controls.Add(_messagesList, 0, 0);
        var listButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoSize = true, Padding = new Padding(0, 5, 0, 0) };
        listButtons.Controls.Add(_moveUpButton);
        listButtons.Controls.Add(_moveDownButton);
        left.Controls.Add(listButtons, 0, 1);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.Controls.Add(_messageEditor, 0, 0);
        var editButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoSize = true, Padding = new Padding(0, 5, 0, 0) };
        editButtons.Controls.Add(_addMessageButton);
        editButtons.Controls.Add(_updateMessageButton);
        editButtons.Controls.Add(_removeMessageButton);
        right.Controls.Add(editButtons, 0, 1);

        _messageSplit.Panel1.Controls.Add(left);
        _messageSplit.Panel2.Controls.Add(right);
        layout.Controls.Add(_messageSplit, 0, 2);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildRuntimeCard()
    {
        var group = new GroupBox
        {
            Text = "3. Runtime",
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            Margin = new Padding(0, 0, 0, 8)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoSize = true };
        controls.Controls.Add(CreateInlineLabel("Default post-response delay"));
        controls.Controls.Add(_delaySeconds);
        controls.Controls.Add(CreateInlineMuted("seconds — minimum 15. JSON plans may override this per message."));
        controls.Controls.Add(_startButton);
        controls.Controls.Add(_stopButton);
        layout.Controls.Add(controls, 0, 0);

        var logic = new Label
        {
            Text = "Send → checkpoint confirmed delivery → wait for response completion → safety delay → revalidate same chat → next pending message. Passive Runtime.evaluate timeouts are retried safely without resending.",
            Dock = DockStyle.Fill,
            ForeColor = FluentTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        layout.Controls.Add(logic, 0, 1);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildInspectorCard()
    {
        var group = new GroupBox
        {
            Text = "4. Runtime Inspector — live",
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            Margin = new Padding(0, 0, 0, 8)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        var metrics = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true };
        metrics.Controls.Add(_inspectorState);
        metrics.Controls.Add(_inspectorMessage);
        metrics.Controls.Add(_inspectorProgress);
        metrics.Controls.Add(_inspectorRetries);
        metrics.Controls.Add(_inspectorCdp);
        layout.Controls.Add(metrics, 0, 0);
        layout.Controls.Add(_inspectorError, 0, 1);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildStatusBar()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = FluentTheme.Surface, Padding = new Padding(10, 5, 10, 5) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = FluentTheme.Surface };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        layout.Controls.Add(_statusLabel, 0, 0);
        layout.Controls.Add(_cycleLabel, 1, 0);
        panel.Controls.Add(layout);
        return panel;
    }

    private void ApplyResponsiveLayout()
    {
        if (IsDisposed || Disposing) return;

        var compact = ClientSize.Width < 1080;
        _root.Padding = compact ? new Padding(8) : new Padding(14);
        var targetOrientation = compact ? Orientation.Horizontal : Orientation.Vertical;

        try
        {
            // Orientation changes validate SplitterDistance against the new axis immediately.
            // Temporarily remove panel minimums and reset the distance before changing axis so
            // small windows / high DPI can never violate Panel1MinSize/Panel2MinSize constraints.
            _messageSplit.Panel1MinSize = 0;
            _messageSplit.Panel2MinSize = 0;
            if (_messageSplit.SplitterDistance != 0)
                _messageSplit.SplitterDistance = 0;
            if (_messageSplit.Orientation != targetOrientation)
                _messageSplit.Orientation = targetOrientation;

            var total = targetOrientation == Orientation.Vertical
                ? _messageSplit.ClientSize.Width
                : _messageSplit.ClientSize.Height;
            var available = total - _messageSplit.SplitterWidth;
            if (available <= 2)
            {
                _planSummaryLabel.AutoEllipsis = true;
                return;
            }

            var desiredDistance = compact
                ? available / 2
                : (int)Math.Round(available * 0.42d);
            desiredDistance = Math.Clamp(desiredDistance, 1, available - 1);
            _messageSplit.SplitterDistance = desiredDistance;

            var panel2Space = Math.Max(0, available - desiredDistance);
            _messageSplit.Panel1MinSize = Math.Min(compact ? 120 : 260, desiredDistance);
            _messageSplit.Panel2MinSize = Math.Min(compact ? 120 : 300, panel2Space);
        }
        catch (InvalidOperationException)
        {
            ResetSplitMinimums();
        }
        catch (ArgumentOutOfRangeException)
        {
            ResetSplitMinimums();
        }

        _planSummaryLabel.AutoEllipsis = true;
    }

    private void ResetSplitMinimums()
    {
        try
        {
            _messageSplit.Panel1MinSize = 0;
            _messageSplit.Panel2MinSize = 0;
        }
        catch (InvalidOperationException) { }
        catch (ArgumentOutOfRangeException) { }
    }

    private void WireEvents()
    {
        _currentModeRadio.CheckedChanged += (_, _) =>
        {
            if (_currentModeRadio.Checked && !_closing) Close();
        };
        _connectButton.Click += async (_, _) => await ConnectSelectedProfileAsync(refreshConversations: true);
        _refreshChatsButton.Click += async (_, _) => await RefreshConversationsAsync();
        _messagesList.SelectedIndexChanged += (_, _) =>
        {
            if (_messagesList.SelectedItem is string selected)
                _messageEditor.Text = selected;
            else if (_messagesList.SelectedItem is PlanMessageChoice planChoice)
                _messageEditor.Text = planChoice.Step.Text;
            UpdateMessageActionStates();
        };
        _messagesList.DrawItem += DrawMessageItem;
        _messagesList.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Delete) return;
            e.SuppressKeyPress = true;
            await RemoveMessageAsync();
        };
        _messageEditor.TextChanged += (_, _) => UpdateMessageActionStates();
        _addMessageButton.Click += async (_, _) => await AddMessageAsync();
        _updateMessageButton.Click += async (_, _) => await UpdateMessageAsync();
        _removeMessageButton.Click += async (_, _) => await RemoveMessageAsync();
        _moveUpButton.Click += async (_, _) => await MoveMessageAsync(-1);
        _moveDownButton.Click += async (_, _) => await MoveMessageAsync(1);
        _loadPlanButton.Click += async (_, _) => await LoadJsonPlanAsync();
        _downloadSampleButton.Click += async (_, _) => await SaveSampleJsonAsync();
        _copyPromptButton.Click += (_, _) => CopyChatGptPrompt();
        _previewPlanButton.Click += (_, _) => PreviewPlan();
        _clearPlanButton.Click += async (_, _) => await ClearJsonPlanAsync();
        _startButton.Click += async (_, _) => await StartMonitorAsync();
        _stopButton.Click += async (_, _) => await StopMonitorAsync();
        UpdateMessageActionStates();
    }

    private void DrawMessageItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _messagesList.Items.Count) return;
        e.DrawBackground();
        var item = _messagesList.Items[e.Index];
        var sent = item is PlanMessageChoice choice && choice.Step.Sent;
        var disabled = item is PlanMessageChoice disabledChoice && !disabledChoice.Step.Enabled;
        var text = item?.ToString() ?? string.Empty;
        using var brush = new SolidBrush(sent ? FluentTheme.Success : disabled ? FluentTheme.Muted : e.ForeColor);
        using var font = sent ? new Font(e.Font, FontStyle.Bold) : new Font(e.Font, e.Font.Style);
        e.Graphics.DrawString(text, font, brush, e.Bounds.Left + 4, e.Bounds.Top + 3);
        e.DrawFocusRectangle();
    }

    private void ConfigureAccessibility()
    {
        AccessibleName = "GPTDeskTop Monitor Only";
        AccessibleDescription = "Standalone same-chat monitor for stored messages and JSON message plans with durable sent checkpoints and Runtime Inspector.";
        _profileCombo.AccessibleName = "Chrome profile";
        _conversationCombo.AccessibleName = "ChatGPT conversation URL";
        _messagesList.AccessibleName = "Stored message sequence";
        _messageEditor.AccessibleName = "Stored message editor";
        _removeMessageButton.AccessibleName = "Delete selected stored or JSON plan message";
        _loadPlanButton.AccessibleName = "Load JSON message plan";
        _downloadSampleButton.AccessibleName = "Download sample JSON message plan";
        _copyPromptButton.AccessibleName = "Copy ChatGPT JSON plan prompt";
        _previewPlanButton.AccessibleName = "Preview and validate JSON message plan";
        _delaySeconds.AccessibleName = "Default post response safety delay seconds";
        _startButton.AccessibleName = "Start same chat monitor";
        _stopButton.AccessibleName = "Stop same chat monitor";
    }

    private async Task LoadSavedStateAsync()
    {
        var profiles = ChromeProfileCatalog.Discover();
        _profileCombo.Items.Clear();
        foreach (var profile in profiles) _profileCombo.Items.Add(profile);

        var savedProfileKey = await _database.GetSettingAsync(ProfileSetting);
        var selectedIndex = profiles
            .Select((profile, index) => new { profile, index })
            .FirstOrDefault(item => string.Equals(item.profile.Key, savedProfileKey, StringComparison.OrdinalIgnoreCase))?.index ?? 0;
        if (_profileCombo.Items.Count > 0) _profileCombo.SelectedIndex = Math.Clamp(selectedIndex, 0, _profileCombo.Items.Count - 1);

        _conversationCombo.Text = await _database.GetSettingAsync(ConversationSetting) ?? string.Empty;
        var delayRaw = await _database.GetSettingAsync(DelaySetting);
        if (int.TryParse(delayRaw, out var delay)) _delaySeconds.Value = Math.Clamp(delay, 15, 3600);

        var savedPlanJson = await _database.GetSettingAsync(PlanSetting);
        if (!string.IsNullOrWhiteSpace(savedPlanJson))
        {
            try
            {
                ApplyJsonPlan(SimpleMonitorMessagePlanService.ParseAndValidate(savedPlanJson));
            }
            catch (InvalidDataException ex)
            {
                _statusLabel.Text = $"Saved JSON plan was invalid and was not loaded: {ex.Message}";
                await _database.SetSettingAsync(PlanSetting, string.Empty);
            }
        }

        if (_loadedPlan is null)
        {
            var savedMessagesJson = await _database.GetSettingAsync(MessagesSetting);
            var messages = DeserializeMessages(savedMessagesJson);
            if (savedMessagesJson is null && messages.Count == 0) messages.Add("كمل");
            ShowManualMessages(messages);
            _statusLabel.Text = "Ready. Select Connect Profile, then choose the same ChatGPT conversation to monitor.";
        }
        else
        {
            var sent = _loadedPlan.Messages.Count(step => step.Enabled && step.Sent);
            _statusLabel.Text = sent == 0
                ? "Saved JSON plan loaded. Ready to start."
                : $"Saved JSON plan resumed with {sent} confirmed message(s) already checkpointed; they will not repeat in RUN ONCE mode.";
        }
        UpdateMessageActionStates();
    }

    private async Task ConnectSelectedProfileAsync(bool refreshConversations)
    {
        if (_profileCombo.SelectedItem is not ChromeProfileInfo profile)
        {
            ShowValidation("Select a Chrome profile first.");
            return;
        }

        SetBusy(true, "Connecting to the selected Chrome profile...");
        try
        {
            if (_session is null || !string.Equals(_session.Profile.Key, profile.Key, StringComparison.OrdinalIgnoreCase))
            {
                if (_runner.IsRunning) await _runner.StopAsync();
                if (_session is not null) await _session.DisposeAsync();
                _session = new SimpleMonitorProfileSession(profile);
            }

            await _session.EnsureConnectedAsync();
            await _database.SetSettingAsync(ProfileSetting, profile.Key);
            _statusLabel.Text = $"Connected: {profile.DisplayLabel}.";
            if (refreshConversations) await RefreshConversationsAsync();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Connection failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Chrome profile", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { SetBusy(false, null); }
    }

    private async Task RefreshConversationsAsync()
    {
        if (_session is null)
        {
            await ConnectSelectedProfileAsync(refreshConversations: false);
            if (_session is null) return;
        }

        SetBusy(true, "Refreshing ChatGPT conversations in the selected profile...");
        try
        {
            var preservedUrl = GetConversationUrl();
            var tabs = await _session.GetConversationTabsAsync();
            _conversationCombo.Items.Clear();
            foreach (var tab in tabs) _conversationCombo.Items.Add(new ConversationChoice(tab));

            var matching = tabs.FirstOrDefault(tab => SimpleMonitorProfileSession.SameConversation(tab.Url, preservedUrl));
            if (matching is not null)
                _conversationCombo.SelectedItem = _conversationCombo.Items.Cast<ConversationChoice>().First(item => item.Tab.Id == matching.Id);
            else
                _conversationCombo.Text = preservedUrl;

            _statusLabel.Text = tabs.Count == 0
                ? "No stable ChatGPT conversations are open yet. Sign in/open the target chat, then Refresh Chats."
                : $"Found {tabs.Count} stable ChatGPT conversation(s) in {_session.Profile.DisplayLabel}.";
        }
        catch (Exception ex) { _statusLabel.Text = $"Refresh failed: {ex.Message}"; }
        finally { SetBusy(false, null); }
    }

    private async Task LoadJsonPlanAsync()
    {
        if (_runner.IsRunning) return;
        using var dialog = new OpenFileDialog
        {
            Title = "Load GPTDeskTop JSON Message Plan",
            Filter = "JSON message plan (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var plan = SimpleMonitorMessagePlanService.ParseAndValidate(await File.ReadAllTextAsync(dialog.FileName));
            ApplyJsonPlan(plan);
            await _database.SetSettingAsync(PlanSetting, SimpleMonitorMessagePlanService.Serialize(plan));
            await _database.SetSettingAsync(DelaySetting, plan.DefaultDelaySeconds.ToString());
            _statusLabel.Text = $"JSON plan loaded and validated: {plan.Name}. Select any row and use Delete Selected if it should not run.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, ex.Message, "Invalid JSON Message Plan", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _statusLabel.Text = $"JSON plan rejected: {ex.Message}";
        }
    }

    private async Task SaveSampleJsonAsync()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Save GPTDeskTop Sample JSON Message Plan",
            Filter = "JSON message plan (*.json)|*.json",
            FileName = "gptdesktop-message-plan-sample.json",
            AddExtension = true,
            DefaultExt = "json",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            await File.WriteAllTextAsync(dialog.FileName, SimpleMonitorMessagePlanService.CreateSampleJson());
            _statusLabel.Text = $"Sample JSON saved: {dialog.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Save Sample JSON", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void CopyChatGptPrompt()
    {
        try
        {
            Clipboard.SetText(SimpleMonitorMessagePlanService.CreateChatGptPrompt());
            _statusLabel.Text = "ChatGPT JSON-generation prompt copied. Attach the sample JSON and paste the prompt.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Clipboard", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void PreviewPlan()
    {
        if (_loadedPlan is null)
        {
            ShowValidation("Load a JSON message plan first.");
            return;
        }
        MessageBox.Show(this, SimpleMonitorMessagePlanService.BuildPreview(_loadedPlan), "JSON Message Plan — Validated Preview", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task ClearJsonPlanAsync()
    {
        if (_loadedPlan is null || _runner.IsRunning) return;
        var pendingManual = _loadedPlan.Messages
            .Where(step => step.Enabled && !step.Sent)
            .Select(step => step.Text)
            .ToList();
        var defaultDelay = _loadedPlan.DefaultDelaySeconds;
        _loadedPlan = null;
        _delaySeconds.Value = Math.Clamp(defaultDelay, 15, 3600);
        ShowManualMessages(pendingManual);
        await PersistManualMessagesAsync();
        _statusLabel.Text = "JSON plan cleared. Only unsent enabled messages were retained as manual messages.";
    }

    private void ApplyJsonPlan(SimpleMonitorMessagePlan plan)
    {
        _loadedPlan = plan;
        _delaySeconds.Value = Math.Clamp(plan.DefaultDelaySeconds, 15, 3600);
        _messagesList.Items.Clear();
        for (var index = 0; index < plan.Messages.Count; index++)
            _messagesList.Items.Add(new PlanMessageChoice(index + 1, plan.Messages[index], plan.DefaultDelaySeconds));
        if (_messagesList.Items.Count > 0) _messagesList.SelectedIndex = 0;
        UpdatePlanSummary();
        _previewPlanButton.Enabled = true;
        _clearPlanButton.Enabled = true;
        _messageEditor.ReadOnly = true;
        _messagesList.Refresh();
        UpdateMessageActionStates();
    }

    private void UpdatePlanSummary()
    {
        if (_loadedPlan is null) return;
        var enabled = _loadedPlan.Messages.Count(step => step.Enabled);
        var sent = _loadedPlan.Messages.Count(step => step.Enabled && step.Sent);
        var pending = _loadedPlan.Loop ? enabled : _loadedPlan.Messages.Count(step => step.Enabled && !step.Sent);
        _planSummaryLabel.Text = $"JSON: {_loadedPlan.Name} • {(_loadedPlan.Loop ? "LOOP" : "RUN ONCE / RESUME")} • {enabled} enabled • {sent} sent • {pending} pending • default {_loadedPlan.DefaultDelaySeconds}s";
    }

    private void ShowManualMessages(IReadOnlyList<string> messages)
    {
        _loadedPlan = null;
        _messagesList.Items.Clear();
        foreach (var message in messages.Where(message => !string.IsNullOrWhiteSpace(message)))
            _messagesList.Items.Add(message);
        if (_messagesList.Items.Count > 0) _messagesList.SelectedIndex = 0;
        else _messageEditor.Clear();
        _planSummaryLabel.Text = "Manual message mode — or load a JSON plan generated from the sample.";
        _previewPlanButton.Enabled = false;
        _clearPlanButton.Enabled = false;
        _messageEditor.ReadOnly = _runner.IsRunning;
        UpdateMessageActionStates();
    }

    private async Task StartMonitorAsync()
    {
        var manualMessages = _loadedPlan is null
            ? _messagesList.Items.OfType<string>().Where(message => !string.IsNullOrWhiteSpace(message)).ToArray()
            : [];
        if (_loadedPlan is null && manualMessages.Length == 0)
        {
            ShowValidation("Add at least one stored message or load a valid JSON message plan.");
            return;
        }
        if (_loadedPlan is { Loop: false } plan && !plan.Messages.Any(step => step.Enabled && !step.Sent))
        {
            _statusLabel.Text = "Plan already complete. Every enabled message is checkpointed SENT; nothing will be resent.";
            return;
        }

        var url = GetConversationUrl();
        if (!SimpleMonitorProfileSession.TryGetConversationId(url, out _))
        {
            ShowValidation("Select or paste a stable ChatGPT conversation URL containing /c/{conversation-id}.");
            return;
        }
        if (_profileCombo.SelectedItem is not ChromeProfileInfo selectedProfile)
        {
            ShowValidation("Select a Chrome profile first.");
            return;
        }
        if (_session is null || !string.Equals(_session.Profile.Key, selectedProfile.Key, StringComparison.OrdinalIgnoreCase))
        {
            await ConnectSelectedProfileAsync(refreshConversations: false);
            if (_session is null) return;
        }

        try
        {
            var target = await _session.ResolveConversationAsync(url, openIfMissing: true);
            if (target is null)
            {
                ShowValidation("The selected same chat could not be resolved in this Chrome profile. No other chat will be used.");
                return;
            }

            if (_loadedPlan is not null)
            {
                var planJson = SimpleMonitorMessagePlanService.Serialize(_loadedPlan);
                var enabledTexts = _loadedPlan.Messages.Where(step => step.Enabled).Select(step => step.Text).ToArray();
                await PersistStateAsync(url, enabledTexts, _loadedPlan.DefaultDelaySeconds, planJson);
                await _runner.StartAsync(
                    _session,
                    target.Url,
                    _loadedPlan.Messages,
                    _loadedPlan.DefaultDelaySeconds,
                    _loadedPlan.Loop,
                    CheckpointPlanMessageSentAsync);
                _statusLabel.Text = $"JSON plan running: {_loadedPlan.Name}. Same chat only; sent checkpoints are durable.";
            }
            else
            {
                var delay = Math.Max(15, (int)_delaySeconds.Value);
                await PersistStateAsync(url, manualMessages, delay, string.Empty);
                await _runner.StartAsync(_session, target.Url, manualMessages, delay);
                _statusLabel.Text = "Monitor running. Waiting for a safe same-chat send opportunity.";
            }
            SetRunningUi(true);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Start blocked: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Monitor Only", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task CheckpointPlanMessageSentAsync(int originalIndex, int total, string message, CancellationToken cancellationToken)
    {
        var plan = _loadedPlan;
        if (plan is null || originalIndex < 0 || originalIndex >= plan.Messages.Count) return;

        // Deliberately do not let a later monitor cancellation erase a confirmed send.
        plan.Messages[originalIndex].Sent = true;
        await _database.SetSettingAsync(PlanSetting, SimpleMonitorMessagePlanService.Serialize(plan));
        PostToUi(() =>
        {
            UpdatePlanSummary();
            _messagesList.Refresh();
            _cycleLabel.Text = $"SENT ✓ {originalIndex + 1}/{total}: {SingleLine(message)}";
        });
    }

    private async Task StopMonitorAsync()
    {
        _stopButton.Enabled = false;
        await _runner.StopAsync();
        SetRunningUi(false);
        _statusLabel.Text = "Monitor stopped. Confirmed sent checkpoints are preserved.";
    }

    private async Task PersistStateAsync(string conversationUrl, IReadOnlyList<string> messages, int delay, string planJson)
    {
        if (_profileCombo.SelectedItem is ChromeProfileInfo profile)
            await _database.SetSettingAsync(ProfileSetting, profile.Key);
        await _database.SetSettingAsync(ConversationSetting, conversationUrl);
        await _database.SetSettingAsync(MessagesSetting, JsonSerializer.Serialize(messages));
        await _database.SetSettingAsync(DelaySetting, delay.ToString());
        await _database.SetSettingAsync(PlanSetting, planJson);
    }

    private async Task PersistManualMessagesAsync()
    {
        var messages = _messagesList.Items
            .OfType<string>()
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .ToArray();
        await _database.SetSettingAsync(MessagesSetting, JsonSerializer.Serialize(messages));
        await _database.SetSettingAsync(PlanSetting, string.Empty);
    }

    private string GetConversationUrl()
        => _conversationCombo.SelectedItem is ConversationChoice choice ? choice.Tab.Url : _conversationCombo.Text.Trim();

    private async Task AddMessageAsync()
    {
        if (_runner.IsRunning || _loadedPlan is not null || string.IsNullOrWhiteSpace(_messageEditor.Text)) return;
        _messagesList.Items.Add(_messageEditor.Text);
        _messagesList.SelectedIndex = _messagesList.Items.Count - 1;
        await PersistManualMessagesAsync();
        _statusLabel.Text = "Message added and saved.";
    }

    private async Task UpdateMessageAsync()
    {
        if (_runner.IsRunning || _loadedPlan is not null) return;
        var index = _messagesList.SelectedIndex;
        if (index < 0 || string.IsNullOrWhiteSpace(_messageEditor.Text)) return;
        _messagesList.Items[index] = _messageEditor.Text;
        _messagesList.SelectedIndex = index;
        await PersistManualMessagesAsync();
        _statusLabel.Text = "Message updated and saved.";
    }

    private async Task RemoveMessageAsync()
    {
        if (_runner.IsRunning) return;
        var index = _messagesList.SelectedIndex;
        if (index < 0) return;

        if (_loadedPlan is not null)
        {
            if (index >= _loadedPlan.Messages.Count) return;
            var removed = _loadedPlan.Messages[index];
            _loadedPlan.Messages.RemoveAt(index);

            if (_loadedPlan.Messages.Count == 0 || !_loadedPlan.Messages.Any(step => step.Enabled))
            {
                var remaining = _loadedPlan.Messages.Select(step => step.Text).ToArray();
                _loadedPlan = null;
                ShowManualMessages(remaining);
                await PersistManualMessagesAsync();
                _statusLabel.Text = remaining.Length == 0
                    ? "Last JSON message deleted. The JSON plan is now cleared."
                    : "JSON message deleted. No enabled JSON steps remain, so the remaining rows were preserved as manual messages.";
            }
            else
            {
                await _database.SetSettingAsync(PlanSetting, SimpleMonitorMessagePlanService.Serialize(_loadedPlan));
                var nextSelection = Math.Min(index, _loadedPlan.Messages.Count - 1);
                ApplyJsonPlan(_loadedPlan);
                _messagesList.SelectedIndex = nextSelection;
                _statusLabel.Text = $"Deleted JSON message: {SingleLine(removed.Text)}. Updated plan saved.";
            }
        }
        else
        {
            _messagesList.Items.RemoveAt(index);
            if (_messagesList.Items.Count > 0)
                _messagesList.SelectedIndex = Math.Min(index, _messagesList.Items.Count - 1);
            else
                _messageEditor.Clear();
            await PersistManualMessagesAsync();
            _statusLabel.Text = "Message deleted and saved.";
        }

        UpdateMessageActionStates();
    }

    private async Task MoveMessageAsync(int offset)
    {
        if (_runner.IsRunning || _loadedPlan is not null) return;
        var index = _messagesList.SelectedIndex;
        var target = index + offset;
        if (index < 0 || target < 0 || target >= _messagesList.Items.Count) return;
        var item = _messagesList.Items[index];
        _messagesList.Items.RemoveAt(index);
        _messagesList.Items.Insert(target, item);
        _messagesList.SelectedIndex = target;
        await PersistManualMessagesAsync();
        _statusLabel.Text = "Message order updated and saved.";
    }

    private void UpdateMessageActionStates()
    {
        var selected = _messagesList.SelectedIndex;
        var hasText = !string.IsNullOrWhiteSpace(_messageEditor.Text);
        var manualEditable = !_runner.IsRunning && _loadedPlan is null;
        var canDelete = !_runner.IsRunning && selected >= 0;
        _addMessageButton.Enabled = manualEditable && hasText;
        _updateMessageButton.Enabled = manualEditable && selected >= 0 && hasText;
        _removeMessageButton.Enabled = canDelete;
        _moveUpButton.Enabled = manualEditable && selected > 0;
        _moveDownButton.Enabled = manualEditable && selected >= 0 && selected < _messagesList.Items.Count - 1;
        _previewPlanButton.Enabled = _loadedPlan is not null;
        _clearPlanButton.Enabled = _loadedPlan is not null && !_runner.IsRunning;
    }

    private void SetRunningUi(bool running)
    {
        _startButton.Enabled = !running;
        _stopButton.Enabled = running;
        _profileCombo.Enabled = !running;
        _connectButton.Enabled = !running;
        _refreshChatsButton.Enabled = !running;
        _conversationCombo.Enabled = !running;
        _messageEditor.ReadOnly = running || _loadedPlan is not null;
        _delaySeconds.Enabled = !running && _loadedPlan is null;
        _loadPlanButton.Enabled = !running;
        _clearPlanButton.Enabled = !running && _loadedPlan is not null;
        UpdateMessageActionStates();
    }

    private void SetBusy(bool busy, string? text)
    {
        UseWaitCursor = busy;
        if (!string.IsNullOrWhiteSpace(text)) _statusLabel.Text = text;
        if (!_runner.IsRunning)
        {
            _connectButton.Enabled = !busy;
            _refreshChatsButton.Enabled = !busy;
            _startButton.Enabled = !busy;
            _loadPlanButton.Enabled = !busy;
        }
    }

    private void OnRunnerStatusChanged(string status)
    {
        PostToUi(() =>
        {
            _statusLabel.Text = status;
            if (status.StartsWith("BLOCKED", StringComparison.OrdinalIgnoreCase)
                || status.StartsWith("Plan complete.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "Monitor stopped.", StringComparison.OrdinalIgnoreCase))
                SetRunningUi(false);
        });
    }

    private void OnRunnerMessageChanged(int index, int count, string message)
        => PostToUi(() =>
        {
            _cycleLabel.Text = $"Message {index}/{count}: {SingleLine(message)}";
            if (index > 0 && index <= _messagesList.Items.Count) _messagesList.SelectedIndex = index - 1;
        });

    private void OnRunnerMessageSent(int index, int count, string message)
        => PostToUi(() => _cycleLabel.Text = $"CONFIRMED ✓ {index}/{count}: {SingleLine(message)}");

    private void OnInspectorChanged(SimpleMonitorInspectorSnapshot snapshot)
        => PostToUi(() =>
        {
            _inspectorState.Text = $"State: {snapshot.State}";
            _inspectorMessage.Text = snapshot.CurrentMessage <= 0 ? "Current: —" : $"Current: {snapshot.CurrentMessage}/{snapshot.TotalMessages}";
            _inspectorProgress.Text = $"Sent: {snapshot.SentMessages}  •  Pending: {snapshot.PendingMessages}";
            _inspectorRetries.Text = $"CDP retries: {snapshot.PassiveReadRetries}";
            _inspectorCdp.Text = $"Last CDP: {snapshot.LastCdpEvent}";
            _inspectorError.Text = string.IsNullOrWhiteSpace(snapshot.LastError) ? "Last error: —" : $"Last error: {snapshot.LastError}";
            _inspectorError.ForeColor = string.IsNullOrWhiteSpace(snapshot.LastError) ? FluentTheme.Muted : Color.OrangeRed;
        });

    private void PostToUi(Action action)
    {
        if (_closing || IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(action); }
            catch (InvalidOperationException) { }
            return;
        }
        action();
    }

    private void ShowValidation(string message)
        => MessageBox.Show(this, message, "Monitor Only", MessageBoxButtons.OK, MessageBoxIcon.Information);

    private static List<string> DeserializeMessages(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json)?.Where(message => !string.IsNullOrWhiteSpace(message)).ToList() ?? [];
        }
        catch (JsonException) { return []; }
    }

    private static Label CreateRowLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold)
    };

    private static Label CreateInlineLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(0, 6, 8, 0),
        Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold)
    };

    private static Label CreateInlineMuted(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(4, 6, 14, 0),
        ForeColor = FluentTheme.Muted
    };

    private static Label InspectorValue(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(0, 5, 18, 3),
        Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold)
    };

    private static string SingleLine(string value)
    {
        var line = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return line.Length <= 70 ? line : line[..67] + "...";
    }

    private sealed record ConversationChoice(ChromeTab Tab)
    {
        public override string ToString()
            => string.IsNullOrWhiteSpace(Tab.Title) ? Tab.Url : $"{Tab.Title} — {Tab.Url}";
    }

    private sealed record PlanMessageChoice(int Index, SimpleMonitorMessageStep Step, int DefaultDelaySeconds)
    {
        public override string ToString()
        {
            var label = string.IsNullOrWhiteSpace(Step.Label) ? $"Message {Index}" : Step.Label;
            var status = Step.Sent ? "SENT ✓" : Step.Enabled ? "ON" : "OFF";
            return $"[{status}] {label} • {Step.EffectiveDelaySeconds(DefaultDelaySeconds)}s • {SingleLine(Step.Text)}";
        }
    }
}
