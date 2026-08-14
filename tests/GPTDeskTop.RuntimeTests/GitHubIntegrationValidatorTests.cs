using GPTDeskTop.Services;
using Xunit;

namespace GPTDeskTop.RuntimeTests;

public sealed class GitHubIntegrationValidatorTests
{
    [Theory]
    [InlineData("owner/repo")]
    [InlineData("walidatiyaai2025-gif/GPTDeskTop")]
    public void RepositoryOwnerSlashRepoIsAccepted(string value)
        => Assert.Null(GitHubIntegrationValidator.ValidateRepository(value));

    [Theory]
    [InlineData("")]
    [InlineData("repo")]
    [InlineData("owner/repo/extra")]
    [InlineData("owner /repo")]
    public void InvalidRepositoryIsRejected(string value)
        => Assert.NotNull(GitHubIntegrationValidator.ValidateRepository(value));

    [Fact]
    public void BlankBranchIsRejected()
        => Assert.NotNull(GitHubIntegrationValidator.ValidateBranch("   "));

    [Fact]
    public void RepositoryCanBeSplitForApiCalls()
    {
        var result = GitHubIntegrationValidator.SplitRepository(" owner/repo ");
        Assert.Equal("owner", result.Owner);
        Assert.Equal("repo", result.Repo);
    }
}
