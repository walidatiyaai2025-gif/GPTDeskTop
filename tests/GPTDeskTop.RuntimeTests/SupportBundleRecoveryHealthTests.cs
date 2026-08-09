using System.Text.Json;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class SupportBundleRecoveryHealthTests
{
    [Fact]
    public void RecoveryProjectionExportsOnlyStateAndCountAndMatchesRuntimeHealthSemantics()
    {
        const string invalidTitleMarker = "MONITOR_TITLE_MARKER";
        const string invalidUrlMarker = "https://chatgpt.com/home-marker";
        const string invalidTabMarker = "TAB_MARKER";
        const string validUrlMarker = "https://chatgpt.com/c/conversation-marker";

        var monitors = new[]
        {
            new SavedMonitor
            {
                Id = 71,
                Title = invalidTitleMarker,
                TabId = invalidTabMarker,
                Url = invalidUrlMarker,
                AutoReply = "MESSAGE_MARKER",
                Enabled = true
            },
            new SavedMonitor
            {
                Id = 72,
                Title = "VALID_TITLE_MARKER",
                TabId = "VALID_TAB_MARKER",
                Url = validUrlMarker,
                Enabled = true
            }
        };

        var database = SupportBundleService.CreateDatabaseSnapshot(
            monitors,
            Array.Empty<MessageLog>(),
            _ => false,
            crashRecoveryPending: true);
        var json = JsonSerializer.Serialize(database);

        Assert.True(database.CrashRecoveryPending);
        Assert.Equal(1, database.InvalidMonitorIdentityCount);
        Assert.Contains("\"CrashRecoveryPending\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"InvalidMonitorIdentityCount\":1", json, StringComparison.Ordinal);
        Assert.DoesNotContain(invalidTitleMarker, json, StringComparison.Ordinal);
        Assert.DoesNotContain(invalidUrlMarker, json, StringComparison.Ordinal);
        Assert.DoesNotContain(invalidTabMarker, json, StringComparison.Ordinal);
        Assert.DoesNotContain(validUrlMarker, json, StringComparison.Ordinal);
        Assert.DoesNotContain("MESSAGE_MARKER", json, StringComparison.Ordinal);

        var health = RuntimeHealthPresentation.Create(
            chromeReachable: true,
            databaseReachable: true,
            chatGptTabCount: 2,
            savedMonitorCount: database.SavedMonitorCount,
            runningMonitorCount: database.RunningMonitorCount,
            checkedAt: DateTimeOffset.UtcNow,
            crashRecoveryPending: database.CrashRecoveryPending,
            invalidMonitorIdentityCount: database.InvalidMonitorIdentityCount);

        Assert.Equal(RuntimeHealthLevel.Degraded, health.Level);
        Assert.Contains("blocked by 1", health.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rebind", health.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecoveryProjectionDefaultsRemainBackwardCompatible()
    {
        var database = SupportBundleService.CreateDatabaseSnapshot(
            new[]
            {
                new SavedMonitor
                {
                    Id = 7,
                    Url = "https://chatgpt.com/c/valid-marker"
                }
            },
            Array.Empty<MessageLog>());

        Assert.False(database.CrashRecoveryPending);
        Assert.Equal(0, database.InvalidMonitorIdentityCount);
    }
}