using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentMessagePlanCodecTests
{
    [Fact]
    public void JsonRoundTripPreservesMultilinePrompts()
    {
        var source = new[] { "first line\nsecond line", "second prompt" };

        var json = DevelopmentMessagePlanCodec.Serialize(source);
        var parsed = DevelopmentMessagePlanCodec.Parse(json);

        Assert.Equal(source, parsed);
    }

    [Fact]
    public void PlainTextUsesDashSeparatorWithoutDestroyingPromptNewlines()
    {
        var parsed = DevelopmentMessagePlanCodec.Parse("first\nline\n---\nsecond\nline");

        Assert.Equal(2, parsed.Count);
        Assert.Equal($"first{Environment.NewLine}line", parsed[0]);
        Assert.Equal($"second{Environment.NewLine}line", parsed[1]);
    }

    [Fact]
    public void PlainTextWithoutSeparatorIsOnePrompt()
    {
        var parsed = DevelopmentMessagePlanCodec.Parse("one\ntwo\nthree");

        Assert.Single(parsed);
        Assert.Equal($"one{Environment.NewLine}two{Environment.NewLine}three", parsed[0]);
    }
}
