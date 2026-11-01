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
        print("PASS — JSON compatibility, sent checkpoint resume, loop order, delay validation")
    }
}
