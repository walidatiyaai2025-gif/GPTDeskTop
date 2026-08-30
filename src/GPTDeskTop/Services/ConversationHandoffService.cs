using System.Text;
using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

/// <summary>
/// Builds a bounded, deterministic continuation message whenever a monitor must move
/// to a fresh ChatGPT conversation. The configured continuation directive is preserved
/// verbatim at the beginning of the physical message, followed by a canonical one-line
/// checkpoint so exact rendered-message verification remains reliable.
/// </summary>
public sealed class ConversationHandoffService
{
    public const string CheckpointMarker = "[HANDOFF-CHECKPOINT]";

    private readonly LocalDatabase _database;

    public ConversationHandoffService(LocalDatabase database) => _database = database;

    public Task<string> BuildAsync(
        SavedMonitor monitor,
        string triggerResponse,
        ChromeTab previousTab,
        CancellationToken cancellationToken = default)
        => BuildAsync(monitor, triggerResponse, previousTab, string.Empty, cancellationToken);

    public async Task<string> BuildAsync(
        SavedMonitor monitor,
        string triggerResponse,
        ChromeTab previousTab,
        string leadingDirective,
        CancellationToken cancellationToken = default)
    {
        var directive = Canonicalize(leadingDirective);
        if (await _database.GetSettingAsync("HandoffEnabled", cancellationToken) != "1")
            return directive;

        var maxChars = await _database.GetIntSettingAsync("HandoffMaxChars", 7000, 1500, 20000, cancellationToken);
        var logs = await _database.GetRecentLogsForMonitorAsync(monitor.Id, 16, cancellationToken);
        var lastConfirmedInbound = logs.LastOrDefault(log =>
            string.Equals(log.Direction, "Inbound", StringComparison.OrdinalIgnoreCase)
            && !log.Status.Contains("Error", StringComparison.OrdinalIgnoreCase)
            && !log.Status.Contains("Deferred", StringComparison.OrdinalIgnoreCase));
        var lastConfirmedOutbound = logs.LastOrDefault(log =>
            string.Equals(log.Direction, "Outbound", StringComparison.OrdinalIgnoreCase)
            && !log.Status.Contains("Failed", StringComparison.OrdinalIgnoreCase)
            && !log.Status.Contains("Deferred", StringComparison.OrdinalIgnoreCase));

        var builder = new StringBuilder(Math.Min(maxChars + 256, 21000));
        if (!string.IsNullOrWhiteSpace(directive))
            builder.Append(directive).Append(' ');

        builder.Append(CheckpointMarker)
            .Append(" Source conversation=").Append(Canonicalize(monitor.Url))
            .Append("; Previous chat=").Append(Canonicalize(previousTab.Title))
            .Append("; Monitor=").Append(monitor.Id)
            .Append("; Continuation=").Append(monitor.RotationCount + 1)
            .Append("; نقطة الاستكمال المؤكدة=")
            .Append(Canonicalize(lastConfirmedInbound?.Response ?? "[لا يوجد رد Inbound مؤكد في السجل القريب]"))
            .Append("; آخر طلب/تعليمات Outbound مؤكدة=")
            .Append(Canonicalize(lastConfirmedOutbound?.Prompt ?? "[غير متاح]"))
            .Append("; سبب الانتقال=").Append(Canonicalize(triggerResponse))
            .Append("; السجل القريب=");

        foreach (var log in logs)
        {
            if (log.Direction == "System" &&
                (log.Status.Contains("Refresh", StringComparison.OrdinalIgnoreCase)
                 || log.Status.Contains("HandoffCheckpoint", StringComparison.OrdinalIgnoreCase)))
                continue;

            var content = !string.IsNullOrWhiteSpace(log.Response) ? log.Response : log.Prompt;
            if (string.IsNullOrWhiteSpace(content))
                continue;

            builder.Append('[').Append(Canonicalize(log.Direction)).Append('/').Append(Canonicalize(log.Status)).Append("] ")
                .Append(TrimCanonical(content, 700)).Append(" | ");
        }

        builder.Append("تعليمات الاستمرارية=تابع من آخر نقطة مؤكدة ولا تبدأ من الصفر ولا تكرر ما تم إنجازه؛ إذا كان العمل برمجيًا فاستمر في الخطوة التالية فقط.");

        var result = Canonicalize(builder.ToString());
        if (result.Length <= maxChars)
            return result;

        var suffix = " [HANDOFF-CHECKPOINT-TRUNCATED]";
        var take = Math.Max(1, maxChars - suffix.Length);
        return result[..take].TrimEnd() + suffix;
    }

    private static string Canonicalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch) || ch == '\u200b')
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }
            builder.Append(ch);
        }

        return builder.ToString().Trim();
    }

    private static string TrimCanonical(string value, int maxChars)
    {
        var canonical = Canonicalize(value);
        if (canonical.Length <= maxChars)
            return canonical;
        return canonical[..Math.Max(1, maxChars - 14)].TrimEnd() + " […truncated…]";
    }
}
