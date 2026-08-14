using GPTDeskTop.Services;
using Xunit;

namespace GPTDeskTop.RuntimeTests;

public sealed class GitHubTokenProtectorTests
{
    [Fact]
    public void TokenRoundTripsForCurrentWindowsUser()
    {
        const string token = "github_pat_test_only_123456";
        var protectedValue = GitHubTokenProtector.Protect(token);
        Assert.NotEqual(token, protectedValue);
        Assert.False(string.IsNullOrWhiteSpace(protectedValue));
        Assert.Equal(token, GitHubTokenProtector.Unprotect(protectedValue));
    }

    [Fact]
    public void EmptyTokenRemainsEmpty()
    {
        Assert.Equal(string.Empty, GitHubTokenProtector.Protect(string.Empty));
        Assert.Equal(string.Empty, GitHubTokenProtector.Unprotect(string.Empty));
    }
}
