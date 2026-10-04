namespace GPTDeskTop.Services;

public enum VerifiedDeliveryOutcome
{
    NotSubmitted,
    Delivered,
    Ambiguous
}

/// <summary>Per-operation state, never inferred from diagnostics or a shared last-result field.</summary>
internal sealed class VerifiedDeliveryAttempt
{
    internal bool SubmitMayHaveOccurred { get; private set; }
    internal void RecordDispatch(bool mayHaveOccurred) => SubmitMayHaveOccurred = mayHaveOccurred;
    internal VerifiedDeliveryOutcome Complete(bool receiptConfirmed) => receiptConfirmed
        ? VerifiedDeliveryOutcome.Delivered
        : SubmitMayHaveOccurred ? VerifiedDeliveryOutcome.Ambiguous : VerifiedDeliveryOutcome.NotSubmitted;
}
