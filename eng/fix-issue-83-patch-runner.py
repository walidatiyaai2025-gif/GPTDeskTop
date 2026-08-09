from pathlib import Path

path = Path('eng/apply-issue-83.py')
text = path.read_text(encoding='utf-8')
old = "close_anchor = '\\n}\\n'\nlast = settings.rfind(close_anchor)"
new = "close_anchor = '\\n}'\nlast = settings.rfind(close_anchor)"
if old not in text:
    raise RuntimeError('Issue #83 closing anchor patch target not found')
text = text.replace(old, new, 1)
path.write_text(text, encoding='utf-8')
print('Issue #83 SettingsForm closing anchor corrected.')
