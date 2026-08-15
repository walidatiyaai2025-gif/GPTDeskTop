from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"Expected exactly one marker in {path}, found {count}: {old[:120]!r}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8", newline="\n")


inspector = ROOT / "src/GPTDeskTop/Services/RuntimeInspectorService.cs"
replace_once(
    inspector,
    "    RuntimeInspectorUiDiagnostics UiDiagnostics,\n    IReadOnlyList<object> Ui,",
    "    RuntimeInspectorUiDiagnostics UiDiagnostics,\n    RuntimeFlightSnapshot FlightRecorder,\n    IReadOnlyList<object> Ui,",
)
replace_once(
    inspector,
    "            VisibleOverflowCount: overflows.Count,\n            VisibleOverflows: overflows.Take(MaxOverflowRows).ToArray());\n\n        var workers",
    "            VisibleOverflowCount: overflows.Count,\n            VisibleOverflows: overflows.Take(MaxOverflowRows).ToArray());\n        var flightRecorder = RuntimeFlightRecorder.Snapshot();\n\n        var workers",
)
replace_once(
    inspector,
    "            verifiedSendDiagnostics,\n            uiDiagnostics,\n            ui,",
    "            verifiedSendDiagnostics,\n            uiDiagnostics,\n            flightRecorder,\n            ui,",
)
replace_once(
    inspector,
    "        var verifiedSend = snapshot.VerifiedSendDiagnostics;\n        var ui = snapshot.UiDiagnostics;\n        return",
    "        var verifiedSend = snapshot.VerifiedSendDiagnostics;\n        var ui = snapshot.UiDiagnostics;\n        var flight = snapshot.FlightRecorder;\n        var flightMonitors = flight.MonitorCounts.Count == 0\n            ? \"none\"\n            : string.Join(\",\", flight.MonitorCounts.OrderBy(pair => pair.Key).Select(pair => $\"{pair.Key}:{pair.Value}\"));\n        return",
)
replace_once(
    inspector,
    "               $\"UI forms: {ui.FormsCaptured} | visible controls: {ui.VisibleControls} | visible overflows: {ui.VisibleOverflowCount}\\r\\n\" +",
    "               $\"Flight recorder: {flight.EventCount}/{flight.Capacity} events | seq {flight.FirstSequence}-{flight.LastSequence} | monitors {flightMonitors}\\r\\n\" +\n               $\"UI forms: {ui.FormsCaptured} | visible controls: {ui.VisibleControls} | visible overflows: {ui.VisibleOverflowCount}\\r\\n\" +",
)
replace_once(
    inspector,
    "            File.WriteAllText(Path.Combine(temp, \"runtime-inspector.json\"), ToSanitizedJson(snapshot), new UTF8Encoding(false));\n            File.WriteAllText(Path.Combine(temp, \"summary.txt\"), Summary(snapshot), new UTF8Encoding(false));",
    "            File.WriteAllText(Path.Combine(temp, \"runtime-inspector.json\"), ToSanitizedJson(snapshot), new UTF8Encoding(false));\n            File.WriteAllText(Path.Combine(temp, \"runtime-flight-recorder.json\"), JsonSerializer.Serialize(snapshot.FlightRecorder, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));\n            File.WriteAllText(Path.Combine(temp, \"summary.txt\"), Summary(snapshot), new UTF8Encoding(false));",
)

trace = ROOT / "src/GPTDeskTop/Services/MonitorDiagnosticTraceService.cs"
replace_once(
    trace,
    "    private readonly Dictionary<long, string> _lastStateFingerprints = new();\n    private readonly Task _worker;",
    "    private readonly Dictionary<long, string> _lastStateFingerprints = new();\n    private readonly Dictionary<long, string?> _lastTargetIds = new();\n    private readonly Task _worker;",
)
replace_once(
    trace,
    "            var tab = tabs.FirstOrDefault(candidate =>\n                RuntimeHealthPresentation.IsChatGptConversationUrl(candidate.Url)\n                && RuntimeHealthPresentation.IsChatGptConversationUrl(saved.Url)\n                && ChatGptConversationIdentity.IsSame(candidate.Url, saved.Url));\n\n            if (!running || tab is null)",
    "            var tab = tabs.FirstOrDefault(candidate =>\n                RuntimeHealthPresentation.IsChatGptConversationUrl(candidate.Url)\n                && RuntimeHealthPresentation.IsChatGptConversationUrl(saved.Url)\n                && ChatGptConversationIdentity.IsSame(candidate.Url, saved.Url));\n\n            var currentTargetId = tab?.Id;\n            if (!_lastTargetIds.TryGetValue(saved.Id, out var previousTargetId)\n                || !string.Equals(previousTargetId, currentTargetId, StringComparison.Ordinal))\n            {\n                _lastTargetIds[saved.Id] = currentTargetId;\n                RuntimeFlightRecorder.Record(\n                    \"Browser\",\n                    \"TargetChanged\",\n                    tab is null ? \"missing\" : \"bound\",\n                    \"monitor-target\",\n                    saved.Id,\n                    tab?.Id,\n                    tab?.Url);\n            }\n\n            if (!running || tab is null)",
)
replace_once(
    trace,
    "        foreach (var staleId in _lastStateFingerprints.Keys.Where(id => !activeIds.Contains(id)).ToArray())\n            _lastStateFingerprints.Remove(staleId);",
    "        foreach (var staleId in _lastStateFingerprints.Keys.Where(id => !activeIds.Contains(id)).ToArray())\n        {\n            _lastStateFingerprints.Remove(staleId);\n            _lastTargetIds.Remove(staleId);\n        }",
)
replace_once(
    trace,
    "        _lastStateFingerprints[record.MonitorId] = fingerprint;\n        WriteRecord(record);",
    "        _lastStateFingerprints[record.MonitorId] = fingerprint;\n        var flightReason = record.FailureType\n            ?? (record.IsGenerating == true ? \"chatgpt-generating\"\n                : record.TargetFound ? \"target-found\"\n                : \"target-missing\");\n        RuntimeFlightRecorder.Record(\n            \"Monitor\",\n            \"StateChanged\",\n            record.Running ? \"running\" : \"stopped\",\n            flightReason,\n            record.MonitorId);\n        WriteRecord(record);",
)

