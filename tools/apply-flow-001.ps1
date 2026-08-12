$ErrorActionPreference = 'Stop'

function Replace-Exact {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [Parameter(Mandatory=$true)][string]$Old,
        [Parameter(Mandatory=$true)][string]$New
    )

    $content = [IO.File]::ReadAllText($Path)
    if (-not $content.Contains($Old)) {
        throw "FLOW-001 patch anchor was not found in $Path"
    }
    $updated = $content.Replace($Old, $New)
    [IO.File]::WriteAllText($Path, $updated, [Text.UTF8Encoding]::new($false))
}

$path = 'src/GPTDeskTop/UI/MainForm.cs'

Replace-Exact -Path $path -Old @'
    private readonly Button _refreshTabsButton = new() { Text = "Refresh", AutoSize = true };
    private readonly Button _addMonitorButton = new() { Text = "Add Monitor", AutoSize = true };
'@ -New @'
    private readonly Button _refreshTabsButton = new() { Text = "Refresh", AutoSize = true };
    private readonly Button _newChatMonitorButton = new() { Text = "New Chat + Monitor", AutoSize = true };
    private readonly Button _addMonitorButton = new() { Text = "Add Monitor", AutoSize = true };
'@

Replace-Exact -Path $path -Old @'
    private bool _shutdownCompleted;
    private bool _ownedResourcesDisposed;
'@ -New @'
    private bool _shutdownCompleted;
    private bool _ownedResourcesDisposed;
    private bool _newChatMonitorWorkflowRunning;
'@

Replace-Exact -Path $path -Old @'
        FluentTheme.StyleButton(_launchChromeButton, primary: true);
        FluentTheme.StyleButton(_startAllButton, primary: true);
'@ -New @'
        FluentTheme.StyleButton(_launchChromeButton, primary: true);
        FluentTheme.StyleButton(_newChatMonitorButton, primary: true);
        FluentTheme.StyleButton(_startAllButton, primary: true);
'@

Replace-Exact -Path $path -Old @'
        toolbar.Controls.Add(CreateActionGroup("MONITOR", _addMonitorButton, _monitorSettingsButton, _deleteMonitorButton));
'@ -New @'
        toolbar.Controls.Add(CreateActionGroup("MONITOR", _newChatMonitorButton, _addMonitorButton, _monitorSettingsButton, _deleteMonitorButton));
'@

Replace-Exact -Path $path -Old @'
        _refreshTabsButton.Click += async (_, _) => await RefreshTabsAsync();
        _addMonitorButton.Click += async (_, _) => await AddSelectedTabAsync();
'@ -New @'
        _refreshTabsButton.Click += async (_, _) => await RefreshTabsAsync();
        _newChatMonitorButton.Click += async (_, _) => await CreateNewChatMonitorAsync();
        _addMonitorButton.Click += async (_, _) => await AddSelectedTabAsync();
'@

Replace-Exact -Path $path -Old @'
        _toolTip.SetToolTip(_refreshTabsButton, "Refresh the list of currently open ChatGPT conversation tabs. Shortcut: F5.");
        _toolTip.SetToolTip(_addMonitorButton, "Create a saved monitor from the selected open ChatGPT conversation(s). Shortcut: Ctrl+N.");
'@ -New @'
        _toolTip.SetToolTip(_refreshTabsButton, "Refresh the list of currently open ChatGPT conversation tabs. Shortcut: F5.");
        _toolTip.SetToolTip(_newChatMonitorButton, "Create a fresh ChatGPT conversation, send an initial message, then create and start a monitor with a separate auto reply.");
        _toolTip.SetToolTip(_addMonitorButton, "Create a saved monitor from the selected open ChatGPT conversation(s). Shortcut: Ctrl+N.");
'@

$launchBlock = @'
    private async Task LaunchChromeAsync()
    {
        try
        {
            _chrome.LaunchMonitorChrome();
            AppendActivity("Monitor Chrome launched.");
            await Task.Delay(1800);
            if (_chromeHidden) await _chrome.HideMonitorChromeAsync();
            await RefreshTabsAsync();
        }
        catch (Exception ex) { ShowError("Chrome Launch Error", ex.Message); }
    }
'@

$newLaunchBlock = @'
    private async Task LaunchChromeAsync()
    {
        try
        {
            _chrome.LaunchMonitorChrome();
            AppendActivity("Monitor Chrome launched.");
            await Task.Delay(1800);
            if (_chromeHidden) await _chrome.HideMonitorChromeAsync();
            await RefreshTabsAsync();
        }
        catch (Exception ex) { ShowError("Chrome Launch Error", ex.Message); }
    }

    private async Task CreateNewChatMonitorAsync()
    {
        if (_newChatMonitorWorkflowRunning) return;

        var initialMessage = await _database.GetSettingAsync("NewChatBootstrapMessage") ?? string.Empty;
        var monitorAutoReply = await _database.GetSettingAsync("NewChatMonitorAutoReply")
            ?? await _database.GetSettingAsync("DefaultAutoReply")
            ?? "كمل";

        if (!NewChatMonitorForm.Edit(
                this,
                initialMessage,
                monitorAutoReply,
                out var updatedInitialMessage,
                out var updatedMonitorAutoReply))
            return;

        await _database.SetSettingAsync("NewChatBootstrapMessage", updatedInitialMessage);
        await _database.SetSettingAsync("NewChatMonitorAutoReply", updatedMonitorAutoReply);

        _newChatMonitorWorkflowRunning = true;
        UpdateActionStates();
        AppendActivity("New Chat + Monitor: creating a fresh ChatGPT conversation and delivering the verified initial message...");

        try
        {
            var workflow = new NewChatMonitorWorkflowService(_chrome, _monitor, _database);
            var result = await workflow.ExecuteAsync(updatedInitialMessage, updatedMonitorAutoReply);
            await RefreshTabsAsync();
            await RefreshMonitorsAsync();
            SelectMonitorRow(result.Monitor.Id);
            AppendActivity($"New Chat + Monitor complete: monitor #{result.Monitor.Id} is running on {result.ConversationTab.Url}.");
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, "MainForm.CreateNewChatMonitor");
            await RefreshTabsAsync();
            await RefreshMonitorsAsync();
            ShowError("New Chat + Monitor Error", ex.Message);
        }
        finally
        {
            _newChatMonitorWorkflowRunning = false;
            UpdateActionStates();
        }
    }
'@
Replace-Exact -Path $path -Old $launchBlock -New $newLaunchBlock

Replace-Exact -Path $path -Old @'
        _addMonitorButton.Enabled = hasTab;
        _monitorSettingsButton.Enabled = hasMonitor && !selectedRunning;
'@ -New @'
        _newChatMonitorButton.Enabled = !_newChatMonitorWorkflowRunning && !_shutdownRequested;
        _addMonitorButton.Enabled = hasTab && !_newChatMonitorWorkflowRunning;
        _monitorSettingsButton.Enabled = hasMonitor && !selectedRunning && !_newChatMonitorWorkflowRunning;
'@

Write-Host 'FLOW-001 MainForm patch applied.'
