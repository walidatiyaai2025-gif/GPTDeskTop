import Foundation
import SwiftUI
import AppKit

@MainActor final class MonitorEngine: ObservableObject {
    @Published var plan: MessagePlan
    @Published private(set) var running = false
    @Published private(set) var status = "Ready — connect Chrome and sign in to ChatGPT."
    @Published private(set) var log: [String] = []
    @Published private(set) var currentPosition = "—"
    @Published private(set) var connected = false
    @Published private(set) var connectionLabel = "Not connected"
    @Published private(set) var selectedTabURL = ""
    @Published private(set) var hold = ""

    private let store = MonitorStore()
    private let chrome = ChromeCDP()
    private var settings: LocalSettings
    private var worker: Task<Void, Never>?
    private var connectedTab: ChromeTab?

    init() {
        let current = store.load()
        settings = current
        plan = current.plan
        hold = current.checkpoint.blockedReason
        selectedTabURL = current.checkpoint.conversationURL
    }

    var sentCount: Int { plan.messages.filter { $0.enabled && $0.sent }.count }
    var pendingCount: Int { plan.messages.filter { $0.enabled && !$0.sent }.count }

    private func persist() throws {
        settings.plan = plan
        try store.save(settings)
    }

    func savePlanEdits() {
        guard !running else { return }
        do { try persist() } catch { event("Save failed: \(error.localizedDescription)") }
    }

    func importPlan(from url: URL) {
        guard !running else { return }
        do {
            let granted = url.startAccessingSecurityScopedResource()
            defer { if granted { url.stopAccessingSecurityScopedResource() } }
            let data = try Data(contentsOf: url)
            var decoded = try JSONDecoder().decode(MessagePlan.self, from: data)
            try decoded.validate()
            // Imported files are plans, not runtime checkpoint authority.
            for i in decoded.messages.indices { decoded.messages[i].sent = false }
            plan = decoded
            settings.checkpoint = MonitorCheckpoint()
            hold = ""
            selectedTabURL = ""
            try persist()
            event("Imported \(plan.messages.count) messages; external sent flags were reset for safety.")
        } catch { event("Import rejected: \(error.localizedDescription)") }
    }

    func exportPlan(to url: URL) {
        do {
            let encoder = JSONEncoder(); encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            let data = try encoder.encode(plan)
            try data.write(to: url, options: [.atomic])
            event("Plan exported: \(url.lastPathComponent)")
        } catch { event("Export failed: \(error.localizedDescription)") }
    }

    func connect() async {
        guard !running else { return }
        status = "Connecting to Chrome…"
        do {
            let tabs = try await chrome.connect()
            connected = true
            connectionLabel = await chrome.connectionModeDescription()
            status = "Connected to \(connectionLabel). Start when the intended ChatGPT conversation is ready."
            event("Chrome connected using \(connectionLabel). \(tabs.count) tab(s) discovered.")
        } catch {
            connected = false
            connectionLabel = "Not connected"
            status = error.localizedDescription
            event("Connection failed: \(error.localizedDescription)")
        }
    }

    func start() {
        guard !running else { return }
        do { try plan.validate(); try persist() }
        catch { event("Cannot start: \(error.localizedDescription)"); return }
        if !hold.isEmpty {
            event("Start blocked: resolve safety hold first. \(hold)")
            return
        }
        running = true
        worker = Task { [weak self] in
            guard let self else { return }
            await self.runLoop()
            self.running = false
            self.worker = nil
        }
    }

    func stop() {
        worker?.cancel()
        status = "Stop requested — no further messages will be submitted."
        event(status)
    }

    func clearHoldAfterManualReview() {
        guard !running else { return }
        // This action must be explicitly confirmed in the UI. It never marks a message sent.
        settings.checkpoint.blockedReason = ""
        settings.checkpoint.pendingSendIndex = nil
        settings.checkpoint.pendingSendText = ""
        hold = ""
        do { try persist(); event("Safety hold cleared by operator after manual chat review.") }
        catch { event("Could not persist: \(error.localizedDescription)") }
    }

    private func event(_ message: String) {
        status = message
        let timestamp = Date().formatted(date: .omitted, time: .standard)
        log.append("\(timestamp)  \(message)")
        if log.count > 300 { log.removeFirst(log.count - 300) }
    }

    private func block(_ reason: String) {
        settings.checkpoint.blockedReason = reason
        hold = reason
        do { try persist() } catch { event("Critical: hold could not be saved: \(error.localizedDescription)") }
        event("BLOCKED — \(reason)")
    }

