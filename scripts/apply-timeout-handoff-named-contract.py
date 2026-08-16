from pathlib import Path

root = Path(__file__).resolve().parents[1]
path = root / "src/GPTDeskTop/Services/ChatGptMonitorService.cs"
text = path.read_text(encoding="utf-8")
old = '''                        await ConversationHandoffCheckpointStore.PrepareAsync(_database, monitor, oldTab, "DeliveryTimeout", recoveryMessage, text, "RecoveredToNewChat", "RecoverySent", "RecoveryHandoffCommitDeferred", incrementRotationCount: false, recordRotation: false, cancellationToken);'''
new = '''                        await ConversationHandoffCheckpointStore.PrepareAsync(\n                            database: _database,\n                            monitor: monitor,\n                            sourceTab: oldTab,\n                            rotationTrigger: "DeliveryTimeout",\n                            startMessage: recoveryMessage,\n                            triggerResponse: text,\n                            successStatus: "RecoveredToNewChat",\n                            outboundStatus: "RecoverySent",\n                            conflictStatus: "RecoveryHandoffCommitDeferred",\n                            incrementRotationCount: false,\n                            recordRotation: false,\n                            cancellationToken: cancellationToken);'''
if text.count(old) != 1:
    raise SystemExit(f"expected one timeout PrepareAsync anchor, found {text.count(old)}")
path.write_text(text.replace(old, new, 1), encoding="utf-8")
print("timeout handoff named regression contract applied")
