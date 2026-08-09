from pathlib import Path

path = Path('docs/DEVELOPMENT_STATUS.md')
text = path.read_text(encoding='utf-8')
old = '- Consistent Configuration Backup Snapshot is implemented for Issue #81: backup collection now reads its allowlisted settings and saved monitors through one SQLite connection and one read transaction, so the generated portable document cannot mix settings and monitor state from separate database snapshots. Existing single-key/settings/monitor read APIs remain available for unrelated callers.'
new = '- Consistent Configuration Backup Snapshot is implemented in PR #82: backup collection reads allowlisted settings and saved monitors through one private-cache SQLite connection and one explicitly deferred read transaction, so the generated portable document cannot mix settings and monitor state from separate database snapshots and can still observe the last committed WAL snapshot while another writer is active. Existing single-key/settings/monitor read APIs remain available for unrelated callers.'
if old not in text:
    raise RuntimeError('Issue #81 status bullet not found')
text = text.replace(old, new, 1)

validation = '- **Post-1.8 Consistent Configuration Backup Snapshot validation:** PR #82 implementation head `d0aee4a783dc7a0474ba8ffb329c39bfef246a79` passed Build GPTDeskTop #415, QA Release x64 #203, QA Hidden Chrome CDP #185, QA Crash Process Recovery #193, QA No-Response Watchdog #179, Development Delivery Receipts #293, Development Task Recovery #289 and Development Message Reload #130. Focused validation passed 274/274 tests; Build #415 repeated the runtime suite and all lifecycle/delivery/rebinding/CDP/crash invariants plus application, Setup, helper and rotation-safety validation. Real-SQLite coverage verifies a snapshot sees the old committed settings+monitor pair while a writer transaction is uncommitted and a subsequent snapshot sees the new committed pair.'
heading = '## Next Executable Task'
pos = text.index(heading)
if validation not in text:
    text = text[:pos] + validation + '\n\n' + text[pos:]

old_next = 'Issue #81 is the current tracked post-1.8 task: collect configuration backup settings and saved monitors from one SQLite read snapshot so a concurrent settings save, repair, recovery update or handoff cannot produce a mixed-time portable backup. Merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.'
new_next = 'After consistent configuration-backup snapshot collection, continue post-1.8 maintenance by auditing the next concrete operator/runtime safety or supportability gap, create a tracked issue for the selected gap, implement it on an isolated branch, and merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.'
if old_next not in text:
    raise RuntimeError('Issue #81 Next Executable Task text not found')
text = text.replace(old_next, new_next, 1)
path.write_text(text, encoding='utf-8')
print('Issue #81 status reconciled.')
