using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class RuntimeHealthPresentationTests
{
    [Fact]
    public void HealthyWhenChromeAndDatabaseAreReachable()
    {
        var snapshot = RuntimeHealthPresentation.Create(
            chromeReachable: true,
            databaseReachable: true,
            chatGptTabCount: 2,
            savedMonitorCount: 3,
            runningMonitorCount: 2,
            checkedAt: DateTimeOffset.Parse("2026-08-09T09:00:00Z"));

        Assert.Equal(RuntimeHealthLevel.Healthy, snapshot.Level);
        Assert.Equal("Chrome/CDP and SQLite are reachable.", snapshot.Summary);
        Assert.Equal(2, snapshot.ChatGptTabCount);
        Assert.Equal(3, snapshot.SavedMonitorCount);
        Assert.Equal(2, snapshot.RunningMonitorCount);
        Assert.False(snapshot.CrashRecoveryPending);
        Assert.Equal(0, snapshot.InvalidMonitorIdentityCount);
    }

    [Fact]
    public void HealthyAllowsEmptyWorkspaceWhenNoMonitorsAreSaved()
    {
        var snapshot = RuntimeHealthPresentation.Create(true, true, 0, 0, 0, DateTimeOffset.UtcNow);
        Assert.Equal(RuntimeHealthLevel.Healthy, snapshot.Level);
    }

    [Fact]
    public void DegradedWhenSavedMonitorsHaveNoOpenChatGptTab()
    {
        var snapshot = RuntimeHealthPresentation.Create(true, true, 0, 2, 0, DateTimeOffset.UtcNow);
        Assert.Equal(RuntimeHealthLevel.Degraded, snapshot.Level);
        Assert.Contains("no ChatGPT tab", snapshot.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidMonitorIdentityBlocksRecoveryAndForcesDegradedHealth()
    {
        var snapshot = RuntimeHealthPresentation.Create(
            true,
            true,
            3,
            4,
            2,
            DateTimeOffset.UtcNow,
            crashRecoveryPending: true,
            invalidMonitorIdentityCount: 2);

        Assert.Equal(RuntimeHealthLevel.Degraded, snapshot.Level);
        Assert.True(snapshot.CrashRecoveryPending);
        Assert.Equal(2, snapshot.InvalidMonitorIdentityCount);
        Assert.Contains("blocked by 2", snapshot.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rebind", snapshot.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PendingRecoveryWithoutIdentityBlockerIsStillDegraded()
    {
        var snapshot = RuntimeHealthPresentation.Create(
            true,
            true,
            1,
            1,
            1,
            DateTimeOffset.UtcNow,
            crashRecoveryPending: true);

        Assert.Equal(RuntimeHealthLevel.Degraded, snapshot.Level);
        Assert.True(snapshot.CrashRecoveryPending);
        Assert.Equal(0, snapshot.InvalidMonitorIdentityCount);
        Assert.Contains("pending", snapshot.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false, true, "Chrome/CDP")]
    [InlineData(true, false, "SQLite")]
    public void DegradedWhenOneDependencyIsUnavailable(bool chrome, bool database, string expectedText)
    {
        var snapshot = RuntimeHealthPresentation.Create(
            chrome,
            database,
            1,
            1,
            1,
            DateTimeOffset.UtcNow,
            chrome ? null : "connection refused",
            database ? null : "database locked");

        Assert.Equal(RuntimeHealthLevel.Degraded, snapshot.Level);
        Assert.Contains(expectedText, snapshot.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnavailableWhenBothDependencyProbesFail()
    {
        var snapshot = RuntimeHealthPresentation.Create(
            false,
            false,
            0,
            5,
            9,
            DateTimeOffset.UtcNow,
            " chrome failed ",
            " db failed ");

        Assert.Equal(RuntimeHealthLevel.Unavailable, snapshot.Level);
        Assert.Equal(5, snapshot.RunningMonitorCount);
        Assert.Equal("chrome failed", snapshot.ChromeError);
        Assert.Equal("db failed", snapshot.DatabaseError);
    }

    [Theory]
    [InlineData("https://chatgpt.com/c/abc", true)]
    [InlineData("https://www.chatgpt.com/", true)]
    [InlineData("https://chat.openai.com/c/legacy", true)]
    [InlineData("https://example.com/chatgpt.com", false)]
    [InlineData("not-a-url", false)]
    [InlineData("", false)]
    public void ChatGptUrlDetectionUsesHostNotSubstring(string url, bool expected)
    {
        Assert.Equal(expected, RuntimeHealthPresentation.IsChatGptTabUrl(url));
    }

    [Theory]
    [InlineData("https://chatgpt.com/c/abc123", true)]
    [InlineData("https://chatgpt.com/c/abc123?model=auto#message", true)]
    [InlineData("https://chatgpt.com/g/g-example/c/abc123", true)]
    [InlineData("https://chat.openai.com/c/legacy123", true)]
    [InlineData("https://workspace.chatgpt.com/g/g-example/c/abc123", true)]
    [InlineData("https://chatgpt.com/", false)]
    [InlineData("https://chatgpt.com/c/", false)]
    [InlineData("https://chatgpt.com/share/abc123", false)]
    [InlineData("http://chatgpt.com/c/abc123", false)]
    [InlineData("https://chatgpt.com.evil.example/c/abc123", false)]
    [InlineData("https://example.com/c/abc123", false)]
    [InlineData("not-a-url", false)]
    [InlineData("", false)]
    public void ConversationIdentityRequiresHttpsTrustedHostAndConversationPath(string url, bool expected)
    {
        Assert.Equal(expected, RuntimeHealthPresentation.IsChatGptConversationUrl(url));
    }

    [Fact]
    public void NegativeCountsAreClampedAndRunningCannotExceedSaved()
    {
        var snapshot = RuntimeHealthPresentation.Create(
            true,
            true,
            -1,
            2,
            99,
            DateTimeOffset.UtcNow,
            invalidMonitorIdentityCount: 99);

        Assert.Equal(0, snapshot.ChatGptTabCount);
        Assert.Equal(2, snapshot.SavedMonitorCount);
        Assert.Equal(2, snapshot.RunningMonitorCount);
        Assert.Equal(2, snapshot.InvalidMonitorIdentityCount);
    }
}