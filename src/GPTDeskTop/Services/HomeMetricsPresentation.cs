using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public static class HomeMetricsPresentation
{
    public static HomeMetricsSnapshot BuildSnapshot(
        int crashCount,
        IReadOnlyCollection<SavedMonitor> monitors,
        Func<long, bool> isMonitorRunning)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(isMonitorRunning);

        var running = monitors.Count(monitor => isMonitorRunning(monitor.Id));
        return new HomeMetricsSnapshot(
            $"Crashes\r\n{Math.Max(0, crashCount)}",
            $"Monitors\r\n{running} / {monitors.Count}");
    }

    public static MonitorStatusPresentation? GetStatus(string? value)
    {
        value ??= string.Empty;
        if (value.Contains("Running", StringComparison.OrdinalIgnoreCase))
        {
            return new MonitorStatusPresentation(
                "● Running",
                Color.SeaGreen,
                FontStyle.Bold);
        }

        if (value.Contains("Stopped", StringComparison.OrdinalIgnoreCase))
        {
            return new MonitorStatusPresentation(
                "● Stopped",
                Color.Firebrick,
                FontStyle.Bold);
        }

        return null;
    }
}

public sealed record HomeMetricsSnapshot(string CrashCardText, string MonitorCardText);
public sealed record MonitorStatusPresentation(string Text, Color ForeColor, FontStyle FontStyle);