using System.Reflection;

namespace GPTDeskTop.Services;

public static class ApplicationBuildIdentity
{
    private const string StableMetadataToken = "stable";

    public static string ProductVersion => GetProductVersion(typeof(ApplicationBuildIdentity).Assembly);

    public static string? StableBuildId => GetStableBuildId(typeof(ApplicationBuildIdentity).Assembly);

    public static string DisplayVersion => FormatDisplayVersion(ProductVersion, StableBuildId);

    public static string GetProductVersion(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var version = assembly.GetName().Version;
        return version is null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    public static string? GetStableBuildId(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return ParseStableBuildId(informationalVersion);
    }

    public static string? ParseStableBuildId(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion)) return null;

        var metadataSeparator = informationalVersion.IndexOf('+');
        if (metadataSeparator < 0 || metadataSeparator == informationalVersion.Length - 1) return null;

        var metadata = informationalVersion[(metadataSeparator + 1)..]
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var index = 0; index < metadata.Length - 1; index++)
        {
            if (!string.Equals(metadata[index], StableMetadataToken, StringComparison.OrdinalIgnoreCase)) continue;

            var candidate = metadata[index + 1];
            if (candidate.Length is < 7 or > 12) return null;
            if (!candidate.All(Uri.IsHexDigit)) return null;
            return candidate.ToLowerInvariant();
        }

        return null;
    }

    public static string FormatDisplayVersion(string productVersion, string? stableBuildId)
    {
        var version = string.IsNullOrWhiteSpace(productVersion) ? "unknown" : productVersion.Trim();
        return string.IsNullOrWhiteSpace(stableBuildId)
            ? $"v{version}"
            : $"v{version} • stable {stableBuildId.Trim().ToLowerInvariant()}";
    }
}
