from pathlib import Path

chrome = Path('src/GPTDeskTop/Services/ChromeDevToolsService.cs')
source = chrome.read_text(encoding='utf-8')

old = '''            if (current.Count != before.Count)\n            {\n                VerifiedSendDiagnostics.Record("FailedClosed", "unexpected-user-turn-change", submitAttempts);\n                return false;\n            }'''
new = '''            // Before any physical submit an unexpected user turn is a real conflict. After an\n            // unacknowledged submit, however, a reload/rebind can expose a partially hydrated turn\n            // list. Let reconciliation require stable evidence instead of failing on one DOM read.\n            if (current.Count != before.Count && unacknowledgedSubmitSinceUtc is null)\n            {\n                VerifiedSendDiagnostics.Record("FailedClosed", "unexpected-user-turn-change", submitAttempts);\n                return false;\n            }'''
if source.count(old) != 1:
    raise SystemExit(f'outer mismatch guard: expected one match, found {source.count(old)}')
source = source.replace(old, new, 1)

old = '''        if (receiptBeforeRefresh.Count != baselineUserTurnCount)\n            return UnacknowledgedSubmitReconciliationResult.Ambiguous;\n\n        if (!await RefreshStuckComposerAsync(tab, cancellationToken))'''
new = '''        // Do not classify a single pre-refresh count mismatch as a conflict. Target replacement\n        // can briefly expose a partial turn list; the post-refresh loop below requires two stable\n        // identical unexpected reads before returning Ambiguous.\n\n        if (!await RefreshStuckComposerAsync(tab, cancellationToken))'''
if source.count(old) != 1:
    raise SystemExit(f'pre-refresh mismatch guard: expected one match, found {source.count(old)}')
source = source.replace(old, new, 1)
chrome.write_text(source, encoding='utf-8')

test = Path('tests/GPTDeskTop.RuntimeTests/VerifiedSendTaskCancellationSelfHealRegressionTests.cs')
t = test.read_text(encoding='utf-8')
needle = '''        Assert.Contains("while (DateTimeOffset.UtcNow < deadline || unacknowledgedSubmitSinceUtc is not null)", method, StringComparison.Ordinal);\n        Assert.Contains("UnacknowledgedSubmitReconciliationResult.TransientInterruption", method, StringComparison.Ordinal);'''
replacement = '''        Assert.Contains("while (DateTimeOffset.UtcNow < deadline || unacknowledgedSubmitSinceUtc is not null)", method, StringComparison.Ordinal);\n        Assert.Contains("current.Count != before.Count && unacknowledgedSubmitSinceUtc is null", method, StringComparison.Ordinal);\n        Assert.Contains("UnacknowledgedSubmitReconciliationResult.TransientInterruption", method, StringComparison.Ordinal);'''
if t.count(needle) != 1:
    raise SystemExit(f'test outer guard insertion: expected one match, found {t.count(needle)}')
t = t.replace(needle, replacement, 1)
needle = '''        Assert.Contains("PostRefreshUserTurnObservation.Hydrating", method, StringComparison.Ordinal);\n        Assert.Contains("stableUnexpectedReads >= 2", method, StringComparison.Ordinal);'''
replacement = '''        Assert.Contains("PostRefreshUserTurnObservation.Hydrating", method, StringComparison.Ordinal);\n        Assert.Contains("stableUnexpectedReads >= 2", method, StringComparison.Ordinal);\n        Assert.DoesNotContain("if (receiptBeforeRefresh.Count != baselineUserTurnCount)", method, StringComparison.Ordinal);'''
if t.count(needle) != 1:
    raise SystemExit(f'test pre-refresh insertion: expected one match, found {t.count(needle)}')
t = t.replace(needle, replacement, 1)
test.write_text(t, encoding='utf-8')
