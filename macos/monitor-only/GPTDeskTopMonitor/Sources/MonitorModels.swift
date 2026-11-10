import Foundation

struct MessageStep: Codable, Identifiable, Equatable {
    var id = UUID()
    var label: String = ""
    var text: String = ""
    var enabled: Bool = true
    var delaySeconds: Int? = nil
    var sent: Bool = false

    enum CodingKeys: String, CodingKey { case label, text, enabled, delaySeconds, sent }
    init(label: String = "", text: String = "", enabled: Bool = true, delaySeconds: Int? = nil, sent: Bool = false) {
        self.label = label; self.text = text; self.enabled = enabled
        self.delaySeconds = delaySeconds; self.sent = sent
    }
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        label = try c.decodeIfPresent(String.self, forKey: .label) ?? ""
        text = try c.decode(String.self, forKey: .text)
        enabled = try c.decodeIfPresent(Bool.self, forKey: .enabled) ?? true
        delaySeconds = try c.decodeIfPresent(Int.self, forKey: .delaySeconds)
        sent = try c.decodeIfPresent(Bool.self, forKey: .sent) ?? false
    }
}

struct MessagePlan: Codable, Equatable {
    var schemaVersion = 1
    var name = "Mac Monitor Plan"
    var loop = false
    var defaultDelaySeconds = 30
    var messages: [MessageStep] = [
        .init(label: "Continue", text: "كمل من آخر نقطة مؤكدة واستمر بدون تكرار."),
        .init(label: "Verify", text: "راجع النتيجة واكمل المهمة بعد التأكد من التنفيذ.")
    ]

    func validate() throws {
        guard schemaVersion == 1 else { throw MonitorError.invalidPlan("schemaVersion must equal 1") }
        guard (30...3600).contains(defaultDelaySeconds) else { throw MonitorError.invalidPlan("Default delay must be 30–3600 seconds") }
        guard (1...1000).contains(messages.count) else { throw MonitorError.invalidPlan("1–1000 messages required") }
        guard messages.contains(where: \.enabled) else { throw MonitorError.invalidPlan("Enable at least one message") }
        for (index, message) in messages.enumerated() {
            guard !message.text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
                throw MonitorError.invalidPlan("Message \(index + 1) cannot be empty")
            }
            if let delay = message.delaySeconds, !(30...3600).contains(delay) {
                throw MonitorError.invalidPlan("Message \(index + 1) delay must be 30–3600 seconds")
            }
        }
    }

    func nextPendingIndex(after previous: Int? = nil) -> Int? {
        if loop {
            let start = previous.map { ($0 + 1) % messages.count } ?? 0
            for offset in 0..<messages.count {
                let i = (start + offset) % messages.count
                if messages[i].enabled { return i }
            }
            return nil
        }
        return messages.indices.first(where: { messages[$0].enabled && !messages[$0].sent })
    }
}

struct MonitorCheckpoint: Codable {
    var pendingResponse = false
    var pendingSendIndex: Int? = nil
    var pendingSendText = ""
    var pendingSendBaselineCount = 0
    var pendingAssistantBaseline = 0
    var assistantBaseline = 0
    var currentIndex: Int? = nil
    var nextLoopIndex = 0
    var conversationURL = ""
    var tabId = ""
    var blockedReason = ""
}

struct LocalSettings: Codable {
    var plan = MessagePlan()
    var checkpoint = MonitorCheckpoint()
}

enum MonitorError: LocalizedError {
    case invalidPlan(String)
    case browser(String)
    case blocked(String)

    var errorDescription: String? {
        switch self {
        case .invalidPlan(let x): return x
        case .browser(let x): return x
        case .blocked(let x): return x
        }
    }
}

struct PageState: Codable {
    var url: String
    var lastUser: String
    var userCount: Int
    var lastAssistant: String
    var assistantCount: Int
    var generating: Bool
    var composerReady: Bool
    var sendEnabled: Bool
    var hasRateLimit: Bool
}
