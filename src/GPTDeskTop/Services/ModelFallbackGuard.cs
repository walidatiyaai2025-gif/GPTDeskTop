namespace GPTDeskTop.Services;
public static class ModelFallbackGuard
{
    public static bool CanAutoSwitch(bool projectAllowsAutoSwitch, int delayedOccurrences, int threshold = 2) =>
        projectAllowsAutoSwitch && delayedOccurrences >= threshold;
}
