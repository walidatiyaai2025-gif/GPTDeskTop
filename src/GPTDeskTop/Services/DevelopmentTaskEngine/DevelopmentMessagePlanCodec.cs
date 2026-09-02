using System.Text.Json;

namespace GPTDeskTop.Services.DevelopmentTaskEngine;

/// <summary>
/// Portable format for Development Messages plans. JSON preserves arbitrary multiline
/// prompts. Plain text is also accepted: a line containing only --- separates prompts;
/// without a separator the entire clipboard/file is one multiline prompt.
/// </summary>
public static class DevelopmentMessagePlanCodec
{
    public static IReadOnlyList<string> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var source = text.Trim();

        try
        {
            var document = JsonSerializer.Deserialize<MessageDocument>(
                source,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var documentMessages = Normalize(document?.Messages);
            if (documentMessages.Count > 0) return documentMessages;
        }
        catch (JsonException) { }

        try
        {
            var array = JsonSerializer.Deserialize<List<string>>(source);
            var arrayMessages = Normalize(array);
            if (arrayMessages.Count > 0) return arrayMessages;
        }
        catch (JsonException) { }

        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var messages = new List<string>();
        var block = new List<string>();
        foreach (var line in normalized.Split('\n'))
        {
            if (string.Equals(line.Trim(), "---", StringComparison.Ordinal))
            {
                AddBlock(messages, block);
                block.Clear();
                continue;
            }
            block.Add(line);
        }
        AddBlock(messages, block);
        return messages;
    }

    public static string Serialize(IEnumerable<string> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return JsonSerializer.Serialize(
            new MessageDocument { Messages = Normalize(messages) },
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static List<string> Normalize(IEnumerable<string>? messages)
        => messages?
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Select(message => message.Trim())
            .ToList() ?? [];

    private static void AddBlock(List<string> messages, List<string> block)
    {
        var value = string.Join(Environment.NewLine, block).Trim();
        if (!string.IsNullOrWhiteSpace(value)) messages.Add(value);
    }

    private sealed class MessageDocument
    {
        public List<string> Messages { get; set; } = [];
    }
}
