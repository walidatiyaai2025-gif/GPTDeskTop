using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class SavedMonitorLivePresentationTests
{
    [Fact]
    public void GeneratingActivityMapsToFixedPrivacySafeText()
    {
        var at = DateTimeOffset.UtcNow;
        const string secret = "TOP-SECRET-PROMPT";
        var live = SavedMonitorLivePresentation.FromActivity(
            $"Waiting | Assistant response still growing | response-still-growing | {secret} | https://chatgpt.com/c/private",
            at);

        Assert.NotNull(live);
        Assert.Equal("🟢 Generating", live!.Status);
        Assert.Equal("ChatGPT is generating the monitored response.", live.Reason);
        Assert.DoesNotContain(secret, live.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("http", live.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartedActivityNeverLeaksTitleOrAutoReply()
    {
        var live = SavedMonitorLivePresentation.FromActivity(
            "Started: PRIVATE TITLE | Reply: PRIVATE AUTO REPLY | https://chatgpt.com/c/private",
            DateTimeOffset.UtcNow);

        Assert.NotNull(live);
        Assert.Equal("🟢 Monitoring", live!.Status);
        Assert.Equal("Monitor is running and observing its ChatGPT conversation.", live.Reason);
        Assert.DoesNotContain("PRIVATE", live.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("Reply", live.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Sending", 2, "🟢 Sending", "attempt 2")]
    [InlineData("Accepted", 1, "🟢 Delivered", "receipt confirmed")]
    [InlineData("ReconcileRequired", 1, "🟢 Reconciling", "duplicate sending blocked")]
    [InlineData("Completed", 1, "🟢 Monitoring", "reconciled")]
    public void DeliveryPhasesMapToOperatorFacingLiveState(
        string phase,
        int attempts,
        string expectedStatus,
        string expectedReasonFragment)
    {
        var live = SavedMonitorLivePresentation.FromDelivery(phase, attempts, DateTimeOffset.UtcNow);

        Assert.Equal(expectedStatus, live.Status);
        Assert.Contains(expectedReasonFragment, live.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FreshLiveRuntimeOverridesTransientSavedTabRecovery()
    {
        var now = DateTimeOffset.UtcNow;
        var baseline = new SavedMonitorRowHealth(
            false,
            "🔴 Recovering",
            "Saved conversation tab is not currently available.");
        var live = SavedMonitorLivePresentation.FromDelivery("Sending", 1, now);

        var effective = SavedMonitorLivePresentation.Overlay(
            baseline,
            workerRunning: true,
            live,
            now);

        Assert.True(effective.IsHealthy);
        Assert.Equal("🟢 Sending", effective.Status);
        Assert.DoesNotContain("not currently available", effective.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("🔴 Connection", "Chrome/CDP unavailable: connection refused")]
    [InlineData("🔴 Recovery", "ChatGPT reports an error: Something went wrong")]
    [InlineData("🔴 Blocked", "Another saved monitor owns this conversation.")]
    [InlineData("🔴 Invalid", "Saved URL is not a ChatGPT conversation URL.")]
    public void LiveRuntimeNeverHidesTrueSafetyFailures(string status, string reason)
    {
        var now = DateTimeOffset.UtcNow;
        var baseline = new SavedMonitorRowHealth(false, status, reason);
        var live = SavedMonitorLivePresentation.FromDelivery("Sending", 1, now);

        var effective = SavedMonitorLivePresentation.Overlay(baseline, true, live, now);

        Assert.Same(baseline, effective);
        Assert.False(effective.IsHealthy);
        Assert.Equal(status, effective.Status);
    }

    [Fact]
    public void StoppedWorkerOrStaleEvidenceCannotPaintRecoveryGreen()
    {
        var now = DateTimeOffset.UtcNow;
        var baseline = new SavedMonitorRowHealth(false, "🔴 Recovering", "Saved conversation tab is not currently available.");
        var fresh = SavedMonitorLivePresentation.FromDelivery("Accepted", 1, now);
        var stale = fresh with { ObservedAtUtc = now - SavedMonitorLivePresentation.FreshnessWindow - TimeSpan.FromSeconds(1) };

        Assert.Same(baseline, SavedMonitorLivePresentation.Overlay(baseline, false, fresh, now));
        Assert.Same(baseline, SavedMonitorLivePresentation.Overlay(baseline, true, stale, now));
    }

    [Fact]
    public void StoreKeepsMonitorStatesIsolatedAndRejectsOlderOverwrite()
    {
        var store = new SavedMonitorLiveStateStore();
        var now = DateTimeOffset.UtcNow;
        var monitor2 = SavedMonitorLivePresentation.FromDelivery("Accepted", 1, now);
        var monitor3 = SavedMonitorLivePresentation.FromDelivery("ReconcileRequired", 2, now.AddSeconds(1));

        store.Observe(2, monitor2);
        store.Observe(3, monitor3);
        store.Observe(2, SavedMonitorLivePresentation.FromDelivery("Sending", 1, now.AddSeconds(-5)));

        Assert.Equal("🟢 Delivered", store.Get(2)!.Status);
        Assert.Equal("🟢 Reconciling", store.Get(3)!.Status);
        Assert.Equal(new long[] { 2, 3 }, store.MonitorIds);
    }

    [Fact]
    public void StorePrunesOnlyExpiredMonitorState()
    {
        var store = new SavedMonitorLiveStateStore();
        var now = DateTimeOffset.UtcNow;
        store.Observe(2, SavedMonitorLivePresentation.FromDelivery("Accepted", 1, now));
        store.Observe(3, SavedMonitorLivePresentation.FromDelivery(
            "Accepted",
            1,
            now - SavedMonitorLivePresentation.FreshnessWindow - TimeSpan.FromSeconds(1)));

        store.Prune(now, SavedMonitorLivePresentation.FreshnessWindow);

        Assert.NotNull(store.Get(2));
        Assert.Null(store.Get(3));
    }
}
