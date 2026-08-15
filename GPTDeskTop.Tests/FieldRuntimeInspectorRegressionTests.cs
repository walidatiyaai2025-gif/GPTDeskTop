using GPTDeskTop.Runtime;
using Xunit;

namespace GPTDeskTop.Tests;

public sealed class FieldRuntimeInspectorRegressionTests
{
    [Fact]
    public void Accepted_outbound_message_never_returns_to_send()
    {
        var sending = OutboundDeliveryPolicy.BeforeComposerMutation(13, "continue project", "c/test", null);
        var accepted = OutboundDeliveryPolicy.DomAccepted(sending);
        var reconciled = OutboundDeliveryPolicy.BeforeComposerMutation(13, "continue project", "c/test", accepted);
        Assert.Equal(OutboundDeliveryPhase.ReconcileRequired, reconciled.Phase);
        Assert.Equal("accepted-message-must-not-be-resent", reconciled.LastReason);
        Assert.False(reconciled.MayMutateComposer);
    }

    [Fact]
    public void Receipt_timeout_is_observe_not_resend()
    {
        var accepted = OutboundDeliveryPolicy.DomAccepted(OutboundDeliveryPolicy.BeforeComposerMutation(14, "next", "c/test", null));
        var timeout = OutboundDeliveryPolicy.ReceiptTimeout(accepted);
        Assert.Equal(OutboundDeliveryPhase.ReconcileRequired, timeout.Phase);
        Assert.False(timeout.MayMutateComposer);
    }

    [Fact]
    public void Fingerprint_is_stable_across_whitespace()
    {
        Assert.Equal(OutboundMessageIdentity.Fingerprint("a  b\n c"), OutboundMessageIdentity.Fingerprint("a b c"));
    }

    [Fact]
    public void Runtime_build_snapshot_exposes_actual_executable()
    {
        var snapshot = RuntimeInspector.CaptureBuild();
        Assert.False(string.IsNullOrWhiteSpace(snapshot.ExecutablePath));
        Assert.True(snapshot.ProcessId > 0);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Runtime));
    }
}
