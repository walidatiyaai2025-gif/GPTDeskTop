from pathlib import Path

path = Path("docs/DEVELOPMENT_STATUS.md")
text = path.read_text(encoding="utf-8")

anchor = "- Safe Duplicate Ownership Remediation is merged on `main` (`919147398f3b0e15b71597f7cdfb88181daea917`): a guarded duplicate-owner-only rebind path moves exactly one duplicate owner to a different currently open unowned stable ChatGPT conversation while preserving the same Monitor ID, history association, automation settings, rotation configuration/count and crash-recovery pending state. Runtime Health Repair now handles invalid identities or duplicate owners, the Repair dialog exposes only safe unowned stable targets, and `MonitorDuplicateConversationOwnershipRebound` provides explicit remediation telemetry without clearing recovery state.\n"
addition = anchor + "- Transactional Conversation Rebind Ownership Guard is implemented in PR #64: invalid-identity and duplicate-owner repair now converge on `RebindMonitorConversationIfAvailableAsync`, which acquires a non-deferred SQLite writer transaction, revalidates the source snapshot, rechecks duplicate-source ownership when required, verifies the target remains unowned with `COLLATE NOCASE`, updates only the runtime conversation binding fields and writes the remediation diagnostic in the same transaction. This closes the repair-vs-registration and repair-vs-repair TOCTOU path without changing Monitor ID, history identity, operator configuration, enabled state, rotation state or crash-recovery state.\n"
if text.count(anchor) != 1:
    raise RuntimeError(f"expected safe-remediation anchor once, found {text.count(anchor)}")
text = text.replace(anchor, addition, 1)

baseline = "## Release-Readiness Baseline\n\n"
receipt = "- **Post-1.8 Transactional Conversation Rebind validation:** PR #64 head `cf23b3f89d66780a4d7e4210cfa759d929e71ec7` passed Build GPTDeskTop #346, QA Release x64 #134, QA Hidden Chrome CDP #116, QA Crash Process Recovery #124, QA No-Response Watchdog #110, Development Delivery Receipts #224, Development Task Recovery #220 and Development Message Reload #68. Focused real-SQLite validation passed 233/233 tests, including concurrent registration-vs-repair, competing repair-vs-repair and stale-source rollback/receipt atomicity coverage.\n"
if text.count(baseline) != 1:
    raise RuntimeError(f"expected release baseline marker once, found {text.count(baseline)}")
text = text.replace(baseline, baseline + receipt, 1)

old_next = "Issue #63 is the current tracked post-1.8 task: make invalid-identity and duplicate-owner conversation rebinding ownership-safe under concurrency by using one immediate SQLite writer transaction that revalidates the source snapshot, verifies the target remains unowned, updates the existing monitor row and records the repair diagnostic atomically. Merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.\n"
new_next = "After the transactional conversation-rebind ownership guard, continue post-1.8 maintenance by auditing the next concrete operator/runtime safety or supportability gap, create a tracked issue for the selected gap, implement it on an isolated branch, and merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.\n"
if text.count(old_next) != 1:
    raise RuntimeError(f"expected issue #63 next-task paragraph once, found {text.count(old_next)}")
text = text.replace(old_next, new_next, 1)

path.write_text(text, encoding="utf-8")
print("Issue #63 development status reconciled.")
