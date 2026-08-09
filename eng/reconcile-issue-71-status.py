from pathlib import Path

path = Path("docs/DEVELOPMENT_STATUS.md")
text = path.read_text(encoding="utf-8")

anchor = "- Transactional Intentional Conversation Handoff is merged on `main` (`a4f1ae5ea4e7db378d63f7f0b4049b8c8991ab6a`): message-count rotation, context-limit rotation and delivery-timeout recovery now re-enumerate the verified new-chat TargetId until ChatGPT exposes the final stable `/c/{conversation-id}` URL, then atomically claim that unowned conversation from the expected old saved conversation through one immediate SQLite transaction. The transaction updates the same Monitor ID, increments RotationCount only for rotation paths, and commits rotation/success receipts together. Conflicts or unresolved stable URLs leave the old authoritative tab open and close the unclaimed new tab.\n"
addition = anchor + "- Config-Only Existing Monitor Settings Save — PR #72: operator edits to an existing monitor now update only editable configuration columns. Runtime identity and state (`TabId`, `Title`, `Url`, `RotationCount`, Monitor ID/history identity) remain database-owned, so a stale settings dialog cannot roll back a concurrent repair, recovery or intentional handoff. The UI reloads the monitor after save and handles deletion while the dialog was open without recreating the row.\n"
if text.count(anchor) != 1:
    raise RuntimeError(f"expected transactional handoff anchor once, found {text.count(anchor)}")
text = text.replace(anchor, addition, 1)

baseline = "## Release-Readiness Baseline\n\n"
receipt = "- **Post-1.8 Config-Only Existing Monitor Settings validation:** PR #72 head `2a5496a18393f184b3ce52e1aea3b0f56e8efd5d` passed Build GPTDeskTop #380, QA Release x64 #168, QA Hidden Chrome CDP #150, QA Crash Process Recovery #158, QA No-Response Watchdog #144, Development Delivery Receipts #258, Development Task Recovery #254 and Development Message Reload #100. Focused validation passed 252/252 tests, including stale-settings-after-handoff, stale-settings-after-repair, deletion and source-contract coverage.\n"
if text.count(baseline) != 1:
    raise RuntimeError(f"expected release baseline marker once, found {text.count(baseline)}")
text = text.replace(baseline, baseline + receipt, 1)

old_next = "Issue #71 is the current tracked post-1.8 task: make existing-monitor operator settings saves configuration-only so a stale settings dialog can never overwrite a newer runtime conversation binding, title, target ID or RotationCount produced by repair, recovery or intentional handoff. Merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.\n"
new_next = "After config-only existing-monitor settings persistence, continue post-1.8 maintenance by auditing the next concrete operator/runtime safety or supportability gap, create a tracked issue for the selected gap, implement it on an isolated branch, and merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.\n"
if text.count(old_next) != 1:
    raise RuntimeError(f"expected issue #71 next-task paragraph once, found {text.count(old_next)}")
text = text.replace(old_next, new_next, 1)

path.write_text(text, encoding="utf-8")
print("Issue #71 development status reconciled.")
