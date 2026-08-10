namespace GPTDeskTop.RuntimeTests;

public sealed class NotificationSettingsReloadRegressionTests
{
    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }

    [Fact]
    public void ProgramWiresTrayReloadIntoMainForm()
    {
        var source = ReadSource("src", "GPTDeskTop", "Program.cs");

        Assert.Contains(
            "new MainForm(chrome, monitor, database, notifications.ReloadSettingsAsync)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MainFormReloadsNotificationsOnlyAfterSuccessfulSettingsDialog()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");
        Assert.Contains("Func<Task>? reloadNotificationSettings = null", source, StringComparison.Ordinal);
        Assert.Contains("_reloadNotificationSettings = reloadNotificationSettings", source, StringComparison.Ordinal);

        var start = source.IndexOf("private async Task OpenSettingsAsync()", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task LaunchChromeAsync()", start, StringComparison.Ordinal);
        Assert.True(start >= 0);
        Assert.True(end > start);

        var method = source[start..end];
        var successGuard = method.IndexOf("if (form.ShowDialog(this) != DialogResult.OK) return;", StringComparison.Ordinal);
        var callback = method.IndexOf("await _reloadNotificationSettings();", StringComparison.Ordinal);
        var refresh = method.IndexOf("await RefreshMonitorsAsync();", StringComparison.Ordinal);

        Assert.True(successGuard >= 0);
        Assert.True(callback > successGuard);
        Assert.True(refresh > callback);
        Assert.Contains("MainForm.ReloadNotificationSettings", method, StringComparison.Ordinal);
    }

    [Fact]
    public void TrayReloadBoundaryRefreshesAllCachedNotificationValues()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "TrayNotificationService.cs");

        Assert.Contains("public async Task ReloadSettingsAsync()", source, StringComparison.Ordinal);
        Assert.Contains("NotificationDurationSeconds", source, StringComparison.Ordinal);
        Assert.Contains("NotificationSoundEnabled", source, StringComparison.Ordinal);
        Assert.Contains("NotificationSoundType", source, StringComparison.Ordinal);
        Assert.Contains("UpdateMenuChecks();", source, StringComparison.Ordinal);
        Assert.Contains("await ReloadSettingsAsync();", source, StringComparison.Ordinal);
    }
}