pool = ROOT / "src/GPTDeskTop/Services/ChromeDevToolsSessionPool.cs"
replace_once(
    pool,
    "        var session = GetOrCreateSession(tab);\n        return session.SendCommandAsync(method, parameters, cancellationToken, extractRuntimeValue);\n    }\n\n    public void Prune",
    "        RuntimeFlightRecorder.Record(\"CDP\", \"CommandRequested\", \"started\", method, tabId: tab.Id, conversationRef: tab.Url);\n        var session = GetOrCreateSession(tab);\n        return SendInstrumentedAsync(session, tab, method, parameters, cancellationToken, extractRuntimeValue);\n    }\n\n    private static async Task<JsonElement> SendInstrumentedAsync(\n        DevToolsSession session,\n        ChromeTab tab,\n        string method,\n        object parameters,\n        CancellationToken cancellationToken,\n        bool extractRuntimeValue)\n    {\n        try\n        {\n            var result = await session.SendCommandAsync(method, parameters, cancellationToken, extractRuntimeValue).ConfigureAwait(false);\n            RuntimeFlightRecorder.Record(\"CDP\", \"CommandCompleted\", \"success\", method, tabId: tab.Id, conversationRef: tab.Url);\n            return result;\n        }\n        catch (Exception ex)\n        {\n            RuntimeFlightRecorder.Record(\"CDP\", \"CommandCompleted\", \"failed\", ex.GetType().Name, tabId: tab.Id, conversationRef: tab.Url);\n            throw;\n        }\n    }\n\n    public void Prune",
)
replace_once(
    pool,
    "        DisposeSessions(stale);\n    }\n\n    public void Invalidate",
    "        DisposeSessions(stale);\n        if (stale is { Count: > 0 })\n            RuntimeFlightRecorder.Record(\"CDP\", \"SessionPruned\", \"retired\", \"target-no-longer-live\");\n    }\n\n    public void Invalidate",
)
replace_once(
    pool,
    "        stale?.Dispose();\n    }\n\n    public void Clear()",
    "        stale?.Dispose();\n        if (stale is not null)\n            RuntimeFlightRecorder.Record(\"CDP\", \"SessionInvalidated\", \"retired\", \"target-invalidated\", tabId: targetId);\n    }\n\n    public void Clear()",
)
replace_once(
    pool,
    "        DisposeSessions(sessions);\n    }\n\n    public void Dispose()",
    "        DisposeSessions(sessions);\n        if (sessions.Count > 0)\n            RuntimeFlightRecorder.Record(\"CDP\", \"SessionPoolCleared\", \"retired\", \"explicit-clear\");\n    }\n\n    public void Dispose()",
)
replace_once(
    pool,
    "        stale?.Dispose();\n        return session;\n    }\n\n    private static void DisposeSessions",
    "        stale?.Dispose();\n        RuntimeFlightRecorder.Record(\n            \"CDP\",\n            stale is null ? \"SessionCreated\" : \"SessionReplaced\",\n            \"ready\",\n            stale is null ? \"new-target-session\" : \"target-session-rebound\",\n            tabId: tab.Id,\n            conversationRef: tab.Url);\n        return session;\n    }\n\n    private static void DisposeSessions",
)

# The integration mechanism is intentionally self-cleaning: the generated commit contains only product/test changes.
for relative in ["scripts/apply-flight-recorder-integration.py", ".github/workflows/flightrec-integrate.yml"]:
    target = ROOT / relative
    if target.exists():
        target.unlink()

print("Flight recorder integration applied successfully.")
