using System.Text.Json;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public static class ChromeProfileCatalog
{
    public static IReadOnlyList<ChromeProfileInfo> Discover()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var chromeUserData = Path.Combine(localAppData, "Google", "Chrome", "User Data");
        var managedRoot = Path.Combine(localAppData, "GPTDeskTop", "ChromeProfiles");
        var profiles = new List<ChromeProfileInfo>();

        var localStatePath = Path.Combine(chromeUserData, "Local State");
        if (File.Exists(localStatePath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(localStatePath));
                if (document.RootElement.TryGetProperty("profile", out var profileNode)
                    && profileNode.TryGetProperty("info_cache", out var infoCache)
                    && infoCache.ValueKind == JsonValueKind.Object)
                {
                    foreach (var entry in infoCache.EnumerateObject())
                    {
                        var sourceDirectory = Path.Combine(chromeUserData, entry.Name);
                        if (!Directory.Exists(sourceDirectory)) continue;

                        var displayName = ReadString(entry.Value, "name") ?? entry.Name;
                        var email = ReadString(entry.Value, "user_name")
                            ?? ReadString(entry.Value, "gaia_name")
                            ?? string.Empty;
                        var key = SanitizeKey(entry.Name);
                        profiles.Add(new ChromeProfileInfo(
                            key,
                            displayName,
                            email,
                            sourceDirectory,
                            Path.Combine(managedRoot, key)));
                    }
                }
            }
            catch (JsonException)
            {
                // Fall back to directory discovery below. A damaged Local State file must never
                // make the monitor attach to a random Chrome profile.
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        if (profiles.Count == 0 && Directory.Exists(chromeUserData))
        {
            try
            {
                foreach (var directory in Directory.EnumerateDirectories(chromeUserData))
                {
                    var name = Path.GetFileName(directory);
                    if (!string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase)
                        && !name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var key = SanitizeKey(name);
                    profiles.Add(new ChromeProfileInfo(
                        key,
                        name,
                        string.Empty,
                        directory,
                        Path.Combine(managedRoot, key)));
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        if (profiles.Count == 0)
        {
            profiles.Add(new ChromeProfileInfo(
                "Default",
                "Chrome Default",
                string.Empty,
                Path.Combine(chromeUserData, "Default"),
                Path.Combine(managedRoot, "Default")));
        }

        return profiles
            .OrderBy(profile => profile.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(profile => profile.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ReadString(JsonElement node, string propertyName)
        => node.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string SanitizeKey(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Default" : sanitized;
    }
}
