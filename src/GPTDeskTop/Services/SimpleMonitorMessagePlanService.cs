using System.Text;
using System.Text.Json;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public static class SimpleMonitorMessagePlanService
{
    private const int CurrentSchemaVersion = 1;
    private const int MinDelaySeconds = 15;
    private const int MaxDelaySeconds = 3600;
    private const int MaxMessages = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = false,
        WriteIndented = true
    };

    public static SimpleMonitorMessagePlan ParseAndValidate(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("The JSON message-plan file is empty.");

        SimpleMonitorMessagePlan? plan;
        try
        {
            plan = JsonSerializer.Deserialize<SimpleMonitorMessagePlan>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Invalid JSON: {ex.Message}", ex);
        }

        if (plan is null)
            throw new InvalidDataException("The JSON message-plan file did not contain a plan.");
        if (plan.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported schemaVersion '{plan.SchemaVersion}'. Expected {CurrentSchemaVersion}.");
        if (plan.DefaultDelaySeconds is < MinDelaySeconds or > MaxDelaySeconds)
            throw new InvalidDataException($"defaultDelaySeconds must be between {MinDelaySeconds} and {MaxDelaySeconds}.");
        if (plan.Messages is null || plan.Messages.Count == 0)
            throw new InvalidDataException("The plan must contain at least one message.");
        if (plan.Messages.Count > MaxMessages)
            throw new InvalidDataException($"The plan contains too many messages. Maximum: {MaxMessages}.");

        var normalized = new List<SimpleMonitorMessageStep>(plan.Messages.Count);
        for (var index = 0; index < plan.Messages.Count; index++)
        {
            var step = plan.Messages[index] ?? throw new InvalidDataException($"Message #{index + 1} is null.");
            var text = step.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidDataException($"Message #{index + 1} has empty text.");
            if (step.DelaySeconds is < MinDelaySeconds or > MaxDelaySeconds)
                throw new InvalidDataException($"Message #{index + 1} delaySeconds must be between {MinDelaySeconds} and {MaxDelaySeconds} when supplied.");

            normalized.Add(new SimpleMonitorMessageStep
            {
                Label = string.IsNullOrWhiteSpace(step.Label) ? null : step.Label.Trim(),
                Text = text,
                Enabled = step.Enabled,
                DelaySeconds = step.DelaySeconds,
                Sent = step.Sent
            });
        }

        if (!normalized.Any(step => step.Enabled))
            throw new InvalidDataException("The plan must contain at least one enabled message.");

        return new SimpleMonitorMessagePlan
        {
            SchemaVersion = CurrentSchemaVersion,
            Name = string.IsNullOrWhiteSpace(plan.Name) ? "Message Plan" : plan.Name.Trim(),
            Loop = plan.Loop,
            DefaultDelaySeconds = plan.DefaultDelaySeconds,
            Messages = normalized
        };
    }

    public static string Serialize(SimpleMonitorMessagePlan plan)
        => JsonSerializer.Serialize(plan, JsonOptions);

    public static string CreateSampleJson()
    {
        var sample = new SimpleMonitorMessagePlan
        {
            SchemaVersion = CurrentSchemaVersion,
            Name = "Example ChatGPT work sequence",
            Loop = false,
            DefaultDelaySeconds = 15,
            Messages =
            [
                new SimpleMonitorMessageStep
                {
                    Label = "Continue work",
                    Text = "كمل من آخر نقطة مؤكدة واستمر بدون تكرار ما تم إنجازه.",
                    Enabled = true,
                    DelaySeconds = 15,
                    Sent = false
                },
                new SimpleMonitorMessageStep
                {
                    Label = "Check completion",
                    Text = "راجع ما تم، أصلح أي نقص واضح، ثم أكمل المهمة الحالية حتى أقصى نقطة ممكنة.",
                    Enabled = true,
                    DelaySeconds = 20,
                    Sent = false
                },
                new SimpleMonitorMessageStep
                {
                    Label = "Optional disabled step",
                    Text = "هذه رسالة مثال معطلة ولن يتم إرسالها إلا إذا جعلت enabled = true.",
                    Enabled = false,
                    DelaySeconds = 15,
                    Sent = false
                }
            ]
        };
        return Serialize(sample);
    }

    public static string CreateChatGptPrompt()
        => "Create a GPTDeskTop Monitor Only JSON message plan. Return ONLY valid JSON, no markdown fences and no explanation. Use schemaVersion 1. Keep every delaySeconds and defaultDelaySeconds between 15 and 3600 seconds. Use the messages array in the exact execution order. Each message object must contain text and enabled, and may contain label and delaySeconds. You may omit sent or set sent=false; sent is owned by GPTDeskTop runtime and must never be set true by generated input. Set loop=false for a durable one-time sequence that resumes after restart without repeating confirmed messages. Set loop=true only when intentional repetition is required.";

    public static string BuildPreview(SimpleMonitorMessagePlan plan)
    {
        var enabledCount = plan.Messages.Count(step => step.Enabled);
        var sentCount = plan.Messages.Count(step => step.Enabled && step.Sent);
        var pendingCount = plan.Messages.Count(step => step.Enabled && (!step.Sent || plan.Loop));
        var builder = new StringBuilder();
        builder.AppendLine($"Plan: {plan.Name}");
        builder.AppendLine($"Mode: {(plan.Loop ? "Loop" : "Run once / durable resume")}");
        builder.AppendLine($"Default delay: {plan.DefaultDelaySeconds} sec");
        builder.AppendLine($"Messages: {plan.Messages.Count} total / {enabledCount} enabled / {sentCount} sent / {pendingCount} pending");
        builder.AppendLine();

        for (var index = 0; index < plan.Messages.Count; index++)
        {
            var step = plan.Messages[index];
            var label = string.IsNullOrWhiteSpace(step.Label) ? $"Message {index + 1}" : step.Label;
            var state = step.Sent ? "SENT" : step.Enabled ? "ON" : "OFF";
            builder.AppendLine($"{index + 1}. [{state}] {label} — delay {step.EffectiveDelaySeconds(plan.DefaultDelaySeconds)} sec");
            builder.AppendLine(Compact(step.Text));
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string Compact(string value)
    {
        var singleLine = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return singleLine.Length <= 180 ? singleLine : singleLine[..177] + "...";
    }
}
