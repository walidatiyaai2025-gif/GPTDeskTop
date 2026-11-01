import SwiftUI
import AppKit
import UniformTypeIdentifiers

@main struct MonitorOnlyMacApp: App {
    @StateObject private var engine = MonitorEngine()
    var body: some Scene {
        WindowGroup("GPTDeskTop Monitor Only") {
            MonitorOnlyView(engine: engine)
                .frame(minWidth: 980, minHeight: 690)
        }
        .defaultSize(width: 1180, height: 810)
    }
}

struct MonitorOnlyView: View {
    @ObservedObject var engine: MonitorEngine
    @State private var selection = 0
    @State private var showClearHold = false

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack {
                VStack(alignment: .leading, spacing: 5) {
                    Text("Monitor Only").font(.largeTitle.bold())
                    Text("Message plan • confirmed sends • durable resume • current Chrome profile")
                        .foregroundStyle(.secondary).font(.subheadline)
                }
                Spacer()
                Label(engine.running ? "Running" : "Stopped", systemImage: engine.running ? "circle.fill" : "pause.circle")
                    .foregroundColor(engine.running ? .green : .secondary)
            }
            .padding(.bottom, 4)

            GroupBox("1. Chrome / ChatGPT") {
                VStack(alignment: .leading, spacing: 8) {
                    HStack(spacing: 12) {
                        Button { Task { await engine.connect() } } label: {
                            Label("Connect / Open Chrome", systemImage: "globe")
                        }.disabled(engine.running)
                        Text(engine.connectionLabel)
                            .foregroundColor(engine.connected ? .green : .secondary)
                        Spacer()
                        if engine.running { ProgressView().controlSize(.small) }
                    }
                    Text("To reuse your current Chrome profile, enable Allow remote debugging in chrome://inspect/#remote-debugging. This grants CDP access to that browser session. If unavailable, the app opens its separate profile.")
                        .font(.caption).foregroundStyle(.secondary)
                    if !engine.selectedTabURL.isEmpty {
                        Text(engine.selectedTabURL).font(.caption.monospaced()).lineLimit(1).textSelection(.enabled)
                    }
                }.padding(7)
            }

            GroupBox("2. Messages / JSON Plan") {
                VStack(spacing: 10) {
                    HStack(spacing: 10) {
                        TextField("Plan name", text: $engine.plan.name)
                            .textFieldStyle(.roundedBorder).disabled(engine.running)
                        Toggle("Loop", isOn: $engine.plan.loop)
                            .toggleStyle(.checkbox).disabled(engine.running)
                        Text("Delay (sec)")
                        TextField("30", value: $engine.plan.defaultDelaySeconds, format: .number)
                            .frame(width: 64).textFieldStyle(.roundedBorder).disabled(engine.running)
                        Button("Import JSON") { importJSON() }.disabled(engine.running)
                        Button("Export JSON") { exportJSON() }
                    }
                    HStack(spacing: 12) {
                        VStack(alignment: .leading, spacing: 6) {
                            List {
                                ForEach(engine.plan.messages.indices, id: \.self) { i in
                                    Button { selection = i } label: {
                                        HStack {
                                            Image(systemName: engine.plan.messages[i].sent ? "checkmark.circle.fill" : (engine.plan.messages[i].enabled ? "circle" : "minus.circle"))
                                                .foregroundColor(engine.plan.messages[i].sent ? .green : .secondary)
                                            Text("\(i + 1). \(engine.plan.messages[i].label.isEmpty ? "Message" : engine.plan.messages[i].label)")
                                                .lineLimit(1)
                                            Spacer()
                                        }
                                        .padding(5)
                                        .background(selection == i ? Color.accentColor.opacity(0.16) : Color.clear)
                                        .cornerRadius(6)
                                    }.buttonStyle(.plain)
                                }
                            }
                            .listStyle(.bordered)
                            HStack {
                                Button { add() } label: { Image(systemName: "plus") }
                                Button { remove() } label: { Image(systemName: "minus") }
                                Button { move(-1) } label: { Image(systemName: "arrow.up") }
                                Button { move(1) } label: { Image(systemName: "arrow.down") }
                                Spacer()
                            }.disabled(engine.running)
                        }.frame(minWidth: 250, maxWidth: 350)
                        Divider()
                        if engine.plan.messages.indices.contains(selection) {
                            VStack(alignment: .leading, spacing: 8) {
                                HStack {
                                    Text("Edit message \(selection + 1)").font(.headline)
                                    Spacer()
                                    Toggle("Enabled", isOn: $engine.plan.messages[selection].enabled)
                                        .toggleStyle(.checkbox).disabled(engine.running)
                                }
                                TextField("Label", text: $engine.plan.messages[selection].label)
                                    .textFieldStyle(.roundedBorder).disabled(engine.running)
                                TextEditor(text: $engine.plan.messages[selection].text)
                                    .font(.body).border(Color.gray.opacity(0.3))
                                    .disabled(engine.running)
                                HStack {
                                    Text("Override delay (sec, 30–3600)").font(.caption)
                                    TextField("Uses default", text: delayBinding(index: selection))
                                        .frame(width: 110).textFieldStyle(.roundedBorder)
                                        .disabled(engine.running)
                                    Spacer()
                                    if engine.plan.messages[selection].sent {
                                        Label("Sent", systemImage: "checkmark.circle.fill").foregroundStyle(.green)
                                    }
                                }
                            }
                        } else {
                            Text("Select a message").foregroundStyle(.secondary)
                        }
                    }
                    .frame(minHeight: 230)
                }.padding(7)
            }
            .onChange(of: engine.plan) { _ in engine.savePlanEdits() }

