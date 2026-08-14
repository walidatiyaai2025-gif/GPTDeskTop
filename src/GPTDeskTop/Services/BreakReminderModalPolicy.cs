namespace GPTDeskTop.Services;

public static class BreakReminderModalPolicy
{
    private static readonly string[] ReminderMarkers =
    [
        "just checking in",
        "you've been chatting a while",
        "is this a good time for a break"
    ];

    public static bool IsKnownDismissibleReminder(string? heading, string? body)
    {
        var text = $"{heading} {body}";
        return ReminderMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsSecurityOrHumanVerification(string? text) =>
        HumanVerificationDetector.IsRequired(text);
}
