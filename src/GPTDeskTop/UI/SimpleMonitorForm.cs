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
    private readonly SimpleMonitorRunner _runner = new();
    private SimpleMonitorProfileSession? _session;
    private SimpleMonitorMessagePlan? _loadedPlan;

    private readonly RadioButton _currentModeRadio = new() { Text = "Current GPTDeskTop", AutoSize = true };
    private readonly RadioButton _monitorModeRadio = new() { Text = "Monitor Only — Same Chat", AutoSize = true, Checked = true };
    private readonly ComboBox _profileCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Button _connectButton = new() { Text = "Connect Profile", AutoSize = true };
    private readonly Button _refreshChatsButton = new() { Text = "Refresh Chats", AutoSize = true };
    private readonly ComboBox _conversationCombo = new() { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill };

    private readonly ListBox _messagesList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly TextBox _messageEditor = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        AcceptsReturn = true,
        ScrollBars = ScrollBars.Vertical,
        MinimumSize = new Size(0, 100)
    };
    private readonly Button _addMessageButton = new() { Text = "Add", AutoSize = true };
    private readonly Button _updateMessageButton = new() { Text = "Update", AutoSize = true };
    private readonly Button _removeMessageButton = new() { Text = "Remove", AutoSize = true };
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

    private readonly NumericUpDown _delaySeconds = new() { Minimum = 15, Maximum = 3600, Value = 15, Width = 110 };
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

    private bool _closing;

    public SimpleMonitorForm(LocalDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));

        Text = "GPTDeskTop — Monitor Only";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(980, 760);
        ClientSize = new Size(1180, 860);

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

        Shown += async (_, _) => await LoadSavedStateAsync();
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
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18),
            BackColor = FluentTheme.Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 174));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildTargetCard(), 0, 1);
        root.Controls.Add(BuildMessagesCard(), 0, 2);
        root.Controls.Add(BuildRuntimeCard(), 0, 3);
        root.Controls.Add(BuildStatusBar(), 0, 4);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = FluentTheme.Surface,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(16, 10, 16, 10),
            Margin = new Padding(0, 0, 0, 10)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = FluentTheme.Surface };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));

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
            Text = "Stored messages or JSON plan • selected Chrome profile • same ChatGPT conversation only",
            Dock = DockStyle.Fill,
            ForeColor = FluentTheme.Muted,
            TextAlign = ContentAlignment.TopLeft
        }, 0, 1);

        var modes = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = FluentTheme.Surface,
            Padding = new Padding(0, 13, 0, 0)
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
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 0, 10)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(CreateRowLabel("Chrome profile"), 0, 0);
        layout.Controls.Add(_profileCombo, 1, 0);
        var profileButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        profileButtons.Controls.Add(_connectButton);
        profileButtons.Controls.Add(_refreshChatsButton);
        layout.Controls.Add(profileButtons, 2, 0);

        layout.Controls.Add(CreateRowLabel("ChatGPT conversation"), 0, 1);
        layout.Controls.Add(_conversationCombo, 1, 1);
        layout.SetColumnSpan(_conversationCombo, 2);

        var hardRule = new Label
        {
            Text = "LOCKED RULE: Same Chat = ON   •   New Chat = OFF   •   Rotation = OFF   •   fallback to another chat = OFF",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold),
            ForeColor = FluentTheme.Success,
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(hardRule, 0, 2);
        layout.SetColumnSpan(hardRule, 3);

        var securityNote = new Label
        {
            Text = "GPTDeskTop keeps an automation-safe persistent Chrome session for each selected profile. On first use, sign in to ChatGPT once in the Chrome window that opens; that managed profile retains the login.",
            Dock = DockStyle.Fill,
            ForeColor = FluentTheme.Muted,
            AutoEllipsis = true
        };
        layout.Controls.Add(securityNote, 0, 3);
        layout.SetColumnSpan(securityNote, 3);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildMessagesCard()
    {
        var group = new GroupBox
        {
            Text = "2. Message sequence / JSON Message Plan",
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 0, 10)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var planButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 3, 0, 0) };
        planButtons.Controls.Add(_loadPlanButton);
        planButtons.Controls.Add(_downloadSampleButton);
        planButtons.Controls.Add(_copyPromptButton);
        planButtons.Controls.Add(_previewPlanButton);
        planButtons.Controls.Add(_clearPlanButton);
        layout.Controls.Add(planButtons, 0, 0);
        layout.SetColumnSpan(planButtons, 2);

        layout.Controls.Add(_planSummaryLabel, 0, 1);
        layout.SetColumnSpan(_planSummaryLabel, 2);

        layout.Controls.Add(_messagesList, 0, 2);
        layout.Controls.Add(_messageEditor, 1, 2);

        var listButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 7, 0, 0) };
        listButtons.Controls.Add(_moveUpButton);
        listButtons.Controls.Add(_moveDownButton);
        layout.Controls.Add(listButtons, 0, 3);

        var editButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 7, 0, 0) };
        editButtons.Controls.Add(_addMessageButton);
        editButtons.Controls.Add(_updateMessageButton);
        editButtons.Controls.Add(_removeMessageButton);
        layout.Controls.Add(editButtons, 1, 3);

        group.Controls.Add(layout);
        return group;
    }

    private Control BuildRuntimeCard()
    {
        var group = new GroupBox
        {
            Text = "3. Runtime",
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 0, 8)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 245));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(CreateRowLabel("Default post-response delay"), 0, 0);
        layout.Controls.Add(_delaySeconds, 1, 0);
        layout.Controls.Add(new Label
        {
            Text = "seconds — minimum 15. JSON plans may override this per message.",
            Dock = DockStyle.Fill,
            ForeColor = FluentTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft
        }, 2, 0);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        actions.Controls.Add(_stopButton);
        actions.Controls.Add(_startButton);
        layout.Controls.Add(actions, 3, 0);

        var logic = new Label
        {
            Text = "Send message → wait for the assistant response to finish → wait its safety delay → revalidate the same chat → send the next enabled message. JSON loop=false stops after one complete sequence.",
            Dock = DockStyle.Fill,
            ForeColor = FluentTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(logic, 0, 1);
        layout.SetColumnSpan(logic, 4);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildStatusBar()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = FluentTheme.Surface, Padding = new Padding(12, 6, 12, 6) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = FluentTheme.Surface };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        layout.Controls.Add(_statusLabel, 0, 0);
        layout.Controls.Add(_cycleLabel, 1, 0);
        panel.Controls.Add(layout);
        return panel;
    }

    private static Label CreateRowLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold)
    };

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
        _messageEditor.TextChanged += (_, _) => UpdateMessageActionStates();
        _addMessageButton.Click += (_, _) => AddMessage();
        _updateMessageButton.Click += (_, _) => UpdateMessage();
        _removeMessageButton.Click += (_, _) => RemoveMessage();
        _moveUpButton.Click += (_, _) => MoveMessage(-1);
        _moveDownButton.Click += (_, _) => MoveMessage(1);
        _loadPlanButton.Click += async (_, _) => await LoadJsonPlanAsync();
        _downloadSampleButton.Click += async (_, _) => await SaveSampleJsonAsync();
        _copyPromptButton.Click += (_, _) => CopyChatGptPrompt();
        _previewPlanButton.Click += (_, _) => PreviewPlan();
        _clearPlanButton.Click += async (_, _) => await ClearJsonPlanAsync();
        _startButton.Click += async (_, _) => await StartMonitorAsync();
        _stopButton.Click += async (_, _) => await StopMonitorAsync();
        _profileCombo.SelectedIndexChanged += (_, _) =>
        {
            if (!_runner.IsRunning) _statusLabel.Text = "Profile selected. Choose Connect Profile to open/attach its managed Chrome session.";
        };
        UpdateMessageActionStates();
    }

    private void ConfigureAccessibility()
    {
        AccessibleName = "GPTDeskTop Monitor Only";
        AccessibleDescription = "Standalone same-chat monitor for stored messages and JSON message plans.";
        _profileCombo.AccessibleName = "Chrome profile";
        _conversationCombo.AccessibleName = "ChatGPT conversation URL";
        _messagesList.AccessibleName = "Stored message sequence";
        _messageEditor.AccessibleName = "Stored message editor";
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
            var messagesJson = await _database.GetSettingAsync(MessagesSetting);
            var messages = DeserializeMessages(messagesJson);
            if (messages.Count == 0) messages.Add("كمل");
            ShowManualMessages(messages);
        }

        if (_loadedPlan is null)
            _statusLabel.Text = "Ready. Select Connect Profile, then choose the same ChatGPT conversation to monitor.";
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
        finally
        {
            SetBusy(false, null);
        }
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
                ? "No stable ChatGPT conversations are open yet. Sign in/open the target chat in the selected Chrome window, then Refresh Chats."
                : $"Found {tabs.Count} stable ChatGPT conversation(s) in {_session.Profile.DisplayLabel}.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Refresh failed: {ex.Message}";
        }
        finally
        {
            SetBusy(false, null);
        }
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
            var json = await File.ReadAllTextAsync(dialog.FileName);
            var plan = SimpleMonitorMessagePlanService.ParseAndValidate(json);
            ApplyJsonPlan(plan);
            await _database.SetSettingAsync(PlanSetting, SimpleMonitorMessagePlanService.Serialize(plan));
            await _database.SetSettingAsync(DelaySetting, plan.DefaultDelaySeconds.ToString());
            _statusLabel.Text = $"JSON plan loaded and validated: {plan.Name}.";
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
            _statusLabel.Text = "ChatGPT JSON-generation prompt copied. Attach the downloaded sample JSON and paste the prompt.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Clipboard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void PreviewPlan()
    {
        if (_loadedPlan is null)
        {
            ShowValidation("Load a JSON message plan first.");
            return;
        }

        MessageBox.Show(
            this,
            SimpleMonitorMessagePlanService.BuildPreview(_loadedPlan),
            "JSON Message Plan — Validated Preview",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private async Task ClearJsonPlanAsync()
    {
        if (_loadedPlan is null || _runner.IsRunning) return;
        var manualMessages = _loadedPlan.Messages
            .Where(step => step.Enabled)
            .Select(step => step.Text)
            .ToList();
        var defaultDelay = _loadedPlan.DefaultDelaySeconds;
        _loadedPlan = null;
        _delaySeconds.Value = Math.Clamp(defaultDelay, 15, 3600);
        ShowManualMessages(manualMessages.Count == 0 ? ["كمل"] : manualMessages);
        await _database.SetSettingAsync(PlanSetting, string.Empty);
        _statusLabel.Text = "JSON plan cleared. Enabled plan messages were retained as manual messages.";
        UpdateMessageActionStates();
    }

    private void ApplyJsonPlan(SimpleMonitorMessagePlan plan)
    {
        _loadedPlan = plan;
        _delaySeconds.Value = Math.Clamp(plan.DefaultDelaySeconds, 15, 3600);
        _messagesList.Items.Clear();
        for (var index = 0; index < plan.Messages.Count; index++)
            _messagesList.Items.Add(new PlanMessageChoice(index + 1, plan.Messages[index], plan.DefaultDelaySeconds));
        if (_messagesList.Items.Count > 0) _messagesList.SelectedIndex = 0;
        _planSummaryLabel.Text = $"JSON: {plan.Name} • {(plan.Loop ? "LOOP" : "RUN ONCE")} • {plan.Messages.Count(step => step.Enabled)} enabled/{plan.Messages.Count} total • default {plan.DefaultDelaySeconds}s";
        _previewPlanButton.Enabled = true;
        _clearPlanButton.Enabled = true;
        _messageEditor.ReadOnly = true;
        UpdateMessageActionStates();
    }

    private void ShowManualMessages(IReadOnlyList<string> messages)
    {
        _loadedPlan = null;
        _messagesList.Items.Clear();
        foreach (var message in messages.Where(message => !string.IsNullOrWhiteSpace(message)))
            _messagesList.Items.Add(message);
        if (_messagesList.Items.Count > 0) _messagesList.SelectedIndex = 0;
        _planSummaryLabel.Text = "Manual message mode — or load a JSON plan generated from the sample.";
        _previewPlanButton.Enabled = false;
        _clearPlanButton.Enabled = false;
        _messageEditor.ReadOnly = _runner.IsRunning;
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
                    _loadedPlan.Loop);
                _statusLabel.Text = $"JSON plan running: {_loadedPlan.Name}. Same chat only.";
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

    private async Task StopMonitorAsync()
    {
        _stopButton.Enabled = false;
        await _runner.StopAsync();
        SetRunningUi(false);
        _statusLabel.Text = "Monitor stopped.";
    }

    private async Task PersistStateAsync(
        string conversationUrl,
        IReadOnlyList<string> messages,
        int delay,
        string planJson)
    {
        if (_profileCombo.SelectedItem is ChromeProfileInfo profile)
            await _database.SetSettingAsync(ProfileSetting, profile.Key);
        await _database.SetSettingAsync(ConversationSetting, conversationUrl);
        await _database.SetSettingAsync(MessagesSetting, JsonSerializer.Serialize(messages));
        await _database.SetSettingAsync(DelaySetting, delay.ToString());
        await _database.SetSettingAsync(PlanSetting, planJson);
    }

    private string GetConversationUrl()
        => _conversationCombo.SelectedItem is ConversationChoice choice
            ? choice.Tab.Url
            : _conversationCombo.Text.Trim();

    private void AddMessage()
    {
        if (_loadedPlan is not null || string.IsNullOrWhiteSpace(_messageEditor.Text)) return;
        _messagesList.Items.Add(_messageEditor.Text);
        _messagesList.SelectedIndex = _messagesList.Items.Count - 1;
    }

    private void UpdateMessage()
    {
        if (_loadedPlan is not null) return;
        var index = _messagesList.SelectedIndex;
        if (index < 0 || string.IsNullOrWhiteSpace(_messageEditor.Text)) return;
        _messagesList.Items[index] = _messageEditor.Text;
        _messagesList.SelectedIndex = index;
    }

    private void RemoveMessage()
    {
        if (_loadedPlan is not null) return;
        var index = _messagesList.SelectedIndex;
        if (index < 0) return;
        _messagesList.Items.RemoveAt(index);
        if (_messagesList.Items.Count > 0) _messagesList.SelectedIndex = Math.Min(index, _messagesList.Items.Count - 1);
        else _messageEditor.Clear();
        UpdateMessageActionStates();
    }

    private void MoveMessage(int offset)
    {
        if (_loadedPlan is not null) return;
        var index = _messagesList.SelectedIndex;
        var target = index + offset;
        if (index < 0 || target < 0 || target >= _messagesList.Items.Count) return;
        var item = _messagesList.Items[index];
        _messagesList.Items.RemoveAt(index);
        _messagesList.Items.Insert(target, item);
        _messagesList.SelectedIndex = target;
    }

    private void UpdateMessageActionStates()
    {
        var selected = _messagesList.SelectedIndex;
        var hasText = !string.IsNullOrWhiteSpace(_messageEditor.Text);
        var editable = !_runner.IsRunning && _loadedPlan is null;
        _addMessageButton.Enabled = editable && hasText;
        _updateMessageButton.Enabled = editable && selected >= 0 && hasText;
        _removeMessageButton.Enabled = editable && selected >= 0;
        _moveUpButton.Enabled = editable && selected > 0;
        _moveDownButton.Enabled = editable && selected >= 0 && selected < _messagesList.Items.Count - 1;
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
        => PostToUi(() => _cycleLabel.Text = $"Message {index}/{count}: {SingleLine(message)}");

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
            return JsonSerializer.Deserialize<List<string>>(json)?
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

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
            var status = Step.Enabled ? "ON" : "OFF";
            return $"[{status}] {label} • {Step.EffectiveDelaySeconds(DefaultDelaySeconds)}s • {SingleLine(Step.Text)}";
        }
    }
}
