from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected one match, found {count}")
    p.write_text(text.replace(old, new), encoding="utf-8")

# Preserve the pre-existing case-insensitive conversation URL contract after normalization.
replace_once(
    "src/GPTDeskTop/Services/ChatGptConversationIdentity.cs",
    '''        return string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);\n''',
    '''        return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);\n''')
replace_once(
    "src/GPTDeskTop/Services/MonitorConversationOwnership.cs",
    '''                     .GroupBy(monitor => ChatGptConversationIdentity.Normalize(monitor.Url), StringComparer.Ordinal)\n''',
    '''                     .GroupBy(monitor => ChatGptConversationIdentity.Normalize(monitor.Url), StringComparer.OrdinalIgnoreCase)\n''')

# Source-contract tests now assert the stronger shared identity primitive rather than old raw comparisons.
replace_once(
    "tests/GPTDeskTop.RuntimeTests/MonitorRegistrationBoundaryRegressionTests.cs",
    '''        Assert.Contains("string.Equals(m.Url, tab.Url, StringComparison.OrdinalIgnoreCase)", source, StringComparison.Ordinal);\n''',
    '''        Assert.Contains("ChatGptConversationIdentity.IsSame(m.Url, tab.Url)", source, StringComparison.Ordinal);\n''')
replace_once(
    "tests/GPTDeskTop.RuntimeTests/MonitorIdentityRepairUiRegressionTests.cs",
    '''        Assert.Contains("StringComparison.OrdinalIgnoreCase", source, StringComparison.Ordinal);\n''',
    '''        Assert.Contains("ChatGptConversationIdentity.IsSame(saved.Url, targetTab.Url)", source, StringComparison.Ordinal);\n''')
replace_once(
    "tests/GPTDeskTop.RuntimeTests/TransactionalConversationRebindTests.cs",
    '''        Assert.Contains("targetOwner.Transaction = transaction", database, StringComparison.Ordinal);\n''',
    '''        Assert.Contains("FindLogicalConversationOwnerIdAsync", database, StringComparison.Ordinal);\n''')

# Keep new tests analyzer-clean.
replace_once(
    "tests/GPTDeskTop.RuntimeTests/CanonicalConversationOwnershipTests.cs",
    '''            var savedFresh = Assert.Single((await db.GetSavedMonitorsAsync()).Where(m => m.Id == freshResult.MonitorId));\n''',
    '''            var savedFresh = Assert.Single(await db.GetSavedMonitorsAsync(), m => m.Id == freshResult.MonitorId);\n''')

print("Issue #73 compatibility/test corrections applied.")
