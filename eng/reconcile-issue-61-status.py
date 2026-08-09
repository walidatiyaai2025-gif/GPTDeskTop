from pathlib import Path

path = Path("docs/DEVELOPMENT_STATUS.md")
text = path.read_text(encoding="utf-8")

old_boundary = """- Operator Duplicate Ownership Runtime Boundary is merged on `main` (`229d0da9d5cc7b401fa7f8ecb045cabe93d6c293`): direct operator monitor start now refuses duplicate owners before worker creation, records `MonitorStartDuplicateConversationOwnership`, Runtime Health reports duplicate-owner counts as a degraded blocker, PendingRetry is disabled while duplicates remain, and privacy-safe Support Diagnostics exports only the aggregate duplicate-owner count with no monitor/conversation identity.\n\n## Release-Readiness Baseline\n"""
new_boundary = """- Operator Duplicate Ownership Runtime Boundary is merged on `main` (`229d0da9d5cc7b401fa7f8ecb045cabe93d6c293`): direct operator monitor start now refuses duplicate owners before worker creation, records `MonitorStartDuplicateConversationOwnership`, Runtime Health reports duplicate-owner counts as a degraded blocker, PendingRetry is disabled while duplicates remain, and privacy-safe Support Diagnostics exports only the aggregate duplicate-owner count with no monitor/conversation identity.\n- Safe Duplicate Ownership Remediation is implemented in PR #62: a guarded duplicate-owner-only rebind path moves exactly one duplicate owner to a different currently open unowned stable ChatGPT conversation while preserving the same Monitor ID, history association, automation settings, rotation configuration/count and crash-recovery pending state. Runtime Health Repair now handles invalid identities or duplicate owners, the Repair dialog exposes only safe unowned stable targets, and `MonitorDuplicateConversationOwnershipRebound` provides explicit remediation telemetry without clearing recovery state.\n\n## Release-Readiness Baseline\n"""
if text.count(old_boundary) != 1:
    raise RuntimeError(f"expected operator-boundary insertion point once, found {text.count(old_boundary)}")
text = text.replace(old_boundary, new_boundary)

baseline_marker = "## Release-Readiness Baseline\n\n"
receipt = "- **Post-1.8 Safe Duplicate Ownership Remediation validation:** PR #62 implementation head `b913b8e7f0999e409b5b82b2a7e497e11ecb2843` passed Build GPTDeskTop #339, QA Release x64 #127, QA Hidden Chrome CDP #109, QA Crash Process Recovery #117, QA No-Response Watchdog #103, Development Delivery Receipts #217, Development Task Recovery #213 and Development Message Reload #62. The focused runtime suite passed 229/229 tests after preserving the pre-existing repair-dialog accessibility name required by the compatibility regression contract.\n"
if text.count(baseline_marker) != 1:
    raise RuntimeError(f"expected release-readiness marker once, found {text.count(baseline_marker)}")
text = text.replace(baseline_marker, baseline_marker + receipt, 1)

old_next = "Issue #61 is the current tracked post-1.8 task: provide a safe guided remediation path for legacy duplicate stable-conversation ownership by rebinding exactly one duplicate owner to an unowned stable conversation while preserving its Monitor ID, history, configuration, rotation state and recovery state. Merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly."
new_next = "After the safe duplicate-ownership remediation, continue post-1.8 maintenance by auditing the next concrete operator/runtime safety or supportability gap, create a tracked issue for the selected gap, implement it on an isolated branch, and merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly."
if text.count(old_next) != 1:
    raise RuntimeError(f"expected current issue #61 next-task paragraph once, found {text.count(old_next)}")
text = text.replace(old_next, new_next)

path.write_text(text, encoding="utf-8")
print("Issue #61 development status reconciled with validated receipts and next-step handoff.")
