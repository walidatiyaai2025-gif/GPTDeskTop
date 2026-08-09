from pathlib import Path

path = Path('docs/DEVELOPMENT_STATUS.md')
text = path.read_text(encoding='utf-8')
lines = text.splitlines()

canonical_bullet = "- Canonical Stable-Conversation Ownership is implemented in PR #74: stable ChatGPT conversation ownership now uses the shared canonical identity contract across duplicate detection, duplicate-safe registration, transactional repair, intentional handoff and guarded runtime-target refresh. Equivalent stable URL spellings such as trailing-slash variants and case-equivalent URI forms resolve to the same logical owner, newly persisted stable bindings use the canonical form, and legacy non-canonical rows are left intact but surface as duplicate ownership blockers rather than bypassing safety."
if canonical_bullet not in lines:
    anchor = next(i for i, line in enumerate(lines) if line.startswith('- Config-Only Existing Monitor Settings Save is merged on `main`'))
    lines.insert(anchor + 1, canonical_bullet)

validation = "- **Post-1.8 Canonical Stable-Conversation Ownership validation:** PR #74 implementation head `fe7ba3130266d24f2fe022ec3c07ed2baa653021` passed Build GPTDeskTop #387, QA Release x64 #175, QA Hidden Chrome CDP #157, QA Crash Process Recovery #165, QA No-Response Watchdog #151, Development Delivery Receipts #265, Development Task Recovery #261 and Development Message Reload #106. Focused validation passed 257/257 tests; Build #387 repeated the runtime suite and all lifecycle/delivery/rebinding/CDP/crash invariants plus application, Setup, helper and rotation-safety validation. Deterministic real-SQLite coverage verifies canonical duplicate registration, duplicate-owner visibility, repair/handoff ownership conflicts and guarded runtime-target updates while preserving legacy rows without destructive migration."
next_heading = lines.index('## Next Executable Task')
if validation not in lines:
    lines[next_heading:next_heading] = [validation, '']
    next_heading += 2

replacement = "After canonical stable-conversation ownership, continue post-1.8 maintenance by auditing the next concrete operator/runtime safety or supportability gap, create a tracked issue for the selected gap, implement it on an isolated branch, and merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly."
i = next_heading + 1
while i < len(lines) and not lines[i].strip():
    i += 1
if i >= len(lines):
    raise RuntimeError('Next executable task body was not found')
lines[i] = replacement

path.write_text('\n'.join(lines) + '\n', encoding='utf-8')
print('Issue #73 status reconciled.')
