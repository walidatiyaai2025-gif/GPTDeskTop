from pathlib import Path

path = Path('docs/DEVELOPMENT_STATUS.md')
text = path.read_text(encoding='utf-8')
old = '- Configuration Backup Round-Trip Safety is implemented for Issue #77: portable export now refuses legacy invalid monitor identities and canonical-equivalent duplicate ownership instead of producing a backup the current importer cannot restore, canonicalizes every valid exported conversation URL, preserves atomic destination replacement semantics on validation failure, and updates import confirmation copy to describe canonical conversation-identity matching.'
new = '- Configuration Backup Round-Trip Safety is implemented in PR #78: portable export refuses legacy invalid monitor identities and canonical-equivalent duplicate ownership instead of producing a backup the current importer cannot restore, canonicalizes every valid exported conversation URL, preserves an existing destination and removes temporary output on validation failure, and keeps import confirmation copy aligned with canonical conversation-identity matching.'
if old not in text:
    raise RuntimeError('Issue #77 status bullet not found')
text = text.replace(old, new, 1)

validation = '- **Post-1.8 Configuration Backup Round-Trip Safety validation:** PR #78 implementation head `c5981010fb47d5c5191503145b7d8532817c13fe` passed Build GPTDeskTop #401, QA Release x64 #189, QA Hidden Chrome CDP #171, QA Crash Process Recovery #179, QA No-Response Watchdog #165, Development Delivery Receipts #279, Development Task Recovery #275 and Development Message Reload #118. Focused validation passed 267/267 tests; Build #401 repeated the runtime suite and all lifecycle/delivery/rebinding/CDP/crash invariants plus application, Setup, helper and rotation-safety validation. Deterministic coverage verifies invalid legacy identity refusal, logical duplicate refusal, canonical URL export, preservation of an existing destination on failed export, no temporary-file residue and canonical import operator copy.'
heading = '## Next Executable Task'
pos = text.index(heading)
if validation not in text:
    text = text[:pos] + validation + '\n\n' + text[pos:]

old_next = 'Issue #77 is the current tracked post-1.8 task: make configuration backup export round-trippable by refusing invalid or duplicate stable-conversation ownership before file creation, canonicalizing valid exported conversation URLs, and keeping operator copy aligned with canonical import matching. Merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.'
new_next = 'After configuration-backup round-trip safety, continue post-1.8 maintenance by auditing the next concrete operator/runtime safety or supportability gap, create a tracked issue for the selected gap, implement it on an isolated branch, and merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.'
if old_next not in text:
    raise RuntimeError('Issue #77 Next Executable Task text not found')
text = text.replace(old_next, new_next, 1)
path.write_text(text, encoding='utf-8')
print('Issue #77 status reconciled.')
