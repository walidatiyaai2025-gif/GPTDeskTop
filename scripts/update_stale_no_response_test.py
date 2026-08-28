from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OLD_NAME = "NoResponseWatchdogCopiesConfiguredMessageAndResetsTimer"
NEW_NAME = "NoResponseWatchdogUsesPassiveWaitAndCurrentTurnErrorAuthority"

replacement = '''    [Fact]\n    public void NoResponseWatchdogUsesPassiveWaitAndCurrentTurnErrorAuthority()\n    {\n        var directory = new DirectoryInfo(AppContext.BaseDirectory);\n        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GPTDeskTop.sln")))\n            directory = directory.Parent;\n\n        Assert.NotNull(directory);\n        var root = directory!.FullName;\n        var monitorSource = File.ReadAllText(Path.Combine(root, "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));\n        var chromeSource = File.ReadAllText(Path.Combine(root, "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));\n\n        Assert.DoesNotContain("GetIntSettingAsync(\\\"NoResponseRefreshSeconds\\\"", monitorSource, StringComparison.Ordinal);\n        Assert.Contains("Passive long-response wait ON", monitorSource, StringComparison.Ordinal);\n        Assert.Contains("var isError = !state.IsGenerating && !string.IsNullOrWhiteSpace(state.ErrorText);", monitorSource, StringComparison.Ordinal);\n        Assert.Contains("const isCurrentTurnElement = element =>", chromeSource, StringComparison.Ordinal);\n        Assert.Contains("const errorText = isGenerating ? '' : findErrorText();", chromeSource, StringComparison.Ordinal);\n    }'''


def method_end(text: str, open_brace: int) -> int:
    depth = 0
    in_string = False
    verbatim = False
    escape = False
    i = open_brace
    while i < len(text):
        ch = text[i]
        if in_string:
            if verbatim:
                if ch == '"':
                    if i + 1 < len(text) and text[i + 1] == '"':
                        i += 2
                        continue
                    in_string = False
                    verbatim = False
            else:
                if escape:
                    escape = False
                elif ch == '\\\\':
                    escape = True
                elif ch == '"':
                    in_string = False
        else:
            if ch == '@' and i + 1 < len(text) and text[i + 1] == '"':
                in_string = True
                verbatim = True
                i += 2
                continue
            if ch == '"':
                in_string = True
            elif ch == '{':
                depth += 1
            elif ch == '}':
                depth -= 1
                if depth == 0:
                    return i + 1
        i += 1
    raise RuntimeError("Could not find end of stale no-response test method")

matches = []
for path in (ROOT / "tests").rglob("*.cs"):
    text = path.read_text(encoding="utf-8")
    if NEW_NAME in text:
        matches.append((path, "already-updated"))
        continue
    name_index = text.find(OLD_NAME)
    if name_index < 0:
        continue
    fact_index = text.rfind("[Fact]", 0, name_index)
    if fact_index < 0:
        raise RuntimeError(f"Found {OLD_NAME} without [Fact] in {path}")
    line_start = text.rfind("\n", 0, fact_index) + 1
    open_brace = text.find("{", name_index)
    if open_brace < 0:
        raise RuntimeError(f"Found {OLD_NAME} without method body in {path}")
    end = method_end(text, open_brace)
    updated = text[:line_start] + replacement + text[end:]
    path.write_text(updated, encoding="utf-8")
    matches.append((path, "updated"))

if not matches:
    raise RuntimeError(f"Could not locate {OLD_NAME} or {NEW_NAME} under tests/")

for path, status in matches:
    print(f"{status}: {path.relative_to(ROOT)}")
