using System.Net;
using System.Net.Http;
using System.Text;
using GPTDeskTop.Configuration;
using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;
using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentPersistenceRegressionTests
{
    [Fact]
    public async Task PersistedScheduleIsLoadedByANewEngineInstanceAfterRestart()
    {
        var root = CreateRoot();
        try
        {
            var schedulePath = Path.Combine(root, "schedule.json");
            var statePath = Path.Combine(root, "state.json");
            var messagesPath = Path.Combine(root, "messages.json");
            await File.WriteAllTextAsync(messagesPath, "{\"Messages\":[\"one\"]}");

            var store = new DevelopmentTaskScheduleSettingsStore(schedulePath);
            store.Save(new DevelopmentTaskScheduleSettings { WorkMinutes = 7, CoolingMinutes = 3 });

            await using (var firstProcess = new DevelopmentTaskEngine(
                statePath: statePath,
                messagesPath: messagesPath,
                scheduleSettingsPath: schedulePath))
            {
                Assert.Equal(TimeSpan.FromMinutes(7), firstProcess.WorkWindow);
                Assert.Equal(TimeSpan.FromMinutes(3), firstProcess.CoolingWindow);
            }

            store.Save(new DevelopmentTaskScheduleSettings { WorkMinutes = 11, CoolingMinutes = 4 });

            await using var restartedProcess = new DevelopmentTaskEngine(
                statePath: statePath,
                messagesPath: messagesPath,
                scheduleSettingsPath: schedulePath);

            Assert.Equal(TimeSpan.FromMinutes(11), restartedProcess.WorkWindow);
            Assert.Equal(TimeSpan.FromMinutes(4), restartedProcess.CoolingWindow);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task RecreatedTargetIdIsPersistedAndBecomesExactIdentityOnNextResolution()
    {
        var root = CreateRoot();
        try
        {
            var databasePath = Path.Combine(root, "appdata.db");
            var database = new LocalDatabase(databasePath);
            await database.InitializeAsync();

            var monitor = new SavedMonitor
            {
                Title = "Persisted target QA",
                TabId = "old-target",
                Url = "https://chatgpt.com/c/persisted-target",
                AutoReply = "كمل",
                Enabled = true
            };
            monitor.Id = await database.SaveMonitorAsync(monitor);
            await database.SetSettingAsync($"TaskAutomation.Monitor.{monitor.Id}.Enabled", "1");

            const string replacementId = "new-target-after-restart";
            var json = $$"""
[
  {
    "id": "{{replacementId}}",
    "title": "Persisted target QA",
    "url": "https://chatgpt.com/c/persisted-target",
    "type": "page",
    "webSocketDebuggerUrl": "ws://127.0.0.1/fake"
  }
]
""";
            using var httpClient = new HttpClient(new StaticJsonHandler(json));
            var chrome = new ChromeDevToolsService(
                httpClient,
                new ChromeConfig
                {
                    DebuggingPort = 9222,
                    DebuggingBaseUrl = "http://127.0.0.1:9222",
                    StartUrl = "https://chatgpt.com/"
                });
            var factory = new DevelopmentTaskMonitorTargetFactory(
                database,
                new SavedMonitorTabResolver(chrome),
                chrome);

            var recipients = await factory.ResolveEnabledRecipientsAsync();

            Assert.Single(recipients);
            Assert.Equal(replacementId, recipients[0].TabId);

            // Simulate the next process opening the same SQLite database.
            var restartedDatabase = new LocalDatabase(databasePath);
            await restartedDatabase.InitializeAsync();
            var persisted = (await restartedDatabase.GetSavedMonitorsAsync()).Single(x => x.Id == monitor.Id);
            Assert.Equal(replacementId, persisted.TabId);

            var exactTab = new ChromeTab
            {
                Id = replacementId,
                Title = persisted.Title,
                Url = persisted.Url,
                Type = "page",
                WebSocketDebuggerUrl = "ws://127.0.0.1/fake"
            };
            var resolution = SavedMonitorTabResolver.Resolve(persisted, [exactTab]);

            Assert.True(resolution.Found);
            Assert.Equal("PersistedTabId", resolution.MatchType);
            Assert.Equal(replacementId, resolution.Tab!.Id);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string root)
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch { }
    }

    private sealed class StaticJsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }
}
