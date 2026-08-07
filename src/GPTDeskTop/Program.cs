using GPTDeskTop.Configuration;
using GPTDeskTop.Data;
using GPTDeskTop.Services;
using GPTDeskTop.UI;

namespace GPTDeskTop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var config = AppConfig.Load();
        var database = new LocalDatabase(config.Database.FileName);
        database.InitializeAsync().GetAwaiter().GetResult();

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        var chrome = new ChromeDevToolsService(httpClient, config.Chrome);
        var monitor = new ChatGptMonitorService(chrome, database, config.Monitoring);
        using var notifications = new TrayNotificationService(monitor, database);
        notifications.InitializeAsync().GetAwaiter().GetResult();

        Application.Run(new MainForm(chrome, monitor, database));
    }
}
