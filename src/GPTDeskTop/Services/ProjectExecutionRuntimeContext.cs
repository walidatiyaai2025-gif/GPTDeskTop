using GPTDeskTop.Data;

namespace GPTDeskTop.Services;

public static class ProjectExecutionRuntimeContext
{
    private static readonly object Sync = new();
    private static ProjectExecutionController? _controller;

    public static ProjectExecutionController? Controller
    {
        get { lock (Sync) return _controller; }
    }

    public static void Configure(LocalDatabase database, ChatGptMonitorService monitorService, ChromeDevToolsService chrome)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(monitorService);
        ArgumentNullException.ThrowIfNull(chrome);
        lock (Sync)
            _controller = new ProjectExecutionController(new ProjectStateStore(), database, monitorService, chrome);
    }
}
