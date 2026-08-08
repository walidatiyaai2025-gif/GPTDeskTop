using System.Text;
using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

/// <summary>
/// Builds a bounded, deterministic continuation message when a monitor must move
/// from a context-limited ChatGPT conversation to a fresh chat. It does not call
/// external AI APIs and does not attempt to bypass usage, rate, or context limits.
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
        var logs = await _database.GetRecentLogsForMonitorAsync(monitor.Id, 12, cancellationToken);
        var builder = new StringBuilder(maxChars + 256);

        builder.AppendLine("نحن نكمل محادثة قديمة في ChatGPT بعد أن وصلت المحادثة السابقة إلى حد السياق/طول المحادثة. لا تعتبر هذه محادثة جديدة من حيث المهمة؛ اعتبر المعلومات التالية سياقًا منقولًا من المحادثة السابقة.");
        builder.AppendLine("تابع من حيث توقفنا، ولا تبدأ من الصفر. لا تدّعي أنك تملك رسائل غير موجودة في السياق أدناه. إذا كان هناك نقص حقيقي في المعلومات، اطلب فقط الجزء الضروري.");
        builder.AppendLine($"Monitor: #{monitor.Id} | Previous chat: {previousTab.Title} | Continuation: {monitor.RotationCount + 1}");
        builder.AppendLine();
        builder.AppendLine("آخر رد أدى إلى الانتقال:");
        builder.AppendLine(Trim(triggerResponse, Math.Min(1800, maxChars / 3)));
        builder.AppendLine();
        builder.AppendLine("السجل القريب للمحادثة:");

        foreach (var log in logs)
        {
            if (log.Direction == "System" && log.Status.Contains("Refresh", StringComparison.OrdinalIgnoreCase))
                continue;

            var content = !string.IsNullOrWhiteSpace(log.Response) ? log.Response : log.Prompt;
            if (string.IsNullOrWhiteSpace(content))
                continue;

            builder.Append('[').Append(log.Direction).Append("] ");
            builder.AppendLine(Trim(content, 900));
        }

        builder.AppendLine();
        builder.AppendLine("تعليمات الاستمرارية: حافظ على الهدف والقرارات السابقة، واستمر في تنفيذ الخطوة التالية. إذا كان الرد السابق متعلقًا بالبرمجة، لا تعيد تصميم ما اكتمل ولا تكرر العمل المنجز.");
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
