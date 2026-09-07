using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class SimpleMonitorJsonDelayCompatibilityTests
{
    [Fact]
    public void ParseAndValidate_LegacyDefaultDelay_IsNormalizedToCurrentMinimum()
    {
        var plan = SimpleMonitorMessagePlanService.ParseAndValidate(
            """
            {
              "schemaVersion": 1,
              "name": "Legacy plan",
              "loop": false,
              "defaultDelaySeconds": 15,
              "messages": [
                { "text": "continue", "enabled": true, "delaySeconds": 20 }
              ]
            }
            """);

        Assert.Equal(30, plan.DefaultDelaySeconds);
        Assert.Equal(30, plan.Messages[0].DelaySeconds);
        Assert.Equal(30, plan.Messages[0].EffectiveDelaySeconds(plan.DefaultDelaySeconds));
    }

    [Theory]
    [InlineData(30, 30)]
    [InlineData(45, 45)]
    [InlineData(3600, 3600)]
    public void ParseAndValidate_CurrentDelaysRemainUnchanged(int input, int expected)
    {
        var json = $$"""
            {
              "schemaVersion": 1,
              "name": "Current plan",
              "loop": false,
              "defaultDelaySeconds": {{input}},
              "messages": [
                { "text": "continue", "enabled": true, "delaySeconds": {{input}} }
              ]
            }
            """;

        var plan = SimpleMonitorMessagePlanService.ParseAndValidate(json);

        Assert.Equal(expected, plan.DefaultDelaySeconds);
        Assert.Equal(expected, plan.Messages[0].DelaySeconds);
    }

    [Fact]
    public void ParseAndValidate_StillRejectsValuesBelowLegacyCompatibilityFloor()
    {
        var json =
            """
            {
              "schemaVersion": 1,
              "name": "Invalid plan",
              "loop": false,
              "defaultDelaySeconds": 14,
              "messages": [
                { "text": "continue", "enabled": true }
              ]
            }
            """;

        Assert.Throws<InvalidDataException>(() => SimpleMonitorMessagePlanService.ParseAndValidate(json));
    }

    [Fact]
    public void SampleJson_UsesOnlyCurrentSafeDelayRange()
    {
        var plan = SimpleMonitorMessagePlanService.ParseAndValidate(SimpleMonitorMessagePlanService.CreateSampleJson());

        Assert.InRange(plan.DefaultDelaySeconds, 30, 3600);
        Assert.All(
            plan.Messages.Where(step => step.DelaySeconds.HasValue),
            step => Assert.InRange(step.DelaySeconds!.Value, 30, 3600));
    }

    [Fact]
    public void ChatGptPrompt_AdvertisesCurrentSafeDelayRange()
    {
        var prompt = SimpleMonitorMessagePlanService.CreateChatGptPrompt();

        Assert.Contains("between 30 and 3600 seconds", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("between 15 and 3600 seconds", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(15, 30)]
    [InlineData(29, 30)]
    [InlineData(30, 30)]
    [InlineData(75, 75)]
    [InlineData(5000, 3600)]
    public void NormalizeRuntimeDelaySeconds_ClampsToCurrentRuntimeRange(int input, int expected)
    {
        Assert.Equal(expected, SimpleMonitorMessagePlanService.NormalizeRuntimeDelaySeconds(input));
    }
}