    private func runLoop() async {
        do {
            guard connected else { throw MonitorError.browser("Connect Chrome before starting") }
            let cp = settings.checkpoint
            let tab: ChromeTab
            if !cp.tabId.isEmpty {
                tab = try await chrome.selectedTab(preferredId: cp.tabId, preferredURL: cp.conversationURL)
            } else {
                // First Start creates a fresh ChatGPT conversation, matching Windows Monitor Only.
                tab = try await chrome.createFreshTab()
                settings.checkpoint.tabId = tab.id
                settings.checkpoint.conversationURL = tab.url
                try persist()
                event("Fresh ChatGPT tab created for this plan.")
            }
            connectedTab = tab
            selectedTabURL = tab.url

            // Recover an uncertain physical send ONLY if a read-only receipt proves it was delivered.
            if let pendingIndex = settings.checkpoint.pendingSendIndex {
                let state = try await chrome.readState(tab: tab)
                guard state.userCount > settings.checkpoint.pendingSendBaselineCount,
                      normalize(state.lastUser) == normalize(settings.checkpoint.pendingSendText) else {
                    throw MonitorError.blocked("A previous send may have been accepted. Inspect Chrome manually; automatic resend is disabled.")
                }
                try checkpointDelivery(index: pendingIndex, baseline: settings.checkpoint.pendingAssistantBaseline)
                event("Recovered verified delivery from previous session without resending.")
            }

            while !Task.isCancelled {
                if settings.checkpoint.pendingResponse {
                    event("Waiting for last confirmed message response…")
                    try await waitForResponse(tab: tab, baseline: settings.checkpoint.assistantBaseline)
                    settings.checkpoint.pendingResponse = false
                    try persist()
                    let delay = effectiveDelay(for: settings.checkpoint.currentIndex)
                    event("Response complete — safety delay \(delay)s.")
                    try await sleep(seconds: delay)
                }
                let index: Int?
                if plan.loop {
                    let start = settings.checkpoint.nextLoopIndex % plan.messages.count
                    index = (0..<plan.messages.count).map { (start + $0) % plan.messages.count }.first { plan.messages[$0].enabled }
                } else {
                    index = plan.nextPendingIndex()
                }
                guard let index else { event("Plan complete — all enabled one-time messages were confirmed."); return }
                let step = plan.messages[index]
                currentPosition = "\(index + 1) / \(plan.messages.count)"
                event("Preparing message \(index + 1) of \(plan.messages.count).")
                var pre: PageState? = nil
                for _ in 0..<30 {
                    try Task.checkCancellation()
                    if let state = try? await chrome.readState(tab: tab), state.composerReady {
                        pre = state
                        break
                    }
                    try await sleep(seconds: 2)
                }
                guard var ready = pre else {
                    throw MonitorError.blocked("ChatGPT editor did not load after 60s. Check login in the dedicated Chrome window")
                }
                guard ready.url.hasPrefix("https://chatgpt.com/") else { throw MonitorError.blocked("Target left ChatGPT") }
                // A previous response may still be streaming; never send over it.
                for _ in 0..<150 where ready.generating {
                    try await sleep(seconds: 2)
                    ready = try await chrome.readState(tab: tab)
                }
                guard !ready.generating, ready.composerReady, !ready.hasRateLimit else {
                    throw MonitorError.blocked("ChatGPT is still generating, rate limited, or editor is unavailable")
                }
                // Durable pre-submit safety marker: crash after click will not trigger blind retry.
                settings.checkpoint.pendingSendIndex = index
                settings.checkpoint.pendingSendText = step.text
                settings.checkpoint.pendingSendBaselineCount = ready.userCount
                settings.checkpoint.pendingAssistantBaseline = ready.assistantCount
                try persist()
                event("Sending message \(index + 1) — checking delivery receipt…")
                try await chrome.submitOnce(tab: tab, text: step.text)
                var verified = false
                for _ in 0..<45 {
                    try await sleep(seconds: 2)
                    let state = try await chrome.readState(tab: tab)
                    if state.userCount > ready.userCount && normalize(state.lastUser) == normalize(step.text) {
                        try checkpointDelivery(index: index, baseline: ready.assistantCount)
                        if state.url.hasPrefix("https://chatgpt.com/c/") {
                            settings.checkpoint.conversationURL = state.url
                            selectedTabURL = state.url
                            try persist()
                        }
                        verified = true
                        event("Message \(index + 1) receipt confirmed and checkpoint saved.")
                        break
                    }
                }
                guard verified else {
                    throw MonitorError.blocked("Send outcome not confirmed in 90s — do not resend without manual review")
                }
            }
        } catch is CancellationError {
            event("Stopped. Checkpoint retained.")
        } catch {
            block(error.localizedDescription)
        }
    }

    private func checkpointDelivery(index: Int, baseline: Int) throws {
        plan.messages[index].sent = true
        settings.checkpoint.currentIndex = index
        settings.checkpoint.pendingSendIndex = nil
        settings.checkpoint.pendingSendText = ""
        settings.checkpoint.pendingResponse = true
        settings.checkpoint.assistantBaseline = baseline
        if plan.loop { settings.checkpoint.nextLoopIndex = (index + 1) % plan.messages.count }
        try persist()  // fail-closed if atomic checkpoint write fails
    }

    private func waitForResponse(tab: ChromeTab, baseline: Int) async throws {
        var stableCount = 0
        var lastText = ""
        for _ in 0..<450 {
            try await sleep(seconds: 2)
            let state = try await chrome.readState(tab: tab)
            if state.hasRateLimit { throw MonitorError.blocked("ChatGPT rate/usage limit detected; paused") }
            let completed = state.assistantCount > baseline && !state.generating && !state.lastAssistant.isEmpty
            if completed && state.lastAssistant == lastText { stableCount += 1 } else { stableCount = 0 }
            lastText = state.lastAssistant
            if stableCount >= 2 { return }
        }
        throw MonitorError.blocked("Response completion not confirmed within 15 minutes")
    }

    private func effectiveDelay(for index: Int?) -> Int {
        guard let index, plan.messages.indices.contains(index) else { return plan.defaultDelaySeconds }
        return plan.messages[index].delaySeconds ?? plan.defaultDelaySeconds
    }

    private func normalize(_ s: String) -> String { s.trimmingCharacters(in: .whitespacesAndNewlines) }
    private func sleep(seconds: Int) async throws {
        try await Task.sleep(nanoseconds: UInt64(seconds) * 1_000_000_000)
    }
}
