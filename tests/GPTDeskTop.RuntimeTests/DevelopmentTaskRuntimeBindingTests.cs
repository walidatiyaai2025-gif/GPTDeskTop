using GPTDeskTop.Services.DevelopmentTaskEngine;
using GPTDeskTop.UI;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentTaskRuntimeBindingTests
{
    [Fact]
    public void BindingExposesEngineAndFullLifecycleControls()
    {
        Assert.True(typeof(DevelopmentTaskRuntimeBinding).GetProperty(nameof(DevelopmentTaskRuntimeBinding.Engine)) is not null);
        Assert.True(typeof(DevelopmentTaskRuntimeBinding).GetProperty(nameof(DevelopmentTaskRuntimeBinding.State)) is not null);
        Assert.True(typeof(DevelopmentTaskRuntimeBinding).GetMethod(nameof(DevelopmentTaskRuntimeBinding.StartAsync)) is not null);
        Assert.True(typeof(DevelopmentTaskRuntimeBinding).GetMethod(nameof(DevelopmentTaskRuntimeBinding.PauseAsync)) is not null);
        Assert.True(typeof(DevelopmentTaskRuntimeBinding).GetMethod(nameof(DevelopmentTaskRuntimeBinding.ResumeAsync)) is not null);
        Assert.True(typeof(DevelopmentTaskRuntimeBinding).GetMethod(nameof(DevelopmentTaskRuntimeBinding.StopAsync)) is not null);
    }

    [Fact]
    public void DashboardUsesRuntimeBindingInsteadOfRawEngine()
    {
        var constructor = typeof(DevelopmentTaskDashboardControl)
            .GetConstructor(new[] { typeof(DevelopmentTaskRuntimeBinding) });
        Assert.NotNull(constructor);
    }
}
