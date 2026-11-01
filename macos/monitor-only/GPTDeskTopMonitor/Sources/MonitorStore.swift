import Foundation

struct MonitorStore {
    let fileURL: URL

    init() {
        let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
        let dir = support.appendingPathComponent("GPTDeskTopMonitorMac", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        fileURL = dir.appendingPathComponent("monitor-state.json")
    }

    func load() -> LocalSettings {
        guard let data = try? Data(contentsOf: fileURL),
              let settings = try? JSONDecoder().decode(LocalSettings.self, from: data) else {
            return LocalSettings()
        }
        return settings
    }

    func save(_ settings: LocalSettings) throws {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        let bytes = try encoder.encode(settings)
        try bytes.write(to: fileURL, options: [.atomic])
    }
}
