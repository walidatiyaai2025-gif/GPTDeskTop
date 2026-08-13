namespace GPTDeskTop.Services;

public static class ModelDelayedResponseDetector
{
    private static readonly string[] Markers =
    [
        "our systems are thinking a bit more about this request",
        "you can retry with a faster model",
        "quicker response"
    ];

    public static bool IsDelayed(string? visibleText) =>
        !string.IsNullOrWhiteSpace(visibleText)
        && Markers.Any(marker => visibleText.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
