import Foundation

@main struct CoreTests {
    static func main() throws {
        let input = """
        {"schemaVersion":1,"name":"Cross-platform", "loop":false,"defaultDelaySeconds":30,
         "messages":[{"label":"One", "text":"hello", "enabled":true,"delaySeconds":45,"sent":true},
                     {"label":"Two", "text":"world", "enabled":true,"sent":false}]}
        """
        let plan = try JSONDecoder().decode(MessagePlan.self, from: Data(input.utf8))
        try plan.validate()
        precondition(plan.nextPendingIndex() == 1, "Run-once must resume from pending item")
        let output = try JSONEncoder().encode(plan)
        let restored = try JSONDecoder().decode(MessagePlan.self, from: output)
        precondition(restored.messages[0].sent && restored.messages[1].text == "world", "Round-trip failed")
        var repeating = plan; repeating.loop = true
        precondition(repeating.nextPendingIndex(after: 1) == 0, "Loop wraps")
        repeating.defaultDelaySeconds = 2
        do { try repeating.validate(); preconditionFailure("Expected validation failure") }
        catch MonitorError.invalidPlan { }

        let file = FileManager.default.temporaryDirectory.appendingPathComponent("monitor-state-\(UUID().uuidString).json")
        defer { try? FileManager.default.removeItem(at: file) }
        var settings = LocalSettings(plan: plan)
        settings.checkpoint.pendingResponse = true
        settings.checkpoint.currentIndex = 0
        settings.checkpoint.assistantBaseline = 3
        settings.checkpoint.pendingSendIndex = 1
        settings.checkpoint.pendingSendText = "world"
        settings.checkpoint.pendingSendBaselineCount = 1
        let store = MonitorStore(fileURL: file)
        try store.save(settings)
        let relaunched = store.load()
        precondition(relaunched.plan.messages[0].sent && relaunched.plan.nextPendingIndex() == 1,
                     "Relaunch must skip the already confirmed message")
        precondition(relaunched.checkpoint.pendingResponse && relaunched.checkpoint.currentIndex == 0 &&
                     relaunched.checkpoint.assistantBaseline == 3,
                     "Relaunch must retain the response wait checkpoint")
        precondition(relaunched.checkpoint.pendingSendIndex == 1 && relaunched.checkpoint.pendingSendText == "world" &&
                     relaunched.checkpoint.pendingSendBaselineCount == 1,
                     "Relaunch must retain an uncertain-send marker for receipt reconciliation")
        print("PASS — JSON compatibility, Run Once resume, Loop order, delay validation, atomic restart persistence and no-duplicate checkpointing")
    }
}
