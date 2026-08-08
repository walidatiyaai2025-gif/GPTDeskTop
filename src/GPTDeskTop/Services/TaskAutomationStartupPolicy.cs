namespace GPTDeskTop.Services;

public static class TaskAutomationStartupPolicy
{
    public static bool ShouldResume(string? phase)
    {
        return string.Equals(phase, "Working", StringComparison.OrdinalIgnoreCase)
            || string.Equals(phase, "Cooling", StringComparison.OrdinalIgnoreCase)
            || string.Equals(phase, "Paused", StringComparison.OrdinalIgnoreCase);
    }
}
