using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class SimpleMonitorMessagePlanTests
{
    [Fact]
    public void ParseAndValidate_AcceptsOrderedLoopPlanWithPerMessageDelays()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "name": "Night work",
          "loop": true,
          "defaultDelaySeconds": 15,
          "messages": [
            { "label": "one", "text": "first", "enabled": true, "delaySeconds": 20 },
            { "label": "skip", "text": "disabled", "enabled": false, "delaySeconds": 15 },
            { "label": "two", "text": "second", "enabled": true }
          ]
        }
        """;

        var plan = SimpleMonitorMessagePlanService.ParseAndValidate(json);

        Assert.Equal("Night work", plan.Name);
        Assert.True(plan.Loop);
        Assert.Equal(15, plan.DefaultDelaySeconds);
        Assert.Equal(3, plan.Messages.Count);
        Assert.Equal(20, plan.Messages[0].EffectiveDelaySeconds(plan.DefaultDelaySeconds));
        Assert.False(plan.Messages[1].Enabled);
        Assert.Equal(15, plan.Messages[2].EffectiveDelaySeconds(plan.DefaultDelaySeconds));
    }

    [Fact]
    public void ParseAndValidate_RejectsDelayBelowSafetyMinimum()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "name": "unsafe",
          "loop": true,
          "defaultDelaySeconds": 10,
          "messages": [ { "text": "continue", "enabled": true } ]
        }
        """;

        var ex = Assert.Throws<InvalidDataException>(() => SimpleMonitorMessagePlanService.ParseAndValidate(json));

        Assert.Contains("defaultDelaySeconds", ex.Message);
    }

    [Fact]
    public void ParseAndValidate_RejectsPlanWithoutEnabledMessages()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "name": "none",
          "loop": false,
          "defaultDelaySeconds": 15,
          "messages": [ { "text": "disabled", "enabled": false, "delaySeconds": 15 } ]
        }
        """;

        var ex = Assert.Throws<InvalidDataException>(() => SimpleMonitorMessagePlanService.ParseAndValidate(json));

        Assert.Contains("enabled message", ex.Message);
    }

    [Fact]
    public void SampleJson_RoundTripsThroughValidator()
    {
        var sample = SimpleMonitorMessagePlanService.CreateSampleJson();

        var plan = SimpleMonitorMessagePlanService.ParseAndValidate(sample);

        Assert.Equal(1, plan.SchemaVersion);
        Assert.True(plan.Messages.Count >= 2);
        Assert.Contains(plan.Messages, step => step.Enabled);
    }

    [Fact]
    public void ChatGptPrompt_RequiresJsonOnlyAndSafetyDelay()
    {
        var prompt = SimpleMonitorMessagePlanService.CreateChatGptPrompt();

        Assert.Contains("ONLY valid JSON", prompt);
        Assert.Contains("between 15 and 3600", prompt);
        Assert.Contains("loop=false", prompt);
    }
}
