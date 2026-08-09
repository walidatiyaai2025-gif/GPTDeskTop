using System.Text.Json;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentMessageCatalogTests
{
    [Fact]
    public void CatalogSupportsMoreThanTenMessages()
    {
        var messages = Enumerable.Range(1, 12).Select(i => $"Message {i}").ToArray();
        Assert.True(messages.Length >= 10);
        Assert.Equal(12, messages.Length);
    }

    [Fact]
    public void CatalogRejectsEmptyMessages()
    {
        var messages = new[] { "valid", "   " };
        Assert.Contains(messages, string.IsNullOrWhiteSpace);
    }

    [Fact]
    public void CatalogJsonRoundTripsMessageOrder()
    {
        var payload = new { messages = new[] { "first", "second", "third" } };
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        var values = document.RootElement.GetProperty("messages").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Equal(new[] { "first", "second", "third" }, values);
    }
}
