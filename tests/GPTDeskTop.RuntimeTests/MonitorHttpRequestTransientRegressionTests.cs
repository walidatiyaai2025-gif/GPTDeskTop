using System.Reflection;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorHttpRequestTransientRegressionTests
{
    [Fact]
    public void RepeatedHttpRequestException_IsRateLimitedPerMonitor()
    {
        var method = typeof(ExceptionLogService).GetMethod(
            "IsRepeatedMonitorTransportException",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var monitorId = DateTime.UtcNow.Ticks;
        var exception = new HttpRequestException("Chrome /json/list temporarily unavailable");

        var first = (bool)method!.Invoke(null, [exception, "ChatGptMonitorService.MonitorLoop", monitorId])!;
        var second = (bool)method.Invoke(null, [exception, "ChatGptMonitorService.MonitorLoop", monitorId])!;

        Assert.False(first);
        Assert.True(second);
    }

    [Fact]
    public void NonHttpMonitorException_IsNotRateLimited()
    {
        var method = typeof(ExceptionLogService).GetMethod(
            "IsRepeatedMonitorTransportException",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var result = method!.Invoke(null, [new InvalidOperationException("application invariant failed"), "ChatGptMonitorService.MonitorLoop", DateTime.UtcNow.Ticks]);
        Assert.False((bool)result!);
    }
}
