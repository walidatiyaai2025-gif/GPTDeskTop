using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public static class MonitorConversationOwnership
{
    public static HashSet<long> FindDuplicateMonitorIds(IEnumerable<SavedMonitor>? monitors)
    {
        var duplicateIds = new HashSet<long>();
        if (monitors is null) return duplicateIds;

        foreach (var group in monitors
                     .Where(monitor => monitor.Id > 0 && RuntimeHealthPresentation.IsChatGptConversationUrl(monitor.Url))
                     .GroupBy(monitor => ChatGptConversationIdentity.Normalize(monitor.Url), StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Skip(1).Any()))
        {
            foreach (var monitor in group)
                duplicateIds.Add(monitor.Id);
        }

        return duplicateIds;
    }

    public static int CountDuplicateMonitors(IEnumerable<SavedMonitor>? monitors)
        => FindDuplicateMonitorIds(monitors).Count;

    public static bool IsDuplicateOwner(long monitorId, IEnumerable<SavedMonitor>? monitors)
        => monitorId > 0 && FindDuplicateMonitorIds(monitors).Contains(monitorId);
}