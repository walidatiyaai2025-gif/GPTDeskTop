using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

/// <summary>
/// Browser operations required by ChatGptMonitorService. Production uses
/// ChromeDevToolsService; deterministic QA runtimes can verify monitor timing
/// and per-tab behavior without requiring an external ChatGPT account.
/// </summary>
public interface IChatGptMonitorBrowser
{
    Task<ChatPageState> GetChatStateAsync(ChromeTab tab, CancellationToken cancellationToken = default);
    Task ReloadTabAsync(ChromeTab tab, CancellationToken cancellationToken = default);
    Task<ChromeTab> CreateNewChatTabAsync(CancellationToken cancellationToken = default);
    Task<bool> TrySelectModelAsync(ChromeTab tab, string modelLabel, CancellationToken cancellationToken = default);
    Task<bool> SendChatMessageVerifiedAsync(ChromeTab tab, string message, CancellationToken cancellationToken = default);
    Task<bool> CloseTabAsync(ChromeTab tab, CancellationToken cancellationToken = default);
}
