from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8").replace("\r\n", "\n")


def write(path: Path, text: str) -> None:
    with path.open("w", encoding="utf-8", newline="\n") as handle:
        handle.write(text)


def replace_once(text: str, old: str, new: str, description: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"Expected exactly one {description} match, found {count}.")
    return text.replace(old, new, 1)


chrome_path = ROOT / "src" / "GPTDeskTop" / "Services" / "ChromeDevToolsService.cs"
chrome = read(chrome_path)
if "post-submit-reconciliation-time-budget-exhausted" not in chrome:
    chrome = replace_once(
        chrome,
        "        var receiptGrace = TimeSpan.FromSeconds(3);\n        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);",
        "        var receiptGrace = TimeSpan.FromSeconds(3);\n        var maxUnacknowledgedReconciliation = TimeSpan.FromSeconds(90);\n        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);",
        "verified-send timing header",
    )
    chrome = replace_once(
        chrome,
        "        // Before a physical submit the normal deadline still applies. Once a submit has\n"
        "        // an unknown outcome, elapsed time alone is never permission to abandon reconciliation: keep\n"
        "        // observing/rebinding until receipt, stable absence, a genuine conflict/error, or cancellation.\n"
        "        while (DateTimeOffset.UtcNow < deadline || unacknowledgedSubmitSinceUtc is not null)",
        "        // Before a physical submit the normal deadline still applies. Once a submit has an\n"
        "        // unknown outcome, reconciliation gets a bounded liveness budget. Budget exhaustion\n"
        "        // fails closed and never authorizes another physical submit.\n"
        "        while (DateTimeOffset.UtcNow < deadline\n"
        "               || (unacknowledgedSubmitSinceUtc is not null\n"
        "                   && DateTimeOffset.UtcNow - unacknowledgedSubmitSinceUtc.Value < maxUnacknowledgedReconciliation))",
        "unbounded verified-send loop",
    )
    chrome = replace_once(
        chrome,
        "                VerifiedSendDiagnostics.Record(\"Reconciling\", \"receipt-not-observed-after-grace\", submitAttempts);\n"
        "                var reconciliation = await ReconcileUnacknowledgedSubmitAsync(\n"
        "                    tab,\n"
        "                    expected,\n"
        "                    before.Count,\n"
        "                    cancellationToken);",
        "                VerifiedSendDiagnostics.Record(\"Reconciling\", \"receipt-not-observed-after-grace\", submitAttempts);\n"
        "                var reconciliationRemaining = maxUnacknowledgedReconciliation\n"
        "                    - (DateTimeOffset.UtcNow - unacknowledgedSubmitSinceUtc.Value);\n"
        "                if (reconciliationRemaining <= TimeSpan.Zero)\n"
        "                {\n"
        "                    VerifiedSendDiagnostics.Record(\"FailedClosed\", \"post-submit-reconciliation-time-budget-exhausted\", submitAttempts);\n"
        "                    return false;\n"
        "                }\n\n"
        "                using var reconciliationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);\n"
        "                reconciliationCts.CancelAfter(reconciliationRemaining);\n"
        "                UnacknowledgedSubmitReconciliationResult reconciliation;\n"
        "                try\n"
        "                {\n"
        "                    reconciliation = await ReconcileUnacknowledgedSubmitAsync(\n"
        "                        tab,\n"
        "                        expected,\n"
        "                        before.Count,\n"
        "                        reconciliationCts.Token);\n"
        "                }\n"
        "                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && reconciliationCts.IsCancellationRequested)\n"
        "                {\n"
        "                    VerifiedSendDiagnostics.Record(\"FailedClosed\", \"post-submit-reconciliation-time-budget-exhausted\", submitAttempts);\n"
        "                    return false;\n"
        "                }",
        "unbounded reconciliation call",
    )
    chrome = replace_once(
        chrome,
        "        VerifiedSendDiagnostics.Record(\"FailedClosed\", \"verified-send-deadline-without-receipt\", submitAttempts);\n        return false;",
        "        VerifiedSendDiagnostics.Record(\n"
        "            \"FailedClosed\",\n"
        "            unacknowledgedSubmitSinceUtc is null\n"
        "                ? \"verified-send-deadline-without-receipt\"\n"
        "                : \"post-submit-reconciliation-time-budget-exhausted\",\n"
        "            submitAttempts);\n"
        "        return false;",
        "verified-send terminal diagnostic",
    )
    write(chrome_path, chrome)

