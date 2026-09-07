from pathlib import Path

path = Path("scripts/v2033_hardening_patch.py")
text = path.read_text(encoding="utf-8")

launch_block = 'replace_block(\n    chrome,\n    "    public Process LaunchMonitorChrome(string? startUrl = null)\\n",'
recover_block = 'replace_block(\n    chrome,\n    "    private async Task<bool> RecoverMonitorTabAsync(ChromeTab tab, CancellationToken cancellationToken)\\n",'
start = text.index(launch_block)
end = text.index(recover_block, start)
text = text[:start] + text[end:]

# The QA hardening must constrain Monitor Only recovery methods, not ban the legacy
# launcher API used by other product workflows. Locate this temporary global check by
# its stable PowerShell prefix instead of brittle escaped-regex text.
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

path.write_text(text, encoding="utf-8", newline="\n")
print("Scoped v2.0.33 hardening to Monitor Only passive/recovery paths.")
