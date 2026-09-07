from pathlib import Path

path = Path("scripts/v2033_hardening_patch.py")
text = path.read_text(encoding="utf-8")

launch_block = 'replace_block(\n    chrome,\n    "    public Process LaunchMonitorChrome(string? startUrl = null)\\n",'
recover_block = 'replace_block(\n    chrome,\n    "    private async Task<bool> RecoverMonitorTabAsync(ChromeTab tab, CancellationToken cancellationToken)\\n",'
start = text.index(launch_block)
end = text.index(recover_block, start)
text = text[:start] + text[end:]

# Scope the QA rule to Monitor Only recovery methods, preserving the legacy launcher API
# required by other product workflows.
global_gate_start = "          if ($chrome -match 'Process"
start = text.index(global_gate_start)
end_marker = "          $recoverStart ="
end = text.index(end_marker, start)
text = text[:start] + text[end:]

text = text.replace(
    '        Assert.DoesNotContain("Process.Start(", chrome, StringComparison.Ordinal);\n',
    '',
)
text = text.replace(
    '        Assert.DoesNotContain("LaunchMonitorChrome(", chrome, StringComparison.Ordinal);\n',
    '',
)

# Avoid nested quote escaping in the generated C# source assertion. The assertion only
# needs to prove the recovery-state assignment exists; exact quoted values are covered by
# the production source and inspector assertions.
assertion_prefix = '        Assert.Contains("_lastRecovery = attempt > 1 ? '
start = text.index(assertion_prefix)
end = text.index('\n', start)
text = (
    text[:start]
    + '        Assert.Contains("_lastRecovery = attempt > 1 ?", runner, StringComparison.Ordinal);'
    + text[end:]
)

path.write_text(text, encoding="utf-8", newline="\n")
print("Scoped v2.0.33 hardening and normalized generated regression test.")
