using System.Reflection;
using GPTDeskTop.Data;
using GPTDeskTop.UI;

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
        {
            if (_controller is not null) return;
            _controller = new ProjectExecutionController(new ProjectStateStore(), database, monitorService, chrome);
        }
    }

    /// <summary>
    /// Compatibility bootstrap for the module-initialized Project Monitor UI. MainForm owns the
    /// live database/monitor/Chrome instances, so the orchestrator must bind to those instances
    /// rather than constructing a parallel runtime. This method is deliberately idempotent.
    /// </summary>
    public static bool TryConfigureFromForm(Form? form = null)
    {
        if (Controller is not null) return true;

        var mainForm = form as MainForm
                       ?? form?.Owner as MainForm
                       ?? Application.OpenForms.Cast<Form>().OfType<MainForm>().FirstOrDefault();
        if (mainForm is null) return false;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(MainForm);
        var database = type.GetField("_database", flags)?.GetValue(mainForm) as LocalDatabase;
        var monitor = type.GetField("_monitor", flags)?.GetValue(mainForm) as ChatGptMonitorService;
        var chrome = type.GetField("_chrome", flags)?.GetValue(mainForm) as ChromeDevToolsService;
        if (database is null || monitor is null || chrome is null) return false;

        Configure(database, monitor, chrome);
        return Controller is not null;
    }
}
