using System.Globalization;
using System.Text;
using GPTDeskTop.Models;

namespace GPTDeskTop.UI;

public static class HistoryWorkspaceLogic
{
    public const string All = "All";
    public const string Issues = "Issues";
    public const string Success = "Success";
    public const string Deferred = "Deferred";
    public const string Other = "Other";

    public static IReadOnlyList<MessageLog> Filter(
        IEnumerable<MessageLog> source,
        string? searchText,
        string? flowFilter,
        string? statusFilter)
    {
        ArgumentNullException.ThrowIfNull(source);

        var search = searchText?.Trim() ?? string.Empty;
        var flow = string.IsNullOrWhiteSpace(flowFilter) ? All : flowFilter.Trim();
        var status = string.IsNullOrWhiteSpace(statusFilter) ? All : statusFilter.Trim();

        return source.Where(log =>
        {
            if (!string.Equals(flow, All, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(log.Direction, flow, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.Equals(status, All, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(GetStatusCategory(log.Status), status, StringComparison.OrdinalIgnoreCase))
                return false;

            if (search.Length == 0) return true;

            return Contains(log.TabTitle, search)
                   || Contains(log.Direction, search)
                   || Contains(log.Prompt, search)
                   || Contains(log.Response, search)
                   || Contains(log.Status, search)
                   || Contains(log.TabId, search)
                   || Contains(log.MonitorId?.ToString(CultureInfo.InvariantCulture), search)
                   || Contains(log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), search);
        }).ToList();
    }

    public static string GetStatusCategory(string? status)
    {
        var value = status ?? string.Empty;
        if (ContainsAny(value, "error", "failed", "failure", "timeout", "exception", "crash", "fatal"))
            return Issues;
        if (ContainsAny(value, "sent", "success", "verified", "recovered", "rotated", "completed", "complete", "delivered"))
            return Success;
        if (ContainsAny(value, "deferred", "limit", "retry", "pending", "waiting", "paused", "cooling"))
            return Deferred;
        return Other;
    }

    public static string ToClipboardText(MessageLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        var builder = new StringBuilder();
        builder.AppendLine($"Time: {log.Timestamp:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"Monitor: {log.MonitorId?.ToString(CultureInfo.InvariantCulture) ?? "—"}");
        builder.AppendLine($"Chat: {log.TabTitle}");
        builder.AppendLine($"Tab ID: {log.TabId}");
        builder.AppendLine($"Flow: {log.Direction}");
        builder.AppendLine($"Status: {log.Status}");
        builder.AppendLine("Prompt:");
        builder.AppendLine(log.Prompt ?? string.Empty);
        builder.AppendLine("Response:");
        builder.Append(log.Response ?? string.Empty);
        return builder.ToString();
    }

    public static string ToCsv(IEnumerable<MessageLog> logs)
    {
        ArgumentNullException.ThrowIfNull(logs);
        var builder = new StringBuilder();
        builder.AppendLine("Time,MonitorId,TabId,Chat,Flow,Prompt,Response,Status");
        foreach (var log in logs)
        {
            builder.Append(EscapeCsv(log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append(',')
                .Append(EscapeCsv(log.MonitorId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)).Append(',')
                .Append(EscapeCsv(log.TabId)).Append(',')
                .Append(EscapeCsv(log.TabTitle)).Append(',')
                .Append(EscapeCsv(log.Direction)).Append(',')
                .Append(EscapeCsv(log.Prompt)).Append(',')
                .Append(EscapeCsv(log.Response)).Append(',')
                .Append(EscapeCsv(log.Status)).AppendLine();
        }
        return builder.ToString();
    }

    public static string EscapeCsv(string? value)
    {
        var text = value ?? string.Empty;
        if (!text.Contains(',') && !text.Contains('"') && !text.Contains('\r') && !text.Contains('\n'))
            return text;
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private static bool Contains(string? value, string search)
        => value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

    private static bool ContainsAny(string value, params string[] terms)
        => terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}
