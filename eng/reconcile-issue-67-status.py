from pathlib import Path

path = Path("docs/DEVELOPMENT_STATUS.md")
text = path.read_text(encoding="utf-8")

anchor = "- Stable Conversation Target Revalidation is merged on `main` (`38465831a15db88d52c2f4b3ec9e250cdef8c187`): persisted Chrome target IDs are treated only as runtime locators and are accepted only when the live target still represents the saved stable ChatGPT conversation. Reused/stale target IDs fall back to the exact normalized saved conversation URL, operator Start/Start All share the same safe resolver as development delivery, ordinary Start can update only TabId/Title/UpdatedAt when the persisted URL still matches its snapshot, and the runtime service rejects a monitor/tab conversation mismatch before worker creation.\n"
addition = anchor + "- Crash Recovery Stable-Conversation Binding — PR #68: recovery now applies the same saved-conversation identity invariant to persisted target reuse, normalized URL fallback and newly created tabs. Recovery never mutates the persisted conversation URL, writes only guarded runtime target metadata before any send/start, and records `CrashRecoverySavedConversationChanged` while keeping the incident pending if its saved URL snapshot becomes stale. Redirected or reused targets for another conversation are rejected before delivery.\n"
if text.count(anchor) != 1:
    raise RuntimeError(f"expected stable-target anchor once, found {text.count(anchor)}")
text = text.replace(anchor, addition, 1)

baseline = "## Release-Readiness Baseline\n\n"
receipt = "- **Post-1.8 Crash Recovery Stable-Conversation Binding validation:** PR #68 head `47fcc45f4fe667a4668f4e81a6d032ffd2eb8285` passed Build GPTDeskTop #363, QA Release x64 #151, QA Hidden Chrome CDP #133, QA Crash Process Recovery #141, QA No-Response Watchdog #127, Development Delivery Receipts #241, Development Task Recovery #237 and Development Message Reload #84. Focused validation passed 242/242 tests, including reused target-ID fallback, trailing-slash normalization, redirected CreateTab rejection and concurrent saved-conversation change before recovery send/start.\n"
if text.count(baseline) != 1:
    raise RuntimeError(f"expected release baseline marker once, found {text.count(baseline)}")
text = text.replace(baseline, baseline + receipt, 1)

old_next = "Issue #67 is the current tracked post-1.8 task: apply the same stable-conversation identity invariant to crash recovery, reject redirected/reused recovery targets that do not match the saved conversation, and persist only guarded runtime target metadata before any recovery send/start. Merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.\n"
new_next = "After crash-recovery stable-conversation binding, continue post-1.8 maintenance by auditing the next concrete operator/runtime safety or supportability gap, create a tracked issue for the selected gap, implement it on an isolated branch, and merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.\n"
if text.count(old_next) != 1:
    raise RuntimeError(f"expected issue #67 next-task paragraph once, found {text.count(old_next)}")
text = text.replace(old_next, new_next, 1)

path.write_text(text, encoding="utf-8")
print("Issue #67 development status reconciled.")
