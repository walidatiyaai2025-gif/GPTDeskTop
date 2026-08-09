from pathlib import Path

path = Path('docs/DEVELOPMENT_STATUS.md')
text = path.read_text(encoding='utf-8')
old = '- Atomic Operator Settings Save is implemented for Issue #79: the Settings dialog now materializes the complete validated operator settings set and commits it through one immediate SQLite writer transaction. A failed batch rolls back every setting, concurrent batches serialize without mixed pair state, and the existing single-key `SetSettingAsync` remains available for unrelated runtime operations.'
new = '- Atomic Operator Settings Save is implemented in PR #80: the Settings dialog materializes the complete validated operator settings set and commits it through one immediate SQLite writer transaction. A failed batch rolls back every setting, concurrent batches serialize without mixed pair state, the existing single-key `SetSettingAsync` remains available for unrelated runtime operations, and the established busy-state wording/validation UX remains unchanged.'
if old not in text:
    raise RuntimeError('Issue #79 status bullet not found')
text = text.replace(old, new, 1)

validation = '- **Post-1.8 Atomic Operator Settings Save validation:** PR #80 implementation head `a37f988d65764be168181a236719b9f036f08bbb` passed Build GPTDeskTop #408, QA Release x64 #196, QA Hidden Chrome CDP #178, QA Crash Process Recovery #186, QA No-Response Watchdog #172, Development Delivery Receipts #286, Development Task Recovery #282 and Development Message Reload #124. Focused validation passed 271/271 tests; Build #408 repeated the runtime suite and all lifecycle/delivery/rebinding/CDP/crash invariants plus application, Setup, helper and rotation-safety validation. Real-SQLite coverage verifies successful batch persistence, forced mid-batch failure rollback and serialized competing batches without mixed pair state.'
heading = '## Next Executable Task'
pos = text.index(heading)
if validation not in text:
    text = text[:pos] + validation + '\n\n' + text[pos:]

old_next = 'Issue #79 is the current tracked post-1.8 task: make operator Settings save atomic so a database failure cannot leave a partially applied set of coupled settings. Merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.'
new_next = 'After atomic operator Settings persistence, continue post-1.8 maintenance by auditing the next concrete operator/runtime safety or supportability gap, create a tracked issue for the selected gap, implement it on an isolated branch, and merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.'
if old_next not in text:
    raise RuntimeError('Issue #79 Next Executable Task text not found')
text = text.replace(old_next, new_next, 1)
path.write_text(text, encoding='utf-8')
print('Issue #79 status reconciled.')
