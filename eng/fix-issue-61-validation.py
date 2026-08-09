from pathlib import Path

path = Path("src/GPTDeskTop/UI/MonitorIdentityRepairForm.cs")
text = path.read_text(encoding="utf-8")
old = 'AccessibleName = "Monitor conversation blocker repair";'
new = 'AccessibleName = "Monitor conversation identity repair";'
if text.count(old) != 1:
    raise RuntimeError(f"expected exactly one accessibility-name match, found {text.count(old)}")
path.write_text(text.replace(old, new), encoding="utf-8")
print("Issue #61 accessibility compatibility correction applied.")
