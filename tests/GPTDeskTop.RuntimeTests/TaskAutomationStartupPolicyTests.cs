using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class TaskAutomationStartupPolicyTests
{
    [Theory]
    [InlineData("Working")]
    [InlineData("working")]
    [InlineData("Cooling")]
    [InlineData("Paused")]
    public void ResumesPersistedActiveStates(string phase)
    {
        Assert.True(TaskAutomationStartupPolicy.ShouldResume(phase));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Stopped")]
    [InlineData("Completed")]
    [InlineData("Faulted")]
    public void DoesNotAutoResumeTerminalOrUnknownStates(string? phase)
    {
        Assert.False(TaskAutomationStartupPolicy.ShouldResume(phase));
    }
}
