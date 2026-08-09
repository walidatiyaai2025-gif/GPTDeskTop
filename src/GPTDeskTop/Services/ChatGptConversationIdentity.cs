namespace GPTDeskTop.Services;

/// <summary>
/// Compares stable ChatGPT conversation URLs as logical monitor identities.
/// Chrome DevTools target IDs are runtime locators only and must never be used
/// to move a monitor to a different conversation.
/// </summary>
public static class ChatGptConversationIdentity
{
    public static bool IsSame(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;
        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(left)
            || !RuntimeHealthPresentation.IsChatGptConversationUrl(right))
            return false;

        return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var trimmed = value.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return trimmed.TrimEnd('/');

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + uri.Query;
    }
}
