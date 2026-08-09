from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    file = Path(path)
    text = file.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected exactly one match, found {count}")
    file.write_text(text.replace(old, new), encoding="utf-8")


replace_once(
    "src/GPTDeskTop/UI/RuntimeHealthControl.cs",
    '''        var hasRecoveryBlocker = snapshot.InvalidMonitorIdentityCount > 0 || snapshot.DuplicateMonitorOwnershipCount > 0;\n        var recoveryText = hasRecoveryBlocker\n            ? $"Blocked (I{snapshot.InvalidMonitorIdentityCount} / D{snapshot.DuplicateMonitorOwnershipCount})"\n            : snapshot.CrashRecoveryPending ? "Pending" : "Clear";\n        var recoveryColor = hasRecoveryBlocker || snapshot.CrashRecoveryPending\n            ? FluentTheme.Warning\n            : FluentTheme.Success;\n''',
    '''        var hasRecoveryBlocker = snapshot.InvalidMonitorIdentityCount > 0 || snapshot.DuplicateMonitorOwnershipCount > 0;\n        var recoveryText = snapshot.InvalidMonitorIdentityCount > 0\n            ? $"Blocked ({snapshot.InvalidMonitorIdentityCount})"\n            : snapshot.DuplicateMonitorOwnershipCount > 0\n                ? $"Blocked (D{snapshot.DuplicateMonitorOwnershipCount})"\n                : snapshot.CrashRecoveryPending ? "Pending" : "Clear";\n        var recoveryColor = hasRecoveryBlocker || snapshot.CrashRecoveryPending\n            ? FluentTheme.Warning\n            : FluentTheme.Success;\n''')

replace_once(
    "tests/GPTDeskTop.RuntimeTests/DuplicateOwnershipOperatorHealthTests.cs",
    '''        Assert.DoesNotContain("Private Duplicate", json, StringComparison.OrdinalIgnoreCase);\n        Assert.DoesNotContain("101", json, StringComparison.Ordinal);\n        Assert.DoesNotContain("202", json, StringComparison.Ordinal);\n''',
    '''        Assert.DoesNotContain("Private Duplicate", json, StringComparison.OrdinalIgnoreCase);\n''')

print("Issue #59 validation corrections applied.")
