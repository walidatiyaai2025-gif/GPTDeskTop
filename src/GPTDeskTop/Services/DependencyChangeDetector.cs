namespace GPTDeskTop.Services;
public static class DependencyChangeDetector
{
    public static bool Changed(string? previousFingerprint, string currentFingerprint) => !string.Equals(previousFingerprint, currentFingerprint, StringComparison.Ordinal);
}
