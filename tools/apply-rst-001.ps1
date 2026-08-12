$ErrorActionPreference = 'Stop'

function Replace-Exact {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [Parameter(Mandatory=$true)][string]$Old,
        [Parameter(Mandatory=$true)][string]$New
    )

    $content = [IO.File]::ReadAllText($Path)
    if (-not $content.Contains($Old)) {
        throw "RST-001 patch anchor was not found in $Path"
    }
    $updated = $content.Replace($Old, $New)
    [IO.File]::WriteAllText($Path, $updated, [Text.UTF8Encoding]::new($false))
}

$enginePath = 'src/GPTDeskTop/Services/DevelopmentTaskEngine/DevelopmentTaskEngine.cs'
$engineAnchor = @'
    public void RestorePosition(int messageIndex, int completedMessages, DevelopmentTaskEngineStatus status)
'@
$engineReplacement = @'
    public async Task<bool> ResumeIfActiveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ReloadScheduleSettings();
            await LoadStateAsync(cancellationToken).ConfigureAwait(false);
            if (_state.Status is not (DevelopmentTaskEngineStatus.Working or DevelopmentTaskEngineStatus.Cooling))
            {
                PublishState();
                return false;
            }

            var messages = await LoadMessagesAsync(cancellationToken).ConfigureAwait(false);
            if (messages.Count == 0) throw new InvalidOperationException("No development task messages are configured.");
            _state.TotalMessages = messages.Count;
            if (_state.CurrentMessageIndex >= messages.Count)
            {
                _state.Status = DevelopmentTaskEngineStatus.Completed;
                await SaveStateAsync(cancellationToken).ConfigureAwait(false);
                PublishState();
                return false;
            }

            if (_state.Status == DevelopmentTaskEngineStatus.Working)
            {
                _state.WorkWindowStartedAt ??= DateTimeOffset.UtcNow;
                _messageDeliveredThisWindow =
                    _state.LastDeliveredMessageIndex == _state.CurrentMessageIndex - 1 &&
                    !string.IsNullOrWhiteSpace(_state.LastDeliveredMessageFingerprint);
            }
            else
            {
                _state.CoolingStartedAt ??= DateTimeOffset.UtcNow;
                _messageDeliveredThisWindow = false;
            }

            _state.LastError = null;
            _lastEmittedMessageIndex = null;
            await SaveStateAsync(cancellationToken).ConfigureAwait(false);
            PublishState();
            await RestartWorkerAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }

    public void RestorePosition(int messageIndex, int completedMessages, DevelopmentTaskEngineStatus status)
'@
Replace-Exact -Path $enginePath -Old $engineAnchor -New $engineReplacement

$coordinatorPath = 'src/GPTDeskTop/Services/DevelopmentTaskEngine/DevelopmentTaskRuntimeCoordinator.cs'
$coordinatorAnchor = @'
    public async Task StopAsync(CancellationToken cancellationToken = default)
'@
$coordinatorReplacement = @'
    public async Task<bool> ResumeIfActiveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) return false;
            _started = await _engine.ResumeIfActiveAsync(cancellationToken).ConfigureAwait(false);
            return _started;
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
'@
Replace-Exact -Path $coordinatorPath -Old $coordinatorAnchor -New $coordinatorReplacement

$bindingPath = 'src/GPTDeskTop/Services/DevelopmentTaskEngine/DevelopmentTaskRuntimeBinding.cs'
$bindingAnchor = @'
    public Task StopAsync(CancellationToken cancellationToken = default)
'@
$bindingReplacement = @'
    public Task<bool> ResumeIfActiveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _runtime.ResumeIfActiveAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
'@
Replace-Exact -Path $bindingPath -Old $bindingAnchor -New $bindingReplacement

$mainFormPath = 'src/GPTDeskTop/UI/MainForm.cs'
Replace-Exact -Path $mainFormPath -Old @'
        await _monitor.DeleteMonitorAsync(id);
        _selectedMonitor = null;
'@ -New @'
        await _monitor.DeleteMonitorAsync(id);
        await LastWorkingStateService.SetMonitorDesiredRunningAsync(_database, id, false);
        _selectedMonitor = null;
'@

Replace-Exact -Path $mainFormPath -Old @'
        await _monitor.StopMonitorAsync(_selectedMonitor.Id);
        await RefreshMonitorsAsync();
'@ -New @'
        var monitorId = _selectedMonitor.Id;
        await _monitor.StopMonitorAsync(monitorId);
        await LastWorkingStateService.SetMonitorDesiredRunningAsync(_database, monitorId, false);
        await RefreshMonitorsAsync();
'@

Replace-Exact -Path $mainFormPath -Old @'
        await _monitor.StopAllAsync();
        await RefreshMonitorsAsync();
        AppendActivity("All monitors stopped.");
'@ -New @'
        await _monitor.StopAllAsync();
        await LastWorkingStateService.ClearDesiredMonitorsAsync(_database);
        await RefreshMonitorsAsync();
        AppendActivity("All monitors stopped. Restart auto-resume intent cleared.");
'@

Replace-Exact -Path $mainFormPath -Old @'
        await _monitor.StartMonitorAsync(monitor, tab);
        await RefreshMonitorsAsync();
'@ -New @'
        await _monitor.StartMonitorAsync(monitor, tab);
        if (_monitor.IsMonitorRunning(monitor.Id))
            await LastWorkingStateService.SetMonitorDesiredRunningAsync(_database, monitor.Id, true);
        await RefreshMonitorsAsync();
'@

$programPath = 'src/GPTDeskTop/Program.cs'
$programAnchor = @'
                        }
                    }
                }
                catch (Exception ex)
'@
$programReplacement = @'
                        }

                        await LastWorkingStateService.ReplaceDesiredMonitorIdsAsync(
                            database,
                            takeover.RunningMonitorIds);
                    }
                    else
                    {
                        var resume = await LastWorkingStateService.ResumeDesiredMonitorsAsync(
                            chrome,
                            monitor,
                            database);
                        if (resume.IncompleteCount > 0)
                        {
                            var summary = string.Join(
                                "; ",
                                resume.Outcomes
                                    .Where(outcome => !string.Equals(outcome.Status, "Resumed", StringComparison.Ordinal))
                                    .Select(outcome => $"{outcome.MonitorId}:{outcome.Reason}"));
                            await ExceptionLogService.LogAsync(
                                new InvalidOperationException(
                                    $"Restart resume restored {resume.ResumedCount}/{resume.RequestedCount} desired monitors. Incomplete outcomes: {summary}"),
                                "Program.LastWorkingStateResumeIncomplete");
                        }
                    }

                    if (developmentRuntime is not null)
                    {
                        var developmentResumed = await developmentRuntime.ResumeIfActiveAsync();
                        await database.SetSettingAsync(
                            "Runtime.DevelopmentTaskAutoResumed",
                            developmentResumed ? "1" : "0");
                        if (developmentResumed)
                            await database.SetSettingAsync("Runtime.DevelopmentTaskAutoResumeUtc", DateTimeOffset.UtcNow.ToString("O"));
                    }
                }
                catch (Exception ex)
'@
Replace-Exact -Path $programPath -Old $programAnchor -New $programReplacement

Write-Host 'RST-001 source patch applied.'
