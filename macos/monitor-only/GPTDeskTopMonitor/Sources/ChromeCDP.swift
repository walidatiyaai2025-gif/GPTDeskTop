import Foundation
import AppKit

struct ChromeTab: Decodable {
    var id: String
    var type: String
    var url: String
    var webSocketDebuggerUrl: String?
}

/// Connects only to the already-running, explicitly approved Chrome profile.
actor ChromeCDP {
    private var browserWebSocketURL: URL?
    private var browserURLSession: URLSession?
    private var browserSocket: URLSessionWebSocketTask?
    private var commandId = 0
    private var targetSessions: [String: String] = [:]
    private var browserCommandInFlight = false
    private var browserCommandWaiters: [CheckedContinuation<Void, Never>] = []

    func connectionModeDescription() -> String {
        "the currently open Chrome profile"
    }

    func connect() async throws -> [ChromeTab] {
        let existingProfile = FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/Google/Chrome", isDirectory: true)
        guard let endpoint = try? readBrowserWebSocketEndpoint(in: existingProfile) else {
            throw MonitorError.browser("No remote-debugging connection is active for your open Chrome profile. In the Chrome window you already use, enable Allow remote debugging at chrome://inspect/#remote-debugging, approve GPTDeskTop Monitor Only, then click Connect again. The app will not open another profile or window.")
        }
        if browserWebSocketURL != endpoint {
            closeBrowserConnection()
            targetSessions.removeAll()
        }
        browserWebSocketURL = endpoint
        do {
            _ = try await browserCommand(method: "Browser.getVersion", params: [:])
            return try await listTabs()
        } catch {
            closeBrowserConnection()
            targetSessions.removeAll()
            browserWebSocketURL = nil
            throw MonitorError.browser("Could not connect to the already-open Chrome profile. Check that Allow remote debugging is enabled and approve GPTDeskTop Monitor Only in the Chrome window. No new profile or window was opened. Details: \(error.localizedDescription)")
        }
    }

    private func readBrowserWebSocketEndpoint(in userDataDirectory: URL) throws -> URL {
        let file = userDataDirectory.appendingPathComponent("DevToolsActivePort")
        let lines = try String(contentsOf: file, encoding: .utf8)
            .components(separatedBy: .newlines).filter { !$0.isEmpty }
        guard lines.count >= 2, let p = Int(lines[0]), p > 0 && p < 65536,
              lines[1].hasPrefix("/devtools/browser/"),
              let url = URL(string: "ws://127.0.0.1:\(p)\(lines[1])") else {
            throw MonitorError.browser("Invalid Chrome DevToolsActivePort")
        }
        return url
    }

    func listTabs() async throws -> [ChromeTab] {
        let result = try await browserCommand(method: "Target.getTargets", params: [:])
        let targets = result["targetInfos"] as? [[String: Any]] ?? []
        return targets.compactMap { target in
            guard let id = target["targetId"] as? String,
                  let type = target["type"] as? String,
                  let url = target["url"] as? String else { return nil }
            return ChromeTab(id: id, type: type, url: url, webSocketDebuggerUrl: nil)
        }
    }

    func selectedTab(preferredId: String = "", preferredURL: String = "") async throws -> ChromeTab {
        let tabs = try await listTabs().filter { $0.type == "page" && ($0.url.hasPrefix("https://chatgpt.com/") || $0.url == "https://chatgpt.com") }
        if let match = tabs.first(where: { $0.id == preferredId }) { return match }
        if !preferredId.isEmpty { throw MonitorError.blocked("Saved ChatGPT tab was lost. Stopped to avoid sending into a different conversation.") }
        if let exact = tabs.first(where: { $0.url == preferredURL }), !preferredURL.isEmpty { return exact }
        if tabs.count == 1, let only = tabs.first { return only }
        if tabs.isEmpty { throw MonitorError.browser("Open ChatGPT in your current Chrome profile, then connect again") }
        throw MonitorError.blocked("Several ChatGPT tabs are open. Close extras before starting.")
    }

    func createFreshTab() async throws -> ChromeTab {
        let url = "https://chatgpt.com/"
        let result = try await browserCommand(method: "Target.createTarget", params: ["url": url])
        guard let id = result["targetId"] as? String else {
            throw MonitorError.browser("Chrome did not return an ID for the new ChatGPT tab")
        }
        return ChromeTab(id: id, type: "page", url: url, webSocketDebuggerUrl: nil)
    }

    private func command(tab: ChromeTab, method: String, params: [String: Any]) async throws -> [String: Any] {
        let sessionId: String
        if let attached = targetSessions[tab.id] {
            sessionId = attached
        } else {
            let attached = try await browserSocketCommand(method: "Target.attachToTarget", params: ["targetId": tab.id, "flatten": true])
            guard let created = attached["sessionId"] as? String else {
                throw MonitorError.browser("Chrome did not create a CDP session for the selected tab")
            }
            targetSessions[tab.id] = created
            sessionId = created
        }
        do {
            return try await browserSocketCommand(method: method, params: params, sessionId: sessionId)
        } catch {
            targetSessions.removeValue(forKey: tab.id)
            throw error
        }
    }

    private func makeBrowserSocket() throws -> URLSessionWebSocketTask {
        if let browserSocket {
            if browserSocket.closeCode == .invalid { return browserSocket }
            closeBrowserConnection()
            targetSessions.removeAll()
        }
        guard let browserWebSocketURL else { throw MonitorError.browser("Existing Chrome CDP connection is unavailable") }
        let session = URLSession(configuration: .ephemeral)
        let socket = session.webSocketTask(with: browserWebSocketURL)
        socket.resume()
        browserURLSession = session
        browserSocket = socket
        return socket
    }

    private func browserCommand(method: String, params: [String: Any]) async throws -> [String: Any] {
        try await browserSocketCommand(method: method, params: params)
    }

    private func browserSocketCommand(method: String,
                                     params: [String: Any], sessionId: String? = nil) async throws -> [String: Any] {
        await acquireBrowserCommandPermit()
        defer { releaseBrowserCommandPermit() }
        try Task.checkCancellation()
        let socket = try makeBrowserSocket()
        commandId += 1
        let id = commandId
        var payload: [String: Any] = ["id": id, "method": method, "params": params]
        if let sessionId { payload["sessionId"] = sessionId }
        let data = try JSONSerialization.data(withJSONObject: payload)
        try await socket.send(.string(String(decoding: data, as: UTF8.self)))
        for _ in 0..<64 {
            try Task.checkCancellation()
            let message = try await socket.receive()
            let bytes: Data
            switch message {
            case .string(let text): bytes = Data(text.utf8)
            case .data(let raw): bytes = raw
            @unknown default: continue
            }
            guard let object = try JSONSerialization.jsonObject(with: bytes) as? [String: Any],
                  object["id"] as? Int == id else { continue }
            if let error = object["error"] as? [String: Any] {
                throw MonitorError.browser("CDP \(method): \(error["message"] ?? "unknown error")")
            }
            return object["result"] as? [String: Any] ?? [:]
        }
        throw MonitorError.browser("No matching browser-level CDP response for \(method)")
    }

    private func acquireBrowserCommandPermit() async {
        if !browserCommandInFlight {
            browserCommandInFlight = true
            return
        }
        await withCheckedContinuation { continuation in
            browserCommandWaiters.append(continuation)
        }
    }

    private func releaseBrowserCommandPermit() {
        if browserCommandWaiters.isEmpty {
            browserCommandInFlight = false
        } else {
            browserCommandWaiters.removeFirst().resume()
        }
    }

    private func closeBrowserConnection() {
        browserSocket?.cancel(with: .normalClosure, reason: nil)
        browserSocket = nil
        browserURLSession?.invalidateAndCancel()
        browserURLSession = nil
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
          const users = Array.from(document.querySelectorAll('[data-user-message-bubble="true"]'));
          const assistants = Array.from(document.querySelectorAll('[data-content-search-unit-key*="assistant"]'));
          const editor = document.querySelector('#prompt-textarea, [contenteditable="true"][data-lexical-editor="true"], [contenteditable="true"][role="textbox"]');
          const send = document.querySelector('button[data-testid="send-button"], [role="button"][data-testid="send-button"], button[data-testid="composer-send-button"], [role="button"][data-testid="composer-send-button"], button[aria-label="Send prompt"], [role="button"][aria-label="Send prompt"], button[aria-label="Send message"], [role="button"][aria-label="Send message"], button[aria-label="Send"], [role="button"][aria-label="Send"]');
          const stop = document.querySelector('button[data-testid="stop-button"], [role="button"][data-testid="stop-button"], button[aria-label="Stop streaming"], [role="button"][aria-label="Stop streaming"], button[aria-label="Stop generating"], [role="button"][aria-label="Stop generating"]');
          const body = (document.body?.innerText || '').slice(-3000);
          return JSON.stringify({url:location.href,lastUser:text(users.at(-1)),userCount:users.length,
            lastAssistant:text(assistants.at(-1)),assistantCount:assistants.length,
            generating:!!stop,composerReady:!!editor,sendEnabled:!!send && !send.disabled && send.getAttribute('aria-disabled')!=='true' && send.getAttribute('data-disabled')!=='true',
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
        let readyToSend = try await readState(tab: tab)
        guard readyToSend.sendEnabled && !readyToSend.generating && !readyToSend.hasRateLimit else {
            throw MonitorError.blocked("Draft is present but ChatGPT has not enabled Send. No click attempted.")
        }
        let click = #"""
        (() => {const b=document.querySelector('button[data-testid="send-button"], [role="button"][data-testid="send-button"], button[data-testid="composer-send-button"], [role="button"][data-testid="composer-send-button"], button[aria-label="Send prompt"], [role="button"][aria-label="Send prompt"], button[aria-label="Send message"], [role="button"][aria-label="Send message"], button[aria-label="Send"], [role="button"][aria-label="Send"]');
          if(!b || b.disabled || b.getAttribute('aria-disabled')==='true' || b.getAttribute('data-disabled')==='true') return false; b.click(); return true;})()
        """#
        guard (try await evaluate(tab: tab, script: click) as? Bool) == true else {
            throw MonitorError.browser("Send button not ready. Draft retained; no confirmed click.")
        }
    }
}
