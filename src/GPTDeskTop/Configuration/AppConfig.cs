using System.Text.Json;

namespace GPTDeskTop.Configuration;

public sealed class AppConfig
{
    public ChromeConfig Chrome { get; set; } = new();
    public MonitoringConfig Monitoring { get; set; } = new();
    public DatabaseConfig Database { get; set; } = new();
    public TaskAutomationConfig TaskAutomation { get; set; } = new();

    public static AppConfig Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
            return new AppConfig();

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new AppConfig();
    }
}

public sealed class ChromeConfig
{
    public string DebuggingBaseUrl { get; set; } = "http://127.0.0.1:9222";
    public int DebuggingPort { get; set; } = 9222;
    public string StartUrl { get; set; } = "https://chatgpt.com/";
}

public sealed class MonitoringConfig
{
    public int PollIntervalMilliseconds { get; set; } = 1000;
    public int StableResponseMilliseconds { get; set; } = 1500;
    public int DelayAfterSendMilliseconds { get; set; } = 1200;
}

public sealed class DatabaseConfig
{
    public string FileName { get; set; } = "appdata.db";
}

public sealed class TaskAutomationConfig
{
    public bool Enabled { get; set; } = true;
    public int WorkWindowMinutes { get; set; } = 10;
    public int CoolingWindowMinutes { get; set; } = 5;
    public bool ResumeOnStartup { get; set; } = true;
    public bool PersistCheckpoints { get; set; } = true;
    public string MessageCatalogFile { get; set; } = "task-messages.json";
}
