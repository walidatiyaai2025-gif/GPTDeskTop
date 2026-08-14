namespace GPTDeskTop.Services;
public static class HumanVerificationDetector
{
    private static readonly string[] Markers = ["verify you are human", "human verification", "captcha", "security check", "checking your browser"];
    public static bool IsRequired(string? visibleText) => !string.IsNullOrWhiteSpace(visibleText) && Markers.Any(m => visibleText.Contains(m, StringComparison.OrdinalIgnoreCase));
}
