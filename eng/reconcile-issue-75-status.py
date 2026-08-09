from pathlib import Path

path = Path('docs/DEVELOPMENT_STATUS.md')
text = path.read_text(encoding='utf-8')

old = '- Canonical Configuration Import Ownership is implemented for Issue #75: configuration backup import now validates logical conversation identities before mutation, merges against canonical local ownership while preserving an existing local runtime binding and stored URL spelling, rolls back on ambiguous legacy logical ownership, and stores canonical URLs only for genuinely new imported monitors.'
new = '- Canonical Configuration Import Ownership is implemented in PR #76: configuration backup import validates logical conversation identities before mutation, merges against canonical local ownership while preserving an existing local Monitor ID, TabId, stored URL spelling, RotationCount and history identity, rolls back the entire import on ambiguous legacy logical ownership, and stores canonical URLs only for genuinely new imported monitors. The database boundary repeats duplicate-payload validation before any settings write and uses an immediate writer transaction so import cannot race duplicate-safe registration.'
if old not in text:
    raise RuntimeError('Issue #75 status bullet not found')
text = text.replace(old, new, 1)

validation = '- **Post-1.8 Canonical Configuration Import Ownership validation:** PR #76 implementation head `87598b83c1eb21d3a07ef6d3e6ea94cd38c1816f` passed Build GPTDeskTop #394, QA Release x64 #182, QA Hidden Chrome CDP #164, QA Crash Process Recovery #172, QA No-Response Watchdog #158, Development Delivery Receipts #272, Development Task Recovery #268 and Development Message Reload #112. Focused validation passed 262/262 tests; Build #394 repeated the runtime suite and all lifecycle/delivery/rebinding/CDP/crash invariants plus application, Setup, helper and rotation-safety validation. Real-SQLite coverage verifies canonical-equivalent merge with local runtime identity preservation, full rollback on ambiguous legacy logical ownership, defensive duplicate-payload rejection before settings mutation and canonical insertion for genuinely new imported monitors.'
heading = '## Next Executable Task'
pos = text.index(heading)
if validation not in text:
    text = text[:pos] + validation + '\n\n' + text[pos:]

old_next = 'Issue #75 is the current tracked post-1.8 task: make configuration backup import use canonical stable-conversation ownership so equivalent URL spellings cannot create or hide duplicate monitor ownership. Merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.'
new_next = 'After canonical configuration-import ownership, continue post-1.8 maintenance by auditing the next concrete operator/runtime safety or supportability gap, create a tracked issue for the selected gap, implement it on an isolated branch, and merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.'
if old_next not in text:
    raise RuntimeError('Issue #75 Next Executable Task text not found')
text = text.replace(old_next, new_next, 1)

path.write_text(text, encoding='utf-8')
print('Issue #75 status reconciled.')
