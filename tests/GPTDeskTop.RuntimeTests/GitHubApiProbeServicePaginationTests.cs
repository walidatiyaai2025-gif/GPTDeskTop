using System.Net;
using System.Text;
using System.Text.Json;
using GPTDeskTop.Services;
using Xunit;

namespace GPTDeskTop.RuntimeTests;

public sealed class GitHubApiProbeServicePaginationTests
{
    [Fact]
    public async Task TestAsyncAcceptsBranchFromSecondPage()
    {
        var handler = new PagingGitHubHandler();
        var service = new GitHubApiProbeService(handler);
        var settings = new GitHubIntegrationSettings(
            "owner/repo",
            "target/branch",
            WatchCommits: true,
            WatchPullRequests: true,
            WatchIssues: true,
            Token: "test-token");

        var result = await service.TestAsync(settings);

        Assert.True(result.Success, result.Message);
        Assert.Contains("target/branch", result.Branches);
        Assert.Equal(2, handler.BranchRequests);
    }

    [Fact]
    public async Task BranchDiscoveryKeepsCaseDistinctRefsAndStopsAfterShortPage()
    {
        var handler = new PagingGitHubHandler();
        var service = new GitHubApiProbeService(handler);
        var settings = new GitHubIntegrationSettings(
            "owner/repo",
            "Alpha",
            WatchCommits: true,
            WatchPullRequests: true,
            WatchIssues: true,
            Token: "test-token");

        var result = await service.TestAsync(settings);

        Assert.True(result.Success, result.Message);
        Assert.Contains("Alpha", result.Branches);
        Assert.Contains("alpha", result.Branches);
        Assert.Equal(2, result.Branches.Count(x => string.Equals(x, "alpha", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(2, handler.BranchRequests);
    }

    private sealed class PagingGitHubHandler : HttpMessageHandler
    {
        public int BranchRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.PathAndQuery ?? string.Empty;
            string json;

            if (path == "/user")
            {
                json = "{\"login\":\"test-user\"}";
            }
            else if (path == "/repos/owner/repo")
            {
                json = "{\"default_branch\":\"main\",\"private\":false}";
            }
            else if (path == "/repos/owner/repo/branches?per_page=100&page=1")
            {
                BranchRequests++;
                json = JsonSerializer.Serialize(
                    Enumerable.Range(1, 100).Select(i => new { name = $"branch-{i:D3}" }));
            }
            else if (path == "/repos/owner/repo/branches?per_page=100&page=2")
            {
                BranchRequests++;
                json = "[{\"name\":\"target/branch\"},{\"name\":\"Alpha\"},{\"name\":\"alpha\"}]";
            }
            else
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{\"message\":\"unexpected test request\"}", Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
