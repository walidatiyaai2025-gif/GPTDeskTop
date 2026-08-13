using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

/// <summary>
/// Turns Saved Monitors into an always-live operator health board. Healthy monitors are green;
/// every non-healthy state is red and carries a human-readable reason in the grid.
/// </summary>
internal static class SavedMonitorHealthGridExperience
{
    private const string ReasonColumnName = "MonitorHealthReason";
    private const int HealthScanIntervalMs = 2500;
    private static readonly ConditionalWeakTable<MainForm, Installation> Installations = new();

    [ModuleInitializer]
    internal static void Initialize()
        => Application.Idle += InstallOnOpenMainForms;

    private static void InstallOnOpenMainForms(object? sender, EventArgs e)
    {
        foreach (Form form in Application.OpenForms)
        {
            if (form is MainForm main && !main.IsDisposed && !main.Disposing)
                TryInstall(main);
        }
    }

    internal static bool TryInstall(MainForm form)
    {
        if (Installations.TryGetValue(form, out _))
            return true;

        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var monitorField = typeof(MainForm).GetField("_monitor", flags);
        var chromeField = typeof(MainForm).GetField("_chrome", flags);
        var gridField = typeof(MainForm).GetField("_monitorsGrid", flags);

        if (monitorField?.GetValue(form) is not ChatGptMonitorService monitor ||
            chromeField?.GetValue(form) is not ChromeDevToolsService chrome ||
            gridField?.GetValue(form) is not DataGridView grid)
            return false;

        var installation = new Installation(form, monitor, chrome, grid);
        Installations.Add(form, installation);
        installation.Attach();
        return true;
    }

    private sealed class Installation : IDisposable
    {
        private readonly MainForm _form;
        private readonly ChatGptMonitorService _monitor;
        private readonly ChromeDevToolsService _chrome;
        private readonly DataGridView _grid;
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = HealthScanIntervalMs };
        private readonly CancellationTokenSource _lifetime = new();
        private readonly ConcurrentDictionary<long, SavedMonitorRowHealth> _states = new();
        private readonly ConcurrentDictionary<long, FailureNote> _recentFailures = new();
        private int _scanInProgress;
        private bool _disposed;

        internal Installation(
            MainForm form,
            ChatGptMonitorService monitor,
            ChromeDevToolsService chrome,
            DataGridView grid)
        {
            _form = form;
            _monitor = monitor;
            _chrome = chrome;
            _grid = grid;
        }

        internal void Attach()
        {
            EnsureReasonColumn();
            _grid.CellFormatting += OnCellFormatting;
            _grid.DataBindingComplete += OnDataBindingComplete;
            _monitor.Activity += OnMonitorActivity;
            _monitor.RunningStateChanged += OnRunningStateChanged;
            _timer.Tick += OnTimerTick;
            _form.Shown += OnFormShown;
            _form.FormClosing += OnFormClosing;
            _form.FormClosed += OnFormClosed;

            if (_form.Visible)
            {
                _timer.Start();
                QueueScan();
            }
        }

