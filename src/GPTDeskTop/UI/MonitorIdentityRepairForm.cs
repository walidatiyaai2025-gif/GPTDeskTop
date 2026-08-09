using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

public sealed class MonitorIdentityRepairForm : Form
{
    private readonly ChromeDevToolsService _chrome;
    private readonly LocalDatabase _database;
    private readonly MonitorIdentityRepairService _repairService;
    private readonly DuplicateOwnershipRepairService _duplicateRepairService;
    private readonly ComboBox _monitorBox = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _conversationBox = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _statusLabel = new() { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = FluentTheme.Muted, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _refreshButton = new() { Text = "Refresh", AutoSize = true };
    private readonly Button _rebindButton = new() { Text = "Rebind Monitor", AutoSize = true, Enabled = false };
    private readonly Button _cancelButton = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
    private bool _loading;

    public MonitorIdentityRepairForm(ChromeDevToolsService chrome, LocalDatabase database)
    {
        _chrome = chrome ?? throw new ArgumentNullException(nameof(chrome));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _repairService = new MonitorIdentityRepairService(database);
        _duplicateRepairService = new DuplicateOwnershipRepairService(database);

        Text = "Repair Monitor Conversation Ownership";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(720, 420);
        ClientSize = new Size(820, 470);
        AccessibleName = "Monitor conversation identity repair";
        AccessibleDescription = "Repair an invalid saved conversation identity or move one duplicate owner to an unowned stable ChatGPT conversation while preserving the same monitor identity and history.";

        BuildUi();
        ConfigureAccessibility();
        WireEvents();
        FluentTheme.Apply(this);
        FluentTheme.StyleButton(_rebindButton, primary: true);
        AcceptButton = _rebindButton;
        CancelButton = _cancelButton;
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(20),
            BackColor = FluentTheme.Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = FluentTheme.Background };
        heading.Controls.Add(new Label
        {
            Text = "Repair a conversation blocker",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Display", 15F, FontStyle.Bold),
            ForeColor = FluentTheme.Text,
            TextAlign = ContentAlignment.BottomLeft
        }, 0, 0);
        heading.Controls.Add(FluentTheme.CreateMutedLabel("Choose an invalid identity or duplicate owner and a safe replacement conversation. The existing Monitor ID, history, settings and rotation count are preserved."), 0, 1);

        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(BuildSelectionCard("Blocked saved monitor", "Invalid identities and every monitor participating in duplicate stable-conversation ownership are listed.", _monitorBox), 0, 1);
        root.Controls.Add(BuildSelectionCard("Unowned replacement conversation", "Only stable ChatGPT conversations that are not currently owned by any saved monitor are offered.", _conversationBox), 0, 2);

        var notice = new Panel { Dock = DockStyle.Fill, BackColor = FluentTheme.SurfaceAlt, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(12) };
        _statusLabel.Text = "Refresh to discover invalid identities, duplicate owners and safe unowned replacement conversations. Rebinding never clears CrashRecoveryPending directly.";
        notice.Controls.Add(_statusLabel);
        root.Controls.Add(notice, 0, 3);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 10, 0, 0), BackColor = FluentTheme.Background };
        buttons.Controls.Add(_cancelButton);
        buttons.Controls.Add(_rebindButton);
        buttons.Controls.Add(_refreshButton);
        root.Controls.Add(buttons, 0, 4);
        Controls.Add(root);
    }

    private static Control BuildSelectionCard(string title, string description, Control selector)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = FluentTheme.Surface,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(12, 6, 12, 8),
            Margin = new Padding(0, 4, 0, 4)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(FluentTheme.CreateSectionTitle(title), 0, 0);
        panel.Controls.Add(FluentTheme.CreateMutedLabel(description), 0, 1);
        panel.Controls.Add(selector, 0, 2);
        return panel;
    }

    private void ConfigureAccessibility()
    {
        _monitorBox.AccessibleName = "Blocked saved monitor";
        _monitorBox.AccessibleDescription = "Select an invalid identity or duplicate conversation owner that needs repair.";
        _conversationBox.AccessibleName = "Unowned replacement ChatGPT conversation";
        _conversationBox.AccessibleDescription = "Select a currently open stable ChatGPT conversation that no saved monitor owns.";
        _statusLabel.AccessibleName = "Identity repair status";
        _refreshButton.AccessibleName = "Refresh repair choices";
        _rebindButton.AccessibleName = "Rebind selected monitor";
        _cancelButton.AccessibleName = "Cancel monitor identity repair";
        _monitorBox.TabIndex = 0;
        _conversationBox.TabIndex = 1;
        _refreshButton.TabIndex = 2;
        _rebindButton.TabIndex = 3;
        _cancelButton.TabIndex = 4;
    }

    private void WireEvents()
    {
        Shown += async (_, _) => await RefreshChoicesAsync();
        _refreshButton.Click += async (_, _) => await RefreshChoicesAsync();
        _monitorBox.SelectedIndexChanged += (_, _) => UpdateActionState();
        _conversationBox.SelectedIndexChanged += (_, _) => UpdateActionState();
        _rebindButton.Click += async (_, _) => await RebindAsync();
    }

    private async Task RefreshChoicesAsync()
    {
        if (_loading) return;
        _loading = true;
        _refreshButton.Enabled = false;
        _rebindButton.Enabled = false;
        _statusLabel.Text = "Checking saved monitors and open ChatGPT conversations…";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var monitors = await _database.GetSavedMonitorsAsync(timeout.Token);
            var duplicateIds = MonitorConversationOwnership.FindDuplicateMonitorIds(monitors);
            var blockers = monitors
                .Where(saved => !RuntimeHealthPresentation.IsChatGptConversationUrl(saved.Url) || duplicateIds.Contains(saved.Id))
                .Select(saved => new MonitorChoice(saved, duplicateIds.Contains(saved.Id)))
                .ToList();
            var invalidCount = blockers.Count(choice => !choice.IsDuplicateOwner);
            var duplicateCount = blockers.Count(choice => choice.IsDuplicateOwner);
            var ownedConversationUrls = monitors
                .Where(saved => RuntimeHealthPresentation.IsChatGptConversationUrl(saved.Url))
                .Select(saved => saved.Url)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var tabs = await _chrome.GetTabsAsync(timeout.Token);
            var conversations = tabs
                .Where(tab => RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url))
                .Where(tab => !ownedConversationUrls.Contains(tab.Url))
                .GroupBy(tab => tab.Url, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Select(tab => new ConversationChoice(tab))
                .ToList();

            _monitorBox.DataSource = blockers;
            _conversationBox.DataSource = conversations;
            _statusLabel.Text = blockers.Count == 0
                ? "No invalid identities or duplicate conversation owners were found."
                : conversations.Count == 0
                    ? $"{invalidCount} invalid identity blocker(s) and {duplicateCount} duplicate-owner blocker(s) were found, but no unowned stable ChatGPT conversation is currently open."
                    : $"{invalidCount} invalid identity blocker(s) and {duplicateCount} duplicate-owner blocker(s) can be repaired. Select a monitor and unowned replacement conversation.";
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, "MonitorIdentityRepairForm.RefreshChoices");
            _monitorBox.DataSource = null;
            _conversationBox.DataSource = null;
            _statusLabel.Text = $"Cannot load repair choices: {ex.Message}";
        }
        finally
        {
            _loading = false;
            _refreshButton.Enabled = true;
            UpdateActionState();
        }
    }

    private void UpdateActionState()
        => _rebindButton.Enabled = !_loading
            && _monitorBox.SelectedItem is MonitorChoice
            && _conversationBox.SelectedItem is ConversationChoice;

    private async Task RebindAsync()
    {
        if (_monitorBox.SelectedItem is not MonitorChoice monitorChoice
            || _conversationBox.SelectedItem is not ConversationChoice conversationChoice)
            return;

        var blockerKind = monitorChoice.IsDuplicateOwner ? "duplicate owner" : "invalid identity";
        var message = $"Rebind {blockerKind} monitor #{monitorChoice.Monitor.Id} to this unowned ChatGPT conversation?{Environment.NewLine}{Environment.NewLine}{conversationChoice.Tab.Title}{Environment.NewLine}{conversationChoice.Tab.Url}{Environment.NewLine}{Environment.NewLine}The monitor ID, history, automation settings and rotation count will be preserved.";
        if (MessageBox.Show(this, message, "Confirm Monitor Rebind", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        _loading = true;
        _refreshButton.Enabled = false;
        _rebindButton.Enabled = false;
        try
        {
            var result = monitorChoice.IsDuplicateOwner
                ? await _duplicateRepairService.RebindAsync(monitorChoice.Monitor.Id, conversationChoice.Tab)
                : await _repairService.RebindAsync(monitorChoice.Monitor.Id, conversationChoice.Tab);
            _statusLabel.Text = result.CrashRecoveryPending
                ? $"Monitor #{result.MonitorId} repaired. Crash recovery is still pending and will clear only through normal recovery processing."
                : $"Monitor #{result.MonitorId} repaired successfully.";
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, "MonitorIdentityRepairForm.Rebind");
            MessageBox.Show(this, ex.Message, "Monitor Rebind Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            await RefreshChoicesAsync();
        }
        finally
        {
            _loading = false;
            if (!IsDisposed)
            {
                _refreshButton.Enabled = true;
                UpdateActionState();
            }
        }
    }

    private sealed record MonitorChoice(SavedMonitor Monitor, bool IsDuplicateOwner)
    {
        public override string ToString()
            => $"{(IsDuplicateOwner ? "Duplicate owner" : "Invalid identity")}  —  #{Monitor.Id}  {Monitor.Title}  —  {Monitor.Url}";
    }

    private sealed record ConversationChoice(ChromeTab Tab)
    {
        public override string ToString() => $"{Tab.Title}  —  {Tab.Url}";
    }
}