from pathlib import Path

p = Path("scripts/apply-field-delivery-timeout-recovery.py")
text = p.read_text(encoding="utf-8")

helper_anchor = '''def replace_once(path: str, old: str, new: str) -> None:\n    p = Path(path)\n    text = p.read_text(encoding="utf-8")\n    count = text.count(old)\n    if count != 1:\n        raise SystemExit(f"Expected exactly one match in {path}, found {count}: {old[:100]!r}")\n    p.write_text(text.replace(old, new, 1), encoding="utf-8")\n'''
helper_replacement = helper_anchor + '''\n\ndef replace_first(path: str, old: str, new: str) -> None:\n    p = Path(path)\n    text = p.read_text(encoding="utf-8")\n    if old not in text:\n        raise SystemExit(f"Expected at least one match in {path}: {old[:100]!r}")\n    p.write_text(text.replace(old, new, 1), encoding="utf-8")\n'''
if helper_anchor not in text:
    raise SystemExit("replace_once helper anchor was not found")
text = text.replace(helper_anchor, helper_replacement, 1)

ambiguous_call = '''replace_once(\n    monitor,\n    ''' + "'''" + '''                        if (committedRecoveryTab is null)\\n                        {\\n                            lastHandledText = string.Empty;''' + "'''" + ''','''
if ambiguous_call not in text:
    raise SystemExit("ambiguous timeout-recovery patch call was not found")
text = text.replace(ambiguous_call, ambiguous_call.replace("replace_once(", "replace_first(", 1), 1)

p.write_text(text, encoding="utf-8")
print("FIELDERR timeout-recovery selector disambiguated to the first (delivery-timeout) block.")
