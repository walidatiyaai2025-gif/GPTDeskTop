using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentTaskAssistantResponseTests
{
    [Fact]
    public async Task VerifiedDeliveryDoesNotAdvanceUntilAssistantResponseCompletes()
    {
        var root = CreateRoot();
        try
        {
            var statePath = Path.Combine(root, "state.json");
            var messagesPath = Path.Combine(root, "messages.json");
            await File.WriteAllTextAsync(messagesPath, "{\"Messages\":[\"one\",\"two\"]}");

            await using var engine = new DevelopmentTaskEngine(
                workWindow: TimeSpan.FromMinutes(10),
                coolingWindow: TimeSpan.FromMinutes(5),
                statePath: statePath,
                messagesPath: messagesPath);

            await engine.StartAsync("plan", "Plan");
            await engine.MarkAwaitingAssistantResponseAsync(new[] { "17" });

            Assert.True(engine.State.AwaitingAssistantResponse);
            Assert.Equal(0, engine.State.CurrentMessageIndex);
            Assert.Equal(0, engine.State.CompletedMessages);

            var advanced = await engine.HandleAssistantResponseAsync("17", "completed answer", isError: false);

            Assert.True(advanced);
            Assert.False(engine.State.AwaitingAssistantResponse);
            Assert.Equal(1, engine.State.CurrentMessageIndex);
            Assert.Equal(1, engine.State.CompletedMessages);
            Assert.Equal(DevelopmentTaskEngineStatus.Cooling, engine.State.Status);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task AwaitingResponseSurvivesRestartWithoutAdvancingOrResending()
    {
        var root = CreateRoot();
        try
        {
            var statePath = Path.Combine(root, "state.json");
            var messagesPath = Path.Combine(root, "messages.json");
            await File.WriteAllTextAsync(messagesPath, "{\"Messages\":[\"one\",\"two\"]}");

            await using (var first = new DevelopmentTaskEngine(
                workWindow: TimeSpan.FromMinutes(10),
                coolingWindow: TimeSpan.FromMinutes(5),
                statePath: statePath,
                messagesPath: messagesPath))
            {
                await first.StartAsync("plan", "Plan");
                await first.MarkAwaitingAssistantResponseAsync(new[] { "22" });
            }

            await using var resumed = new DevelopmentTaskEngine(
                workWindow: TimeSpan.FromMinutes(10),
                coolingWindow: TimeSpan.FromMinutes(5),
                statePath: statePath,
                messagesPath: messagesPath);

            var active = await resumed.ResumeIfActiveAsync();

            Assert.True(active);
            Assert.True(resumed.State.AwaitingAssistantResponse);
            Assert.Equal(0, resumed.State.CurrentMessageIndex);
            Assert.Equal(new[] { "22" }, resumed.State.AwaitingResponseMonitorIds);

            var advanced = await resumed.HandleAssistantResponseAsync("22", "answer after restart", isError: false);
            Assert.True(advanced);
            Assert.Equal(1, resumed.State.CurrentMessageIndex);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ErrorResponseKeepsExactPlanPosition()
    {
        var root = CreateRoot();
        try
        {
            var statePath = Path.Combine(root, "state.json");
            var messagesPath = Path.Combine(root, "messages.json");
            await File.WriteAllTextAsync(messagesPath, "{\"Messages\":[\"one\"]}");

            await using var engine = new DevelopmentTaskEngine(
                statePath: statePath,
                messagesPath: messagesPath);
            await engine.StartAsync("plan", "Plan");
            await engine.MarkAwaitingAssistantResponseAsync(new[] { "9" });

            var advanced = await engine.HandleAssistantResponseAsync("9", "temporary ChatGPT error", isError: true);

            Assert.False(advanced);
            Assert.True(engine.State.AwaitingAssistantResponse);
            Assert.Equal(0, engine.State.CurrentMessageIndex);
            Assert.NotNull(engine.State.LastError);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch { }
    }
}
