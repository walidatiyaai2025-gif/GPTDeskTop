from pathlib import Path

path = Path("docs/DEVELOPMENT_STATUS.md")
text = path.read_text(encoding="utf-8")

anchor = "- Crash Recovery Stable-Conversation Binding is merged on `main` (`cb09ac8fe8e1a189dbb9b721f5519bb314290f7d`): recovery now applies the same saved-conversation identity invariant to persisted target reuse, normalized URL fallback and newly created tabs. Recovery never mutates the persisted conversation URL, writes only guarded runtime target metadata before any send/start, and records `CrashRecoverySavedConversationChanged` while keeping the incident pending if its saved URL snapshot becomes stale. Redirected or reused targets for another conversation are rejected before delivery.\n"
addition = anchor + "- Transactional Intentional Conversation Handoff — PR #70: message-count rotation, context-limit rotation and delivery-timeout recovery now re-enumerate the verified new-chat TargetId until ChatGPT exposes the final stable `/c/{conversation-id}` URL, then atomically claim that unowned conversation from the expected old saved conversation through one immediate SQLite transaction. The transaction updates the same Monitor ID, increments RotationCount only for rotation paths, and commits rotation/success receipts together. Conflicts or unresolved stable URLs leave the old authoritative tab open and close the unclaimed new tab.\n"
if text.count(anchor) != 1:
    raise RuntimeError(f"expected crash-recovery anchor once, found {text.count(anchor)}")
text = text.replace(anchor, addition, 1)

baseline = "## Release-Readiness Baseline\n\n"
receipt = "- **Post-1.8 Transactional Intentional Conversation Handoff validation:** PR #70 head `e4ddcc0e8b2264f17056be28cacfd8521f1d6496` passed Build GPTDeskTop #372, QA Release x64 #160, QA Hidden Chrome CDP #142, QA Crash Process Recovery #150, QA No-Response Watchdog #136, Development Delivery Receipts #250, Development Task Recovery #246 and Development Message Reload #92. Focused real-SQLite validation passed 248/248 tests, including handoff-vs-registration, competing handoffs, concurrent repair-vs-handoff, rotation-count semantics, atomic rotation/log receipts and timeout-recovery semantics.\n"
if text.count(baseline) != 1:
    raise RuntimeError(f"expected release baseline marker once, found {text.count(baseline)}")
text = text.replace(baseline, baseline + receipt, 1)

old_next = "Issue #69 is the current tracked post-1.8 task: make every intentional conversation-changing runtime handoff transactional, resolve the final stable `/c/{conversation-id}` URL after verified new-chat delivery, atomically claim that unowned target from the expected old conversation, and commit rotation/recovery receipts without broad `SaveMonitorAsync` identity mutation. Merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.\n"
new_next = "After transactional intentional conversation handoff, continue post-1.8 maintenance by auditing the next concrete operator/runtime safety or supportability gap, create a tracked issue for the selected gap, implement it on an isolated branch, and merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.\n"
if text.count(old_next) != 1:
    raise RuntimeError(f"expected issue #69 next-task paragraph once, found {text.count(old_next)}")
text = text.replace(old_next, new_next, 1)

path.write_text(text, encoding="utf-8")
print("Issue #69 development status reconciled.")
