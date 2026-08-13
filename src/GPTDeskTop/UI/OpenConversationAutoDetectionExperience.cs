using System.Reflection;
using System.Runtime.CompilerServices;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

/// <summary>
/// Keeps the Open ChatGPT Conversations surface synchronized with the dedicated monitor Chrome
/// without requiring the operator to press Refresh. The detector only refreshes MainForm when the
/// stable conversation target set actually changes, so idle polling does not rebind the grid,
/// disturb selection, or flood Live Activity.
/// </summary>
internal static class OpenConversationAutoDetectionExperience
{
    private const int DetectionIntervalMs = 1000;
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
        var chromeField = typeof(MainForm).GetField("_chrome", flags);
        var tabsGridField = typeof(MainForm).GetField("_tabsGrid", flags);
        var refreshMethod = typeof(MainForm).GetMethod(
            "RefreshTabsAsync",
            flags,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);

        if (chromeField?.GetValue(form) is not ChromeDevToolsService chrome ||
            tabsGridField?.GetValue(form) is not DataGridView tabsGrid ||
            refreshMethod is null)
            return false;

        var installation = new Installation(form, chrome, tabsGrid, refreshMethod);
        Installations.Add(form, installation);
        installation.Attach();
        return true;
    }

    private sealed class Installation : IDisposable
    {
        private readonly MainForm _form;
        private readonly ChromeDevToolsService _chrome;
        private readonly DataGridView _tabsGrid;
        private readonly MethodInfo _refreshMethod;
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = DetectionIntervalMs };
        private readonly CancellationTokenSource _lifetime = new();
        private string? _lastConversationSignature;
        private int _scanInProgress;
        private bool _disposed;

        internal Installation(
            MainForm form,
            ChromeDevToolsService chrome,
            DataGridView tabsGrid,
            MethodInfo refreshMethod)
        {
            _form = form;
            _chrome = chrome;
            _tabsGrid = tabsGrid;
            _refreshMethod = refreshMethod;
        }

        internal void Attach()
        {
            _timer.Tick += OnTimerTick;
            _form.Shown += OnFormShown;
            _form.FormClosing += OnFormClosing;
            _form.FormClosed += OnFormClosed;

            if (_form.Visible)
                _timer.Start();
        }

        private void OnFormShown(object? sender, EventArgs e)
            => _timer.Start();

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
            => Stop();

        private void OnFormClosed(object? sender, FormClosedEventArgs e)
            => Dispose();

        private void OnTimerTick(object? sender, EventArgs e)
        {
            if (_disposed || _form.IsDisposed || _form.Disposing)
                return;
            if (Interlocked.CompareExchange(ref _scanInProgress, 1, 0) != 0)
                return;

            _ = ScanAsync();
        }

        private async Task ScanAsync()
        {
            try
            {
                using var scanTimeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                scanTimeout.CancelAfter(TimeSpan.FromSeconds(2));

                var pages = await _chrome.GetTabsAsync(scanTimeout.Token);
                var conversations = pages
                    .Where(tab => RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url))
                    .OrderBy(tab => tab.Id, StringComparer.Ordinal)
                    .ToList();
                var signature = BuildSignature(conversations);

                // The first successful scan establishes a baseline. MainForm owns the startup load,
                // so this detector never races that initial refresh merely because the form appeared.
                if (_lastConversationSignature is null)
                {
                    _lastConversationSignature = signature;
                    return;
                }

                if (string.Equals(_lastConversationSignature, signature, StringComparison.Ordinal))
                    return;

                var selected = CaptureSelectedConversation();
                await InvokeRefreshTabsAsync();
                RestoreSelection(selected);
                _lastConversationSignature = signature;
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                // Normal form shutdown.
            }
            catch (OperationCanceledException)
            {
                // A slow/unavailable CDP endpoint is transient. Keep the last visible list and retry
                // on the next interval instead of blanking Open Conversations or generating noise.
            }
            catch (Exception ex) when (ChromeTransportFailureClassifier.IsTransient(ex))
            {
                // Browser target/session churn is expected during navigation and recovery. The next
                // scan will reconcile the grid once Chrome is readable again.
            }
            catch (Exception ex)
            {
                ExceptionLogService.Log(ex, "OpenConversationAutoDetectionExperience.Scan");
            }
            finally
            {
                Interlocked.Exchange(ref _scanInProgress, 0);
            }
        }

        private async Task InvokeRefreshTabsAsync()
        {
            if (_disposed || _form.IsDisposed || _form.Disposing)
                return;

            try
            {
                if (_refreshMethod.Invoke(_form, null) is Task refreshTask)
                    await refreshTask;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw new InvalidOperationException("Open-conversation refresh failed.", ex.InnerException);
            }
        }

        private ConversationSelection? CaptureSelectedConversation()
        {
            if (_tabsGrid.CurrentRow?.DataBoundItem is not ChromeTab tab)
                return null;
            return new ConversationSelection(tab.Id, tab.Url);
        }

        private void RestoreSelection(ConversationSelection? selected)
        {
            if (selected is null || _tabsGrid.IsDisposed || _tabsGrid.Rows.Count == 0)
                return;

            foreach (DataGridViewRow row in _tabsGrid.Rows)
            {
                if (row.DataBoundItem is not ChromeTab tab)
                    continue;
                if (!string.Equals(tab.Id, selected.TabId, StringComparison.Ordinal) &&
                    !ChatGptConversationIdentity.IsSame(tab.Url, selected.Url))
                    continue;

                _tabsGrid.ClearSelection();
                row.Selected = true;
                if (row.Cells.Count > 0)
                    _tabsGrid.CurrentCell = row.Cells[0];
                return;
            }
        }

        private static string BuildSignature(IEnumerable<ChromeTab> conversations)
            => string.Join(
                '\u001e',
                conversations.Select(tab => string.Join(
                    '\u001f',
                    tab.Id ?? string.Empty,
                    tab.Url ?? string.Empty,
                    tab.Title ?? string.Empty)));

        private void Stop()
        {
            if (_disposed)
                return;
            _timer.Stop();
            if (!_lifetime.IsCancellationRequested)
                _lifetime.Cancel();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _timer.Dispose();
            _form.Shown -= OnFormShown;
            _form.FormClosing -= OnFormClosing;
            _form.FormClosed -= OnFormClosed;
            if (!_lifetime.IsCancellationRequested)
                _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }

    private sealed record ConversationSelection(string TabId, string Url);
}
