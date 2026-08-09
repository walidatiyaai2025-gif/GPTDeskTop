from pathlib import Path

path = Path('docs/DEVELOPMENT_STATUS.md')
text = path.read_text(encoding='utf-8')
old = '- Configuration Import Runtime Safety Boundary is implemented for Issue #83: Settings receives a live running-monitor predicate from MainForm without depending on monitor internals, blocks configuration import while any monitor worker is active, rechecks immediately before transactional apply, never auto-stops monitoring, and reloads MainForm monitor/default-setting presentation from SQLite after a successful Settings/import dialog.'
new = '- Configuration Import Runtime Safety Boundary is implemented in PR #84: Settings receives a live running-monitor predicate from MainForm without depending on monitor internals, blocks configuration import while any monitor worker is active, rechecks immediately before transactional apply, never auto-stops monitoring, and reloads MainForm monitor/default-setting presentation from SQLite after a successful Settings/import dialog. Normal Settings save and configuration export remain available while monitors are running.'
if old not in text:
    raise RuntimeError('Issue #83 status bullet not found')
text = text.replace(old, new, 1)

validation = '- **Post-1.8 Configuration Import Runtime Safety Boundary validation:** PR #84 implementation head `f1291bdf128ae4dab330db391c0f94e55194296f` passed Build GPTDeskTop #422, QA Release x64 #210, QA Hidden Chrome CDP #192, QA Crash Process Recovery #200, QA No-Response Watchdog #186, Development Delivery Receipts #300, Development Task Recovery #296 and Development Message Reload #136. Focused validation passed 278/278 tests; Build #422 repeated the runtime suite and all lifecycle/delivery/rebinding/CDP/crash invariants plus application, Setup, helper and rotation-safety validation. Source-contract/UI coverage verifies the import guard runs before file selection and again immediately before transactional apply, Settings stays decoupled from monitor internals, monitoring is never auto-stopped, and MainForm reloads persisted presentation after Settings/import completion.'
heading = '## Next Executable Task'
pos = text.index(heading)
if validation not in text:
    text = text[:pos] + validation + '\n\n' + text[pos:]

old_next = 'Issue #83 is the current tracked post-1.8 task: block configuration import while monitor runtime is active so transactional SQLite changes cannot coexist with stale in-memory worker configuration. Merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.'
new_next = 'After the configuration-import runtime safety boundary, continue post-1.8 maintenance by auditing the next concrete operator/runtime safety or supportability gap, create a tracked issue for the selected gap, implement it on an isolated branch, and merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.'
if old_next not in text:
    raise RuntimeError('Issue #83 Next Executable Task text not found')
text = text.replace(old_next, new_next, 1)
path.write_text(text, encoding='utf-8')
print('Issue #83 status reconciled.')
