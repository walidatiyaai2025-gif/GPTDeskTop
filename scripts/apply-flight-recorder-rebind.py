from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"Expected one marker in {path}, found {count}: {old[:100]!r}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8", newline="\n")

trace = ROOT / "src/GPTDeskTop/Services/MonitorDiagnosticTraceService.cs"
replace_once(
    trace,
    "            var currentTargetId = tab?.Id;\n            if (!_lastTargetIds.TryGetValue(saved.Id, out var previousTargetId)\n                || !string.Equals(previousTargetId, currentTargetId, StringComparison.Ordinal))\n            {\n                _lastTargetIds[saved.Id] = currentTargetId;",
    "            var currentTargetFingerprint = tab is null ? null : $\"{tab.Id}|{tab.Url}\";\n            if (!_lastTargetIds.TryGetValue(saved.Id, out var previousTargetFingerprint)\n                || !string.Equals(previousTargetFingerprint, currentTargetFingerprint, StringComparison.Ordinal))\n            {\n                _lastTargetIds[saved.Id] = currentTargetFingerprint;",
)

chrome = ROOT / "src/GPTDeskTop/Services/ChromeDevToolsService.cs"
replace_once(
    chrome,
    "    private async Task TryRefreshTabBindingAsync(ChromeTab tab, CancellationToken cancellationToken)\n    {\n        try\n        {\n            var tabs = await GetTabsAsync(cancellationToken).ConfigureAwait(false);\n            var current = MonitorDeliveryRecoveryPolicy.FindBestBinding(tabs, tab);\n            if (current is not null)\n                RebindTab(tab, current);\n        }\n        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)\n        {\n            throw;\n        }\n        catch (Exception ex) when (IsRecoverableMonitorTransportException(ex))\n        {\n        }\n    }",
    "    private async Task TryRefreshTabBindingAsync(ChromeTab tab, CancellationToken cancellationToken)\n    {\n        RuntimeFlightRecorder.Record(\"Browser\", \"BindingRefreshRequested\", \"started\", \"stable-target-search\", tabId: tab.Id, conversationRef: tab.Url);\n        try\n        {\n            var tabs = await GetTabsAsync(cancellationToken).ConfigureAwait(false);\n            var current = MonitorDeliveryRecoveryPolicy.FindBestBinding(tabs, tab);\n            if (current is not null)\n            {\n                RebindTab(tab, current);\n                RuntimeFlightRecorder.Record(\"Browser\", \"BindingRefreshed\", \"bound\", \"target-rebound\", tabId: tab.Id, conversationRef: tab.Url);\n            }\n            else\n            {\n                RuntimeFlightRecorder.Record(\"Browser\", \"BindingRefreshed\", \"missing\", \"target-not-found\", tabId: tab.Id, conversationRef: tab.Url);\n            }\n        }\n        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)\n        {\n            RuntimeFlightRecorder.Record(\"Browser\", \"BindingRefreshCompleted\", \"cancelled\", \"operator-or-shutdown\", tabId: tab.Id, conversationRef: tab.Url);\n            throw;\n        }\n        catch (Exception ex) when (IsRecoverableMonitorTransportException(ex))\n        {\n            RuntimeFlightRecorder.Record(\"Browser\", \"BindingRefreshCompleted\", \"failed\", ex.GetType().Name, tabId: tab.Id, conversationRef: tab.Url);\n        }\n    }",
)

for relative in ["scripts/apply-flight-recorder-rebind.py", ".github/workflows/flightrec-rebind.yml"]:
    target = ROOT / relative
    if target.exists():
        target.unlink()
