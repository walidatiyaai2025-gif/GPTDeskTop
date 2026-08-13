using System.Reflection;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorHttpRequestTransientRegressionTests
{
    [Fact]
    public void HttpRequestException_IsClassifiedAsTransientChromeFailure()
    {
        var method = typeof(ChatGptMonitorService).GetMethod(
            "IsTransientChromeException",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var result = method!.Invoke(null, [new HttpRequestException("Chrome /json/list temporarily unavailable")]);
        Assert.True((bool)result!);
    }

    [Fact]
    public void OrdinaryInvalidOperationException_RemainsNonTransient()
    {
        var method = typeof(ChatGptMonitorService).GetMethod(
            "IsTransientChromeException",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var result = method!.Invoke(null, [new InvalidOperationException("application invariant failed")]);
        Assert.False((bool)result!);
    }
}
