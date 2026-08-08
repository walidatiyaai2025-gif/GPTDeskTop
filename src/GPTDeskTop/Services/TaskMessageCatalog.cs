using System.Text.Json;

namespace GPTDeskTop.Services;

public sealed class TaskMessageCatalog
{
    private readonly IReadOnlyList<string> _messages;
    private int _nextIndex;

    private TaskMessageCatalog(IReadOnlyList<string> messages)
    {
        _messages = messages;
    }

    public IReadOnlyList<string> Messages => _messages;

    public string GetNextMessage()
    {
        if (_messages.Count == 0)
            throw new InvalidOperationException("The task message catalog is empty.");

        var index = Interlocked.Increment(ref _nextIndex) - 1;
        return _messages[index % _messages.Count];
    }

    public static TaskMessageCatalog Load(string path)
    {
        if (!File.Exists(path))
            return new TaskMessageCatalog(Array.Empty<string>());

        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<TaskMessageDocument>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        var messages = document?.Messages?
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Select(message => message.Trim())
            .ToArray()
            ?? Array.Empty<string>();

        return new TaskMessageCatalog(messages);
    }

    private sealed class TaskMessageDocument
    {
        public List<string> Messages { get; set; } = new();
    }
}