        private void EnsureReasonColumn()
        {
            if (_grid.Columns.Contains(ReasonColumnName))
                return;

            var urlColumn = _grid.Columns
                .Cast<DataGridViewColumn>()
                .FirstOrDefault(column => string.Equals(
                    column.DataPropertyName,
                    nameof(SavedMonitor.Url),
                    StringComparison.Ordinal));
            if (urlColumn is not null)
            {
                urlColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                urlColumn.FillWeight = 55F;
                urlColumn.MinimumWidth = 220;
            }

            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = ReasonColumnName,
                HeaderText = "Reason",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 45F,
                MinimumWidth = 200,
                ToolTipText = "Current reason why this monitor is healthy or not healthy."
            });
        }

        private void OnFormShown(object? sender, EventArgs e)
        {
            _timer.Start();
            QueueScan();
        }

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
            => Stop();

        private void OnFormClosed(object? sender, FormClosedEventArgs e)
            => Dispose();

        private void OnTimerTick(object? sender, EventArgs e)
            => QueueScan();

        private void OnDataBindingComplete(object? sender, DataGridViewBindingCompleteEventArgs e)
            => ApplyCurrentStates();

        private void OnRunningStateChanged()
            => QueueScan();

        private void OnMonitorActivity(long monitorId, string message)
        {
            if (LooksLikeFailure(message))
                _recentFailures[monitorId] = new FailureNote(CleanActivityReason(message), DateTimeOffset.UtcNow);
            else if (LooksLikeHealthyTransition(message))
                _recentFailures.TryRemove(monitorId, out _);

            if (LooksLikeFailure(message) || LooksLikeHealthyTransition(message))
                QueueScan();
        }

        private void QueueScan()
        {
            if (_disposed || _form.IsDisposed || _form.Disposing)
                return;

            if (_form.InvokeRequired)
            {
                try { _form.BeginInvoke(new Action(QueueScan)); }
                catch (InvalidOperationException) { }
                return;
            }

            if (Interlocked.CompareExchange(ref _scanInProgress, 1, 0) != 0)
                return;

            _ = ScanAsync();
        }

        private async Task ScanAsync()
        {
            try
            {
                var monitors = _grid.Rows
                    .Cast<DataGridViewRow>()
                    .Select(row => row.DataBoundItem)
                    .OfType<SavedMonitor>()
                    .DistinctBy(monitor => monitor.Id)
                    .ToList();

                if (monitors.Count == 0)
                {
                    _states.Clear();
                    return;
                }

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));

                List<ChromeTab>? tabs = null;
                string? globalProbeError = null;
                try
                {
                    tabs = await _chrome.GetTabsAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    globalProbeError = $"Chrome/CDP unavailable: {SavedMonitorHealthPresentation.NormalizeReason(ex.Message)}";
                }

                var duplicateIds = MonitorConversationOwnership.FindDuplicateMonitorIds(monitors);
                var nextStates = new Dictionary<long, SavedMonitorRowHealth>();

                foreach (var monitor in monitors)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    var workerRunning = _monitor.IsMonitorRunning(monitor.Id);
                    var tab = tabs is null ? null : ResolveConversationTab(monitor, tabs);
                    ChatPageState? pageState = null;
                    var probeError = globalProbeError;

                    if (workerRunning && probeError is null && tab is not null)
                    {
                        try
                        {
                            pageState = await _chrome.GetChatStateAsync(tab, timeout.Token);
                        }
                        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                        {
                            return;
                        }
                        catch (Exception ex)
                        {
                            probeError = $"ChatGPT health check failed: {SavedMonitorHealthPresentation.NormalizeReason(ex.Message)}";
                        }
                    }

                    var state = SavedMonitorHealthPresentation.Evaluate(
                        monitor,
                        workerRunning,
                        duplicateIds.Contains(monitor.Id),
                        tab is not null,
                        pageState,
                        probeError,
                        workerRunning ? null : GetRecentFailureReason(monitor.Id));
                    nextStates[monitor.Id] = state;
                }

                foreach (var state in nextStates)
                    _states[state.Key] = state.Value;
                foreach (var monitorId in _states.Keys.Except(nextStates.Keys).ToArray())
                    _states.TryRemove(monitorId, out _);

                ApplyCurrentStates();
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                // Normal application shutdown.
            }
            catch (OperationCanceledException)
            {
                // A bounded health scan may time out during Chrome recovery. Keep the last known
                // colors until the next scan rather than flickering the grid to an unknown state.
            }
            catch (Exception ex)
            {
                ExceptionLogService.Log(ex, "SavedMonitorHealthGridExperience.Scan");
            }
            finally
            {
                Interlocked.Exchange(ref _scanInProgress, 0);
            }
        }

        private static ChromeTab? ResolveConversationTab(SavedMonitor monitor, IReadOnlyList<ChromeTab> tabs)
        {
            var exact = tabs.FirstOrDefault(tab =>
                !string.IsNullOrWhiteSpace(monitor.TabId) &&
                string.Equals(tab.Id, monitor.TabId, StringComparison.Ordinal) &&
                RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url));
            if (exact is not null && ChatGptConversationIdentity.IsSame(exact.Url, monitor.Url))
                return exact;

            return tabs.FirstOrDefault(tab =>
                RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url) &&
                ChatGptConversationIdentity.IsSame(tab.Url, monitor.Url));
        }

        private string? GetRecentFailureReason(long monitorId)
        {
            if (!_recentFailures.TryGetValue(monitorId, out var note))
                return null;
            if (DateTimeOffset.UtcNow - note.At > TimeSpan.FromMinutes(15))
            {
                _recentFailures.TryRemove(monitorId, out _);
                return null;
            }
            return note.Message;
        }

        private void ApplyCurrentStates()
        {
            if (_disposed || _grid.IsDisposed)
                return;

            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.DataBoundItem is not SavedMonitor monitor ||
                    !_states.TryGetValue(monitor.Id, out var health))
                    continue;

                var background = health.IsHealthy ? FluentTheme.SuccessSubtle : FluentTheme.DangerSubtle;
                var foreground = health.IsHealthy ? FluentTheme.Success : FluentTheme.Danger;
                row.DefaultCellStyle.BackColor = background;
                row.DefaultCellStyle.ForeColor = foreground;
                row.DefaultCellStyle.SelectionBackColor = background;
                row.DefaultCellStyle.SelectionForeColor = foreground;
            }

            _grid.Invalidate();
        }

        private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.RowIndex >= _grid.Rows.Count)
                return;
            if (_grid.Rows[e.RowIndex].DataBoundItem is not SavedMonitor monitor ||
                !_states.TryGetValue(monitor.Id, out var health))
                return;

            var column = _grid.Columns[e.ColumnIndex];
            if (string.Equals(column.Name, ReasonColumnName, StringComparison.Ordinal))
            {
                e.Value = health.Reason;
                e.FormattingApplied = true;
            }
            else if (string.Equals(column.DataPropertyName, nameof(SavedMonitor.RuntimeStatus), StringComparison.Ordinal))
            {
                e.Value = health.Status;
                e.CellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
                e.FormattingApplied = true;
            }

            e.CellStyle.BackColor = health.IsHealthy ? FluentTheme.SuccessSubtle : FluentTheme.DangerSubtle;
            e.CellStyle.ForeColor = health.IsHealthy ? FluentTheme.Success : FluentTheme.Danger;
            e.CellStyle.SelectionBackColor = e.CellStyle.BackColor;
            e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
        }

        private static bool LooksLikeFailure(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            string[] markers =
            [
                "failed", "failure", "error", "exception", "unavailable", "disconnect",
                "deferred", "disappeared", "not open", "not accepted", "not verified",
                "stopped by exception", "remains stopped", "blocked", "could not", "cannot"
            ];
            return markers.Any(marker => message.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }

        private static bool LooksLikeHealthyTransition(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            string[] markers =
            [
                "Started:", "recovery complete", "is now monitored", "monitoring the new ChatGPT conversation",
                "same Monitor ID is now bound", "Verified message accepted"
            ];
            return markers.Any(marker => message.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }

        private static string CleanActivityReason(string message)
        {
            var cleaned = SavedMonitorHealthPresentation.NormalizeReason(message);
            var marker = cleaned.IndexOf(':');
            if (cleaned.StartsWith("Monitor #", StringComparison.OrdinalIgnoreCase) && marker >= 0 && marker + 1 < cleaned.Length)
                cleaned = cleaned[(marker + 1)..].Trim();
            return cleaned;
        }

        private void Stop()
        {
            if (_disposed) return;
            _timer.Stop();
            if (!_lifetime.IsCancellationRequested)
                _lifetime.Cancel();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _timer.Dispose();
            _grid.CellFormatting -= OnCellFormatting;
            _grid.DataBindingComplete -= OnDataBindingComplete;
            _monitor.Activity -= OnMonitorActivity;
            _monitor.RunningStateChanged -= OnRunningStateChanged;
            _form.Shown -= OnFormShown;
            _form.FormClosing -= OnFormClosing;
            _form.FormClosed -= OnFormClosed;
            if (!_lifetime.IsCancellationRequested)
                _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }

    private sealed record FailureNote(string Message, DateTimeOffset At);
}
