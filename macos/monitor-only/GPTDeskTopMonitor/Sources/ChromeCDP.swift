import Foundation
import AppKit

struct ChromeTab: Decodable {
    var id: String
    var type: String
    var url: String
    var webSocketDebuggerUrl: String?
}

/// A dedicated Chrome profile only. Never attaches to or modifies the user's normal Chrome profile.
actor ChromeCDP {
    private var port: Int?
    private var ownedProcess: Process?
    private let directory: URL
    private var commandId = 0

    init() {
        let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
        directory = support.appendingPathComponent("GPTDeskTopMonitorMac/ChromeProfile", isDirectory: true)
    }

    func connect() async throws -> [ChromeTab] {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        // Reattach to our own profile's active CDP endpoint on restart, if still healthy.
        if let savedPort = try? readDebuggingPort() {
            port = savedPort
            if let tabs = try? await listTabs() { return tabs }
        }
        let candidates = [
            "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
            NSHomeDirectory() + "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
        ]
        guard let executable = candidates.first(where: { FileManager.default.isExecutableFile(atPath: $0) }) else {
            throw MonitorError.browser("Install Google Chrome in /Applications before connecting")
        }
        // A stale DevToolsActivePort cannot be trusted if its process is gone.
        try? FileManager.default.removeItem(at: directory.appendingPathComponent("DevToolsActivePort"))
        let process = Process()
        process.executableURL = URL(fileURLWithPath: executable)
        process.arguments = ["--remote-debugging-port=0", "--remote-debugging-address=127.0.0.1",
                             "--user-data-dir=\(directory.path)", "--no-first-run", "--no-default-browser-check",
                             "--new-window", "https://chatgpt.com/"]
        try process.run()
        ownedProcess = process
        for _ in 0..<80 {
            try Task.checkCancellation()
            try await Task.sleep(nanoseconds: 250_000_000)
            if let p = try? readDebuggingPort() {
                port = p
                if let tabs = try? await listTabs() { return tabs }
            }
        }
        throw MonitorError.browser("Chrome CDP did not become available. Check Chrome startup or the dedicated profile lock.")
    }

    private func readDebuggingPort() throws -> Int {
        let file = directory.appendingPathComponent("DevToolsActivePort")
        let content = try String(contentsOf: file, encoding: .utf8)
        guard let line = content.components(separatedBy: .newlines).first,
              let p = Int(line), p > 0 && p < 65536 else {
            throw MonitorError.browser("Invalid Chrome debugging port")
        }
        return p
    }

    private func endpoint(_ path: String) throws -> URL {
        guard let port else { throw MonitorError.browser("Chrome is not connected") }
        guard let url = URL(string: "http://127.0.0.1:\(port)\(path)") else {
            throw MonitorError.browser("Invalid Chrome endpoint")
        }
        return url
    }

    func listTabs() async throws -> [ChromeTab] {
        let (data, _) = try await URLSession.shared.data(from: endpoint("/json/list"))
        return try JSONDecoder().decode([ChromeTab].self, from: data)
    }

    func selectedTab(preferredId: String = "", preferredURL: String = "") async throws -> ChromeTab {
        let tabs = try await listTabs().filter { $0.type == "page" && ($0.url.hasPrefix("https://chatgpt.com/") || $0.url == "https://chatgpt.com") }
        if let match = tabs.first(where: { $0.id == preferredId }) { return match }
        if !preferredId.isEmpty { throw MonitorError.blocked("Saved ChatGPT tab was lost. Stopped to avoid sending into a different conversation.") }
        if let exact = tabs.first(where: { $0.url == preferredURL }), !preferredURL.isEmpty { return exact }
        if tabs.count == 1, let only = tabs.first { return only }
        if tabs.isEmpty { throw MonitorError.browser("Open ChatGPT in the dedicated Chrome window, then connect again") }
        throw MonitorError.blocked("Several ChatGPT tabs are open in the dedicated profile. Close extras before starting.")
    }

    func createFreshTab() async throws -> ChromeTab {
        var req = URLRequest(url: endpoint("/json/new?https://chatgpt.com/"))
        req.httpMethod = "PUT"
        let (data, _) = try await URLSession.shared.data(for: req)
        return try JSONDecoder().decode(ChromeTab.self, from: data)
    }

    private func command(tab: ChromeTab, method: String, params: [String: Any]) async throws -> [String: Any] {
        guard let socketURL = tab.webSocketDebuggerUrl.flatMap(URL.init(string:)),
              socketURL.host == "127.0.0.1" || socketURL.host == "localhost" else {
            throw MonitorError.browser("The selected Chrome target has no local CDP WebSocket")
        }
        commandId += 1
        let id = commandId
        let session = URLSession(configuration: .ephemeral)
        let socket = session.webSocketTask(with: socketURL)
        socket.resume()
        defer { socket.cancel(with: .normalClosure, reason: nil); session.invalidateAndCancel() }
        let data = try JSONSerialization.data(withJSONObject: ["id": id, "method": method, "params": params])
        try await socket.send(.string(String(decoding: data, as: UTF8.self)))
        // CDP may emit events before the matching command response.
        for _ in 0..<32 {
            try Task.checkCancellation()
            let msg = try await socket.receive()
            let bytes: Data
            switch msg { case .string(let text): bytes = Data(text.utf8); case .data(let raw): bytes = raw; @unknown default: continue }
            guard let obj = try JSONSerialization.jsonObject(with: bytes) as? [String: Any] else { continue }
            if (obj["id"] as? Int) != id { continue }
            if let error = obj["error"] as? [String: Any] {
                throw MonitorError.browser("CDP \(method): \(error["message"] ?? "unknown error")")
            }
            return obj["result"] as? [String: Any] ?? [:]
        }
        throw MonitorError.browser("No matching CDP response for \(method)")
    }

    private func evaluate(tab: ChromeTab, script: String) async throws -> Any? {
        let result = try await command(tab: tab, method: "Runtime.evaluate", params: [
            "expression": script, "returnByValue": true, "awaitPromise": false
        ])
        if result["exceptionDetails"] != nil { throw MonitorError.browser("Chrome page evaluation failed") }
        return (result["result"] as? [String: Any])?["value"]
    }

    func readState(tab: ChromeTab) async throws -> PageState {
        let script = #"""
        (() => {
          const text = e => (e?.innerText || e?.textContent || '').trim();
          const list = role => Array.from(document.querySelectorAll('[data-message-author-role="' + role + '"]'));
          const users = list('user'), assistants = list('assistant');
          const editor = document.querySelector('#prompt-textarea, [contenteditable="true"][data-lexical-editor="true"], [contenteditable="true"][role="textbox"]');
          const send = document.querySelector('button[data-testid="send-button"], button[aria-label="Send prompt"], button[aria-label="Send message"]');
          const stop = document.querySelector('button[data-testid="stop-button"], button[aria-label="Stop streaming"], button[aria-label="Stop generating"]');
          const body = (document.body?.innerText || '').slice(-3000);
          return JSON.stringify({url:location.href,lastUser:text(users.at(-1)),userCount:users.length,
            lastAssistant:text(assistants.at(-1)),assistantCount:assistants.length,
            generating:!!stop,composerReady:!!editor,sendEnabled:!!send && !send.disabled,
            hasRateLimit:/too many requests|rate limit|usage limit|لقد وصلت.*الحد/i.test(body)});
        })()
        """#
        guard let json = try await evaluate(tab: tab, script: script) as? String,
              let data = json.data(using: .utf8) else { throw MonitorError.browser("Cannot read ChatGPT page state") }
        return try JSONDecoder().decode(PageState.self, from: data)
    }

    /// After any physical submit, the caller must VERIFY before checkpointing. Exceptions are ambiguous, never auto-retry.
    func submitOnce(tab: ChromeTab, text: String) async throws {
        let state = try await readState(tab: tab)
        guard !state.generating && state.composerReady else { throw MonitorError.blocked("ChatGPT composer is not ready") }
        guard !state.hasRateLimit else { throw MonitorError.blocked("ChatGPT rate limit is visible") }
        let focusScript = #"""
        (() => { const el=document.querySelector('#prompt-textarea, [contenteditable="true"][data-lexical-editor="true"], [contenteditable="true"][role="textbox"]');
          if(!el) return false; el.focus(); document.execCommand('selectAll',false,null); return true; })()
        """#
        guard (try await evaluate(tab: tab, script: focusScript) as? Bool) == true else {
            throw MonitorError.browser("Could not focus ChatGPT editor")
        }
        _ = try await command(tab: tab, method: "Input.insertText", params: ["text": text])
        // Read the composer before crossing the physical click boundary.
        let check = #"""
        (() => {const e=document.querySelector('#prompt-textarea, [contenteditable="true"][data-lexical-editor="true"], [contenteditable="true"][role="textbox"]');return (e?.innerText||e?.textContent||'').trim()})()
        """#
        guard let draft = try await evaluate(tab: tab, script: check) as? String,
              draft.trimmingCharacters(in: .whitespacesAndNewlines) == text.trimmingCharacters(in: .whitespacesAndNewlines) else {
            throw MonitorError.browser("Composer content mismatch. No send attempted.")
        }
        let click = #"""
        (() => {const b=document.querySelector('button[data-testid="send-button"], button[aria-label="Send prompt"], button[aria-label="Send message"]');
          if(!b || b.disabled) return false; b.click(); return true;})()
        """#
        guard (try await evaluate(tab: tab, script: click) as? Bool) == true else {
            throw MonitorError.browser("Send button not ready. Draft retained; no confirmed click.")
        }
    }
}
