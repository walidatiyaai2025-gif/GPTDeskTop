using System.Reflection;
using System.Text.Json;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

/// <summary>
/// Monitor-only send path with a strict mutation boundary.
///
/// All conversation recovery/readiness work must complete before this class is called. Once
/// SendChatMessageAsync starts, this sender performs read-only receipt checks only: it never
/// reloads, rebinds, reopens, refreshes or retries a physical submit. An uncertain outcome is
/// returned/raised to the runner, which fails closed and requires operator reconciliation.
/// </summary>
internal static class SimpleMonitorVerifiedSender
{
    private static readonly MethodInfo EvaluateMethod = typeof(ChromeDevToolsService)
        .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
        .Single(method => method.Name == "EvaluateAsync" && method.GetParameters().Length == 4);

    private static readonly TimeSpan ReceiptTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ReceiptPollInterval = TimeSpan.FromMilliseconds(250);

    private const string UserTurnSnapshotExpression = """
(() => {
  const messages = [...document.querySelectorAll('[data-message-author-role="user"]')];
  const last = messages.length
    ? (messages[messages.length - 1].innerText || messages[messages.length - 1].textContent || '').trim()
    : '';
  return { count: messages.length, lastText: last };
})()
""";

    internal static async Task<bool> SendOnceAndVerifyAsync(
        ChromeDevToolsService chrome,
        ChromeTab tab,
        string message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chrome);
        ArgumentNullException.ThrowIfNull(tab);

        var expected = (message ?? string.Empty).Trim();
        if (expected.Length == 0)
            return false;

        // This is the final read-only baseline before the mutation boundary. No recovery action is
        // allowed after this point; a transport failure becomes an uncertain send and fails closed.
        var before = await ReadUserTurnSnapshotAsync(chrome, tab, cancellationToken).ConfigureAwait(false);

        // Exactly one composer mutation / physical submit attempt. SendChatMessageAsync itself does
        // not reload or rebind the tab. Never replace this with SendChatMessageVerifiedAsync here:
        // that legacy path can refresh/rebind after the editor has already been populated.
        var submitted = await chrome.SendChatMessageAsync(tab, message, cancellationToken).ConfigureAwait(false);
        if (!submitted)
            return false;

        var deadline = DateTimeOffset.UtcNow + ReceiptTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await ReadUserTurnSnapshotAsync(chrome, tab, cancellationToken).ConfigureAwait(false);

            if (current.Count > before.Count)
            {
                // A different new user turn means somebody/something else changed the chat. Do not
                // retry or reinterpret it as our receipt.
                return string.Equals(current.LastText, expected, StringComparison.Ordinal);
            }

            await Task.Delay(ReceiptPollInterval, cancellationToken).ConfigureAwait(false);
        }

        // The click outcome is now uncertain. Returning false causes the monitor runner to block;
        // it must never reload/rebind or blindly submit the stored message again.
        return false;
    }

    private static async Task<UserTurnSnapshot> ReadUserTurnSnapshotAsync(
        ChromeDevToolsService chrome,
        ChromeTab tab,
        CancellationToken cancellationToken)
    {
        try
        {
            var task = (Task<JsonElement>)(EvaluateMethod.Invoke(
                chrome,
                new object[] { tab, UserTurnSnapshotExpression, cancellationToken, false })
                ?? throw new InvalidOperationException("User-turn receipt probe returned no task."));
            var value = await task.ConfigureAwait(false);
            var count = value.TryGetProperty("count", out var countElement) ? countElement.GetInt32() : 0;
            var lastText = value.TryGetProperty("lastText", out var textElement) ? textElement.GetString() ?? string.Empty : string.Empty;
            return new UserTurnSnapshot(count, lastText.Trim());
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private readonly record struct UserTurnSnapshot(int Count, string LastText);
}