            GroupBox("3. Runtime & safety") {
                VStack(alignment: .leading, spacing: 9) {
                    HStack {
                        Button { engine.start() } label: { Label("Start Monitor", systemImage: "play.fill") }
                            .buttonStyle(.borderedProminent).disabled(engine.running || !engine.connected || !engine.hold.isEmpty)
                        Button { engine.stop() } label: { Label("Stop", systemImage: "stop.fill") }
                            .disabled(!engine.running)
                        Text("Message: \(engine.currentPosition)")
                        Spacer()
                        Text("Confirmed: \(engine.sentCount)  ·  Pending: \(engine.pendingCount)")
                            .font(.system(.caption, design: .monospaced))
                    }
                    Text(engine.status).font(.subheadline).textSelection(.enabled)
                    if !engine.hold.isEmpty {
                        HStack {
                            Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(.orange)
                            Text("Safety hold: \(engine.hold)").font(.caption).textSelection(.enabled)
                            Spacer()
                            Button("Review / Clear Hold") { showClearHold = true }.disabled(engine.running)
                        }
                    }
                }.padding(7)
            }

            GroupBox("4. Runtime Inspector") {
                ScrollViewReader { proxy in
                    ScrollView {
                        LazyVStack(alignment: .leading, spacing: 4) {
                            ForEach(Array(engine.log.enumerated()), id: \.offset) { i, line in
                                Text(line).font(.system(size: 11, design: .monospaced))
                                    .frame(maxWidth: .infinity, alignment: .leading)
                                    .textSelection(.enabled).id(i)
                            }
                        }.frame(maxWidth: .infinity, alignment: .leading)
                    }
                    .onChange(of: engine.log.count) { count in
                        if count > 0 { proxy.scrollTo(count - 1, anchor: .bottom) }
                    }
                }
            }
            .frame(minHeight: 96)
        }
        .padding(18)
        .alert("Clear safety hold?", isPresented: $showClearHold) {
            Button("Cancel", role: .cancel) { }
            Button("I verified the chat — Clear Hold", role: .destructive) {
                engine.clearHoldAfterManualReview()
            }
        } message: {
            Text("You must inspect the exact Chrome conversation first. A previous message may already have been sent. Clearing the hold lets the pending message be attempted again and could duplicate it.")
        }
    }

    private func delayBinding(index: Int) -> Binding<String> {
        Binding(get: {
            engine.plan.messages.indices.contains(index) ? engine.plan.messages[index].delaySeconds.map(String.init) ?? "" : ""
        }, set: { value in
            guard engine.plan.messages.indices.contains(index), !engine.running else { return }
            engine.plan.messages[index].delaySeconds = value.isEmpty ? nil : Int(value)
        })
    }

    private func add() {
        engine.plan.messages.append(MessageStep(label: "New message", text: ""))
        selection = engine.plan.messages.count - 1
    }

    private func remove() {
        guard engine.plan.messages.indices.contains(selection), engine.plan.messages.count > 1 else { return }
        engine.plan.messages.remove(at: selection)
        selection = min(selection, engine.plan.messages.count - 1)
    }

    private func move(_ delta: Int) {
        let next = selection + delta
        guard engine.plan.messages.indices.contains(selection), engine.plan.messages.indices.contains(next) else { return }
        engine.plan.messages.swapAt(selection, next)
        selection = next
    }

    private func importJSON() {
        let panel = NSOpenPanel(); panel.allowedContentTypes = [.json]; panel.allowsMultipleSelection = false
        if panel.runModal() == .OK, let url = panel.url { engine.importPlan(from: url); selection = 0 }
    }

    private func exportJSON() {
        let panel = NSSavePanel(); panel.allowedContentTypes = [.json]; panel.nameFieldStringValue = "monitor-plan.json"
        if panel.runModal() == .OK, let url = panel.url { engine.exportPlan(to: url) }
    }
}
