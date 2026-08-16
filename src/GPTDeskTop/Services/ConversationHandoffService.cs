using System.Text;
using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

/// <summary>
/// Builds a bounded, deterministic continuation message whenever a monitor must move
/// to a fresh ChatGPT conversation. The packet carries the latest confirmed work state,
/// not only the error/limit that triggered the handoff.
/// </summary>
public sealed class ConversationHandoffService
{
    private readonly LocalDatabase _database;

    public ConversationHandoffService(LocalDatabase database) => _database = database;

    public async Task<string> BuildAsync(
        SavedMonitor monitor,
        string triggerResponse,
        ChromeTab previousTab,
        CancellationToken cancellationToken = default)
    {
        if (await _database.GetSettingAsync("HandoffEnabled", cancellationToken) != "1")
            return string.Empty;

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

        var builder = new StringBuilder(maxChars + 512);
        builder.AppendLine("هذه رسالة استمرارية من محادثة ChatGPT سابقة. اعتبر المهمة نفسها مستمرة ولا تبدأ من الصفر.");
        builder.AppendLine("تابع من آخر نقطة مؤكدة أدناه، ولا تكرر ما تم إنجازه. لا تدّعي امتلاك سياق غير موجود في هذه الرسالة.");
        builder.AppendLine($"Monitor: #{monitor.Id} | Previous chat: {previousTab.Title} | Source conversation: {monitor.Url} | Continuation: {monitor.RotationCount + 1}");
        builder.AppendLine();
        builder.AppendLine("نقطة الاستكمال المؤكدة:");
        builder.AppendLine(lastConfirmedInbound is null
            ? "[لا يوجد رد Inbound مؤكد في السجل القريب؛ استخدم السجل المنقول أدناه لتحديد آخر نقطة مؤكدة.]"
            : Trim(lastConfirmedInbound.Response, Math.Min(2200, maxChars / 3)));
        builder.AppendLine();
        builder.AppendLine("آخر طلب/تعليمات Outbound مؤكدة قبل الانتقال:");
        builder.AppendLine(lastConfirmedOutbound is null
            ? "[غير متاح]"
            : Trim(lastConfirmedOutbound.Prompt, Math.Min(1400, maxChars / 4)));
        builder.AppendLine();
        builder.AppendLine("سبب الانتقال / آخر حالة ظهرت:");
        builder.AppendLine(Trim(triggerResponse, Math.Min(1200, maxChars / 5)));
        builder.AppendLine();
        builder.AppendLine("السجل القريب للمحادثة:");

        foreach (var log in logs)
        {
            if (log.Direction == "System" &&
                (log.Status.Contains("Refresh", StringComparison.OrdinalIgnoreCase)
                 || log.Status.Contains("HandoffCheckpoint", StringComparison.OrdinalIgnoreCase)))
                continue;

            var content = !string.IsNullOrWhiteSpace(log.Response) ? log.Response : log.Prompt;
            if (string.IsNullOrWhiteSpace(content))
                continue;

            builder.Append('[').Append(log.Direction).Append('/').Append(log.Status).Append("] ");
            builder.AppendLine(Trim(content, 800));
        }

        builder.AppendLine();
        builder.AppendLine("تعليمات الاستمرارية: حافظ على الهدف والقرارات السابقة، واستمر في تنفيذ الخطوة التالية من نقطة الاستكمال المؤكدة. إذا كان العمل برمجيًا، لا تعيد تصميم أو تنفيذ ما ثبت أنه اكتمل.");
        builder.AppendLine("تعليمات السلامة: لا تحاول تجاوز حدود الاستخدام أو التحايل على قيود الخدمة. إذا ظهر خطأ أو حد جديد، تعامل معه وفق الرسالة الفعلية وبشكل محافظ.");

        var result = builder.ToString();
        return result.Length <= maxChars
            ? result
            : result[..maxChars] + "\n[تم اختصار السياق المنقول للحفاظ على حجم آمن للرسالة.]";
    }

    private static string Trim(string value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "[empty]";

        value = value.Trim();
        return value.Length <= maxChars
            ? value
            : value[..Math.Max(1, maxChars - 80)] + "\n[…truncated…]";
    }
}
