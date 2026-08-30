using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Runtime;

namespace GPTDeskTop.Services.DevelopmentTaskEngine;

/// <summary>
/// Observes opted-in Development Messages conversations after a verified prompt send.
/// It never advances on elapsed time: the next plan message is unlocked only after a
/// new assistant turn is non-generating and remains text-stable for the configured
/// interval. The delivery baseline is persisted in DevelopmentTaskState, so process
/// restart cannot mistake an older rendered answer for the current prompt's response.
/// </summary>
public sealed class DevelopmentTaskResponseWatcher : IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(350);
    private readonly DevelopmentTaskEngine _engine;
    private readonly LocalDatabase _database;
    private readonly SavedMonitorTabResolver _resolver;
    private readonly ChromeDevToolsService _chrome;
    private readonly TimeSpan _stableWindow;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, Candidate> _candidates = new(StringComparer.Ordinal);
    private readonly Task _worker;
    private int _disposeState;

    public DevelopmentTaskResponseWatcher(
        DevelopmentTaskEngine engine,
        LocalDatabase database,
        SavedMonitorTabResolver resolver,
        ChromeDevToolsService chrome,
        int stableResponseMilliseconds = 1200)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _chrome = chrome ?? throw new ArgumentNullException(nameof(chrome));
        _stableWindow = TimeSpan.FromMilliseconds(Math.Clamp(stableResponseMilliseconds, 250, 30000));
        _worker = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var state = _engine.State;
                if (state.Status != DevelopmentTaskEngineStatus.Working || !state.AwaitingAssistantResponse)
                {
                    _candidates.Clear();
                    await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var messageIndex = state.AwaitingResponseMessageIndex;
                var expectedIds = state.AwaitingResponseMonitorIds
                    .Where(id => !state.CompletedResponseMonitorIds.Contains(id, StringComparer.Ordinal))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                if (expectedIds.Length == 0)
                {
                    await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var monitors = await _database.GetSavedMonitorsAsync(cancellationToken).ConfigureAwait(false);
                var tabs = await _chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);

                foreach (var monitorIdText in expectedIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!long.TryParse(monitorIdText, out var monitorId))
                        continue;

                    var monitor = monitors.FirstOrDefault(item => item.Id == monitorId);
                    if (monitor is null || !monitor.Enabled)
                    {
                        _candidates.Remove(monitorIdText);
                        continue;
                    }

                    if (!await DevelopmentPlanMonitorSettings.IsEnabledAsync(
                            _database, monitor, cancellationToken).ConfigureAwait(false))
                    {
                        _candidates.Remove(monitorIdText);
                        continue;
                    }

                    if (!state.DeliveryReceipts.TryGetValue(monitorIdText, out var receipt) ||
                        receipt.MessageIndex != messageIndex)
                    {
                        _candidates.Remove(monitorIdText);
                        continue;
                    }

                    var resolution = SavedMonitorTabResolver.Resolve(monitor, tabs);
                    if (!resolution.Found || resolution.Tab is null)
                    {
                        _candidates.Remove(monitorIdText);
                        continue;
                    }

                    var page = await _chrome.GetChatStateAsync(resolution.Tab, cancellationToken).ConfigureAwait(false);
                    if (page.IsGenerating)
                    {
                        _candidates.Remove(monitorIdText);
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(page.ErrorText))
                    {
                        _candidates.Remove(monitorIdText);
                        await _engine.HandleAssistantResponseAsync(
                            monitorIdText, page.ErrorText.Trim(), true, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    var response = page.LastAssistantText?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(response) ||
                        page.AssistantCount <= receipt.AssistantCountBeforeDelivery)
                    {
                        _candidates.Remove(monitorIdText);
                        continue;
                    }

                    var responseFingerprint = DevelopmentTaskDeliveryCoordinator.Fingerprint(response);
                    if (page.AssistantCount == receipt.AssistantCountBeforeDelivery &&
                        string.Equals(responseFingerprint, receipt.AssistantFingerprintBeforeDelivery, StringComparison.Ordinal))
                    {
                        _candidates.Remove(monitorIdText);
                        continue;
                    }

                    var now = DateTimeOffset.UtcNow;
                    if (!_candidates.TryGetValue(monitorIdText, out var candidate) ||
                        candidate.MessageIndex != messageIndex ||
                        candidate.AssistantCount != page.AssistantCount ||
                        !string.Equals(candidate.Text, response, StringComparison.Ordinal))
                    {
                        _candidates[monitorIdText] = new Candidate(messageIndex, page.AssistantCount, response, now);
                        continue;
                    }

                    if (now - candidate.FirstStableAt < _stableWindow)
                        continue;

                    _candidates.Remove(monitorIdText);
                    await _engine.HandleAssistantResponseAsync(
                        monitorIdText, response, false, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // CDP/browser availability is transient. Preserve the exact plan position and
                // keep observing rather than faulting or advancing the development workflow.
                try
                {
                    await _database.AddLogAsync(
                        "System", string.Empty, ex.Message,
                        "DevelopmentResponseWatchDeferred", null,
                        string.Empty, "Development Messages", cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    // Diagnostics must never change the delivery semantics.
                }
            }

            try
            {
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0) return;
        _cts.Cancel();
        try { await _worker.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _cts.Dispose();
    }

    private sealed record Candidate(
        int MessageIndex,
        int AssistantCount,
        string Text,
        DateTimeOffset FirstStableAt);
}
