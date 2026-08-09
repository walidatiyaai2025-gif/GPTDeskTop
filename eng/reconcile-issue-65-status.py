from pathlib import Path

path = Path("docs/DEVELOPMENT_STATUS.md")
text = path.read_text(encoding="utf-8")

anchor = "- Transactional Conversation Rebind Ownership Guard is merged on `main` (`eaabca8c8563b39aa0763546c33ca07b3904f18f`): invalid-identity and duplicate-owner repair now converge on `RebindMonitorConversationIfAvailableAsync`, which acquires a non-deferred SQLite writer transaction, revalidates the source snapshot, rechecks duplicate-source ownership when required, verifies the target remains unowned with `COLLATE NOCASE`, updates only the runtime conversation binding fields and writes the remediation diagnostic in the same transaction. This closes the repair-vs-registration and repair-vs-repair TOCTOU path without changing Monitor ID, history identity, operator configuration, enabled state, rotation state or crash-recovery state.\n"
addition = anchor + "- Stable Conversation Target Revalidation is implemented in PR #66: persisted Chrome target IDs are treated only as runtime locators and are accepted only when the live target still represents the saved stable ChatGPT conversation. Reused/stale target IDs fall back to the exact normalized saved conversation URL, operator Start/Start All share the same safe resolver as development delivery, ordinary Start can update only TabId/Title/UpdatedAt when the persisted URL still matches its snapshot, and the runtime service rejects a monitor/tab conversation mismatch before worker creation.\n"
if text.count(anchor) != 1:
    raise RuntimeError(f"expected transactional-rebind anchor once, found {text.count(anchor)}")
text = text.replace(anchor, addition, 1)

baseline = "## Release-Readiness Baseline\n\n"
receipt = "- **Post-1.8 Stable Conversation Target Revalidation validation:** PR #66 head `d465cd0bcd5e52b37f6aac2d5ed3133a22e3828b` passed Build GPTDeskTop #353, QA Release x64 #141, QA Hidden Chrome CDP #123, QA Crash Process Recovery #131, QA No-Response Watchdog #117, Development Delivery Receipts #231, Development Task Recovery #227 and Development Message Reload #74. Focused validation passed 239/239 tests, including stale target-ID reuse rejection, exact saved-conversation URL fallback, missing-conversation handling, runtime-target-only persistence and concurrent conversation-change protection.\n"
if text.count(baseline) != 1:
    raise RuntimeError(f"expected release baseline marker once, found {text.count(baseline)}")
text = text.replace(baseline, baseline + receipt, 1)

old_next = "Issue #65 is the current tracked post-1.8 task: reject stale/reused Chrome target IDs unless the live target still represents the saved stable ChatGPT conversation, use exact saved-conversation URL fallback for recreated targets, and ensure ordinary Start can update runtime target metadata without ever changing the persisted conversation identity. Merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.\n"
new_next = "After stable-conversation target revalidation, continue post-1.8 maintenance by auditing the next concrete operator/runtime safety or supportability gap, create a tracked issue for the selected gap, implement it on an isolated branch, and merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.\n"
if text.count(old_next) != 1:
    raise RuntimeError(f"expected issue #65 next-task paragraph once, found {text.count(old_next)}")
text = text.replace(old_next, new_next, 1)

path.write_text(text, encoding="utf-8")
print("Issue #65 development status reconciled.")
