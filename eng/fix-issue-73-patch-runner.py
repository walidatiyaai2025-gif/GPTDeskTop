from pathlib import Path

path = Path("eng/apply-issue-73.py")
text = path.read_text(encoding="utf-8")
old = '''replace_once(\n    "src/GPTDeskTop/Data/LocalDatabase.cs",\n    \'\'\'                update.Parameters.AddWithValue("$url", targetUrl);\n\'\'\',\n    \'\'\'                update.Parameters.AddWithValue("$url", canonicalTargetUrl);\n\'\'\')\n'''
new = '''text_path = Path("src/GPTDeskTop/Data/LocalDatabase.cs")\ntext = text_path.read_text(encoding="utf-8")\nneedle = \'update.Parameters.AddWithValue("$url", targetUrl);\'\nif text.count(needle) != 2:\n    raise RuntimeError(f"expected repair + handoff target URL assignments, found {text.count(needle)}")\ntext = text.replace(needle, \'update.Parameters.AddWithValue("$url", canonicalTargetUrl);\', 1)\ntext_path.write_text(text, encoding="utf-8")\n'''
if text.count(old) != 1:
    raise RuntimeError(f"expected problematic patch block once, found {text.count(old)}")
path.write_text(text.replace(old, new), encoding="utf-8")
print("Issue #73 patch sequencing corrected.")
