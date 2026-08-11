using System.Net.Sockets;
using System.Reflection;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class ChromeProbeLoggingRegressionTests
{
    [Theory]
    [InlineData("RuntimeHealthControl.ChromeProbe")]
    [InlineData("SupportBundle.ChromeProbe")]
    public void ConnectionRefusedFromReadOnlyChromeProbeIsExpectedAvailabilityState(string source)
    {
        var exception = new HttpRequestException(
            "Connection refused",
            new SocketException((int)SocketError.ConnectionRefused));

        Assert.True(IsExpectedOfflineProbe(exception, source));
    }

    [Fact]
    public void ConnectionRefusedOutsideReadOnlyChromeProbesIsStillDiagnostic()
    {
        var exception = new HttpRequestException(
            "Connection refused",
            new SocketException((int)SocketError.ConnectionRefused));

        Assert.False(IsExpectedOfflineProbe(exception, "ChatGptMonitorService.Runtime"));
    }

    [Fact]
    public void OtherChromeProbeSocketFailuresAreStillDiagnostic()
    {
        var exception = new HttpRequestException(
            "Connection reset",
            new SocketException((int)SocketError.ConnectionReset));

        Assert.False(IsExpectedOfflineProbe(exception, "RuntimeHealthControl.ChromeProbe"));
    }

    [Fact]
    public void ChromeProbeHttpFailuresWithoutConnectionRefusalAreStillDiagnostic()
    {
        var exception = new HttpRequestException("Unexpected Chrome DevTools response.");

        Assert.False(IsExpectedOfflineProbe(exception, "SupportBundle.ChromeProbe"));
    }

    private static bool IsExpectedOfflineProbe(Exception exception, string source)
    {
        var method = typeof(ExceptionLogService).GetMethod(
            "IsExpectedChromeDevToolsOfflineProbe",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return (bool)method!.Invoke(null, [exception, source])!;
    }
}
