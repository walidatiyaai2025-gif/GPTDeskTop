using System.Text.Json;
using GPTDeskTop.Configuration;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class SupportBundlePrivacyTests
{
    [Fact]
    public void DatabaseProjectionKeepsCountsButDropsConversationContent()
    {
        const string secretTitle = "SECRET-MONITOR-TITLE";
        const string secretUrl = "https://chatgpt.com/c/SECRET-CONVERSATION";
        const string secretReply = "SECRET-AUTO-REPLY";
        const string secretPrompt = "SECRET-PROMPT";
        const string secretResponse = "SECRET-RESPONSE";
        const string secretTabId = "SECRET-TAB-ID";

        var monitors = new[]
        {
            new SavedMonitor
            {
                Id = 17,
                Title = secretTitle,
                Url = secretUrl,
                AutoReply = secretReply,
                Enabled = true,
                ConversationRotationEnabled = true,
                ModelRoutingEnabled = true
            },
            new SavedMonitor
            {
                Id = 18,
                Title = "another secret title",
                Url = "https://chatgpt.com/c/another-secret",
                AutoReply = "another secret reply",
                Enabled = false,
                ConversationRotationEnabled = false,
                ModelRoutingEnabled = false
            }
        };
        var logs = new[]
        {
            new MessageLog
            {
                Id = 1,
                Timestamp = new DateTime(2026, 8, 9, 9, 0, 0, DateTimeKind.Utc),
                MonitorId = 17,
                TabId = secretTabId,
                TabTitle = secretTitle,
                Direction = "Inbound",
                Prompt = secretPrompt,
                Response = secretResponse,
                Status = "Detected"
            },
            new MessageLog
            {
                Id = 2,
                Timestamp = new DateTime(2026, 8, 9, 9, 1, 0, DateTimeKind.Utc),
                MonitorId = 17,
                Direction = "System",
                Prompt = "PRIVATE-SYSTEM-PROMPT",
                Response = "PRIVATE-SYSTEM-RESPONSE",
                Status = "NoResponseRefresh"
            }
        };

        var snapshot = SupportBundleService.CreateDatabaseSnapshot(
            monitors,
            logs,
            id => id == 17);
        var json = JsonSerializer.Serialize(snapshot);

        Assert.True(snapshot.Reachable);
        Assert.Equal(2, snapshot.SavedMonitorCount);
        Assert.Equal(1, snapshot.EnabledMonitorCount);
        Assert.Equal(1, snapshot.RunningMonitorCount);
        Assert.Equal(1, snapshot.RotationEnabledCount);
        Assert.Equal(1, snapshot.ModelRoutingEnabledCount);
        Assert.Equal(2, snapshot.RecentHistoryCount);
        Assert.Contains(snapshot.DirectionCounts, item => item.Key == "Inbound" && item.Count == 1);
        Assert.Contains(snapshot.DirectionCounts, item => item.Key == "System" && item.Count == 1);
        Assert.Contains(snapshot.StatusCounts, item => item.Key == "Detected" && item.Count == 1);

        Assert.DoesNotContain(secretTitle, json, StringComparison.Ordinal);
        Assert.DoesNotContain(secretUrl, json, StringComparison.Ordinal);
        Assert.DoesNotContain(secretReply, json, StringComparison.Ordinal);
        Assert.DoesNotContain(secretPrompt, json, StringComparison.Ordinal);
        Assert.DoesNotContain(secretResponse, json, StringComparison.Ordinal);
        Assert.DoesNotContain(secretTabId, json, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE-SYSTEM-PROMPT", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE-SYSTEM-RESPONSE", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Prompt\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Response\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"TabTitle\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"TabId\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationProjectionHidesHostsAndLocalPaths()
    {
        var config = new AppConfig
        {
            Chrome = new ChromeConfig
            {
                DebuggingBaseUrl = "https://secret.internal.example:9443/private",
                DebuggingPort = 9443,
                StartUrl = "https://private.customer.example/chat"
            },
            Monitoring = new MonitoringConfig
            {
                PollIntervalMilliseconds = 1250,
                StableResponseMilliseconds = 1750,
                DelayAfterSendMilliseconds = 900
            },
            Database = new DatabaseConfig
            {
                FileName = Path.Combine("private-root", "customer-name", "app.db")
            }
        };

        var snapshot = SupportBundleService.CreateConfigurationSnapshot(config);
        var json = JsonSerializer.Serialize(snapshot);

        Assert.Equal("Remote", snapshot.DebuggingEndpointKind);
        Assert.Equal("https", snapshot.DebuggingScheme);
        Assert.Equal(9443, snapshot.DebuggingPort);
        Assert.Equal("OtherHttps", snapshot.StartUrlKind);
        Assert.Equal("app.db", snapshot.DatabaseFileName);
        Assert.DoesNotContain("secret.internal.example", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private.customer.example", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-root", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("customer-name", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadmeExplicitlyDocumentsPrivacyExclusions()
    {
        var snapshot = new SupportBundleSnapshot(
            "1.0",
            DateTimeOffset.Parse("2026-08-09T09:00:00Z"),
            "1.8.0",
            ".NET 8",
            "Windows",
            "X64",
            "Healthy",
            "Chrome/CDP and SQLite are reachable.",
            new SupportBundleConfigurationSnapshot("Loopback", "http", 9222, "ChatGPT", 1000, 1500, 1200, "appdata.db"),
            new SupportBundleChromeSnapshot(true, 2, 2, null),
            new SupportBundleDatabaseSnapshot(true, 2, 2, 1, 2, 0, 10, null, null, Array.Empty<SupportBundleCount>(), Array.Empty<SupportBundleCount>(), null),
            new SupportBundleExceptionMetadata(false, "exceptions-current.log", 0, null),
            new[]
            {
                "ChatGPT prompts and assistant responses",
                "raw SQLite database contents",
                "raw exception log contents"
            });

        var readme = SupportBundleService.BuildReadme(snapshot);

        Assert.Contains("Privacy-Safe Support Bundle", readme, StringComparison.Ordinal);
        Assert.Contains("ChatGPT prompts and assistant responses", readme, StringComparison.Ordinal);
        Assert.Contains("raw SQLite database contents", readme, StringComparison.Ordinal);
        Assert.Contains("raw exception log contents", readme, StringComparison.Ordinal);
    }
}
