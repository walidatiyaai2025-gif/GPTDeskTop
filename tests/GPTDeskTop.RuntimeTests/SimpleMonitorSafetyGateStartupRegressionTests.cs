using System.Runtime.CompilerServices;
using GPTDeskTop.Services;
using Xunit;

namespace GPTDeskTop.RuntimeTests;

public sealed class SimpleMonitorSafetyGateStartupRegressionTests
{
    [Fact]
    public void StaticInitializerCanRunWithoutThrowing()
    {
        var safetyGateType = typeof(ChromeDevToolsService).Assembly.GetType(
            "GPTDeskTop.Services.SimpleMonitorSafetyGate",
            throwOnError: true)!;

        var exception = Record.Exception(
            () => RuntimeHelpers.RunClassConstructor(safetyGateType.TypeHandle));

        Assert.Null(exception);
    }
}
