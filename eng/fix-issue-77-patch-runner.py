from pathlib import Path

path = Path('eng/apply-issue-77.py')
text = path.read_text(encoding='utf-8')
text = text.replace("anchor = '## Import behavior\\n'", "anchor = '## Restore / import\\n'", 1)
text = text.replace("raise RuntimeError('CONFIGURATION_BACKUP import behavior anchor not found')", "raise RuntimeError('CONFIGURATION_BACKUP restore/import anchor not found')", 1)
old = "docs = docs.replace('Exact conversation URL matches', 'Canonical conversation identity matches')"
new = "\n".join([
    "docs = docs.replace('- a backup monitor whose conversation URL exactly matches one local monitor updates only operator-controlled monitor configuration;', '- a backup monitor whose canonical conversation identity matches one local monitor updates only operator-controlled monitor configuration while preserving the local stored URL spelling;')",
    "docs = docs.replace('- if more than one local monitor has the same exact URL being imported, the import is considered ambiguous and the entire transaction is rolled back.', '- if more than one local monitor owns the same logical conversation identity being imported, the import is considered ambiguous and the entire transaction is rolled back.')"
])
if old not in text:
    raise RuntimeError('Issue #77 docs replacement statement not found')
text = text.replace(old, new, 1)
path.write_text(text, encoding='utf-8')
print('Issue #77 patch runner corrected.')