outbound_path = ROOT / "src" / "GPTDeskTop" / "Runtime" / "OutboundDeliveryCoordinator.cs"
outbound = read(outbound_path)
if "previous.Phase == OutboundDeliveryPhase.ReconcileRequired" not in outbound:
    outbound = replace_once(
        outbound,
        "    private static bool IsDuplicateInFlight(OutboundDeliverySnapshot previous, string conversationKey, string fingerprint)\n"
        "        => previous.ConversationKey == conversationKey\n"
        "           && previous.MessageFingerprint == fingerprint\n"
        "           && previous.Phase is OutboundDeliveryPhase.Sending or OutboundDeliveryPhase.ReconcileRequired\n"
        "           && DateTimeOffset.UtcNow - previous.UpdatedUtc < DuplicateWindow;",
        "    private static bool IsDuplicateInFlight(OutboundDeliverySnapshot previous, string conversationKey, string fingerprint)\n"
        "        => previous.ConversationKey == conversationKey\n"
        "           && previous.MessageFingerprint == fingerprint\n"
        "           && (previous.Phase == OutboundDeliveryPhase.ReconcileRequired\n"
        "               || (previous.Phase == OutboundDeliveryPhase.Sending\n"
        "                   && DateTimeOffset.UtcNow - previous.UpdatedUtc < DuplicateWindow));",
        "expiring reconcile-required duplicate guard",
    )
    write(outbound_path, outbound)

# Distinguish this field build from the earlier v2.0.0 installer.
for relative in [
    Path("src/GPTDeskTop/GPTDeskTop.csproj"),
    Path("src/GPTDeskTop.Setup/GPTDeskTop.Setup.csproj"),
]:
    path = ROOT / relative
    text = read(path)
    text = text.replace("<Version>2.0.0</Version>", "<Version>2.0.1</Version>")
    text = text.replace("<AssemblyVersion>2.0.0.0</AssemblyVersion>", "<AssemblyVersion>2.0.1.0</AssemblyVersion>")
    text = text.replace("<FileVersion>2.0.0.0</FileVersion>", "<FileVersion>2.0.1.0</FileVersion>")
    text = text.replace("GPTDeskTop Setup v2.0.0", "GPTDeskTop Setup v2.0.1")
    write(path, text)

# Add a source-level regression lock. Existing runtime tests use this pattern for safety invariants.
test_path = ROOT / "tests" / "GPTDeskTop.RuntimeTests" / "VerifiedSendReconciliationLivenessRegressionTests.cs"
if not test_path.exists():
    write(
        test_path,
        '''namespace GPTDeskTop.RuntimeTests;\n\npublic sealed class VerifiedSendReconciliationLivenessRegressionTests\n{\n    private static string RepositoryPath(params string[] segments)\n        => Path.GetFullPath(Path.Combine(\n            AppContext.BaseDirectory,\n            "..", "..", "..", "..", "..",\n            Path.Combine(segments)));\n\n    [Fact]\n    public void UnacknowledgedSubmitReconciliationHasHardLivenessBudget()\n    {\n        var source = File.ReadAllText(RepositoryPath(\n            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));\n\n        Assert.Contains("maxUnacknowledgedReconciliation = TimeSpan.FromSeconds(90)", source, StringComparison.Ordinal);\n        Assert.Contains("post-submit-reconciliation-time-budget-exhausted", source, StringComparison.Ordinal);\n        Assert.Contains("reconciliationCts.CancelAfter(reconciliationRemaining)", source, StringComparison.Ordinal);\n        Assert.DoesNotContain(\n            "while (DateTimeOffset.UtcNow < deadline || unacknowledgedSubmitSinceUtc is not null)",\n            source,\n            StringComparison.Ordinal);\n    }\n\n    [Fact]\n    public void ReconcileRequiredDuplicateGuardDoesNotExpireAfterTwoMinutes()\n    {\n        var source = File.ReadAllText(RepositoryPath(\n            "src", "GPTDeskTop", "Runtime", "OutboundDeliveryCoordinator.cs"));\n\n        Assert.Contains("previous.Phase == OutboundDeliveryPhase.ReconcileRequired", source, StringComparison.Ordinal);\n        Assert.Contains("previous.Phase == OutboundDeliveryPhase.Sending", source, StringComparison.Ordinal);\n        Assert.Contains("DateTimeOffset.UtcNow - previous.UpdatedUtc < DuplicateWindow", source, StringComparison.Ordinal);\n    }\n}\n''',
    )

print("Runtime stall hotfix source is applied.")
