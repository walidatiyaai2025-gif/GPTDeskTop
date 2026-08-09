namespace GPTDeskTop.RuntimeTests;

public sealed class DialogAccessibilityUiRegressionTests
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
    public void ApplicationSettingsAreDpiSafeResizableAndKeyboardAccessible()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "SettingsForm.cs");

        Assert.Contains("AutoScaleMode = AutoScaleMode.Dpi", source, StringComparison.Ordinal);
        Assert.Contains("FormBorderStyle = FormBorderStyle.Sizable", source, StringComparison.Ordinal);
        Assert.Contains("MinimumSize = new Size(720, 520)", source, StringComparison.Ordinal);
        Assert.Contains("AccessibleName = \"GPTDeskTop application settings\"", source, StringComparison.Ordinal);
        Assert.Contains("ConfigureAccessible(_defaultReply", source, StringComparison.Ordinal);
        Assert.Contains("_defaultReply.Focus();", source, StringComparison.Ordinal);
        Assert.Contains("AcceptButton = _saveButton", source, StringComparison.Ordinal);
        Assert.Contains("CancelButton = _cancelButton", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationSettingsExposeAsyncOperationStateAndPreventDuplicateSave()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "SettingsForm.cs");

        Assert.Contains("private bool _busy;", source, StringComparison.Ordinal);
        Assert.Contains("if (_busy) return;", source, StringComparison.Ordinal);
        Assert.Contains("SetBusy(true, \"Loading settings…\")", source, StringComparison.Ordinal);
        Assert.Contains("SetBusy(true, \"Saving settings…\")", source, StringComparison.Ordinal);
        Assert.Contains("_saveButton.Enabled = !busy", source, StringComparison.Ordinal);
        Assert.Contains("Settings Save Error", source, StringComparison.Ordinal);
        Assert.Contains("Settings Load Error", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NotificationSoundSelectorTracksParentToggle()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "SettingsForm.cs");

        Assert.Contains("_soundEnabled.CheckedChanged += (_, _) => UpdateDependentControls();", source, StringComparison.Ordinal);
        Assert.Contains("_soundType.Enabled = _soundEnabled.Checked && !_busy;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorSettingsExposeRuntimeStatusAndSafeValidationFlow()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MonitorSettingsForm.cs");

        Assert.Contains("ApplyMonitorStatus(monitor);", source, StringComparison.Ordinal);
        Assert.Contains("_runtimeStatus.Text = running ? \"RUNNING\" : \"STOPPED\";", source, StringComparison.Ordinal);
        Assert.Contains("_runtimeStatus.Text = \"DISABLED\";", source, StringComparison.Ordinal);
        Assert.Contains("private void TrySaveAndClose()", source, StringComparison.Ordinal);
        Assert.Contains("DialogResult = DialogResult.OK;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_saveButton = new() { Text = \"Save Monitor\", AutoSize = true, DialogResult = DialogResult.OK", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorSettingsDisableDependentRotationAndRoutingFields()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MonitorSettingsForm.cs");

        Assert.Contains("_rotationEnabledCheck.CheckedChanged += (_, _) => UpdateDependentControls();", source, StringComparison.Ordinal);
        Assert.Contains("_modelRoutingEnabledCheck.CheckedChanged += (_, _) => UpdateDependentControls();", source, StringComparison.Ordinal);
        Assert.Contains("_newChatMessageBox.Enabled = rotationEnabled;", source, StringComparison.Ordinal);
        Assert.Contains("_maxRotations.Enabled = rotationEnabled;", source, StringComparison.Ordinal);
        Assert.Contains("_preferredModelBox.Enabled = routingEnabled;", source, StringComparison.Ordinal);
        Assert.Contains("_fallbackModelBox.Enabled = routingEnabled;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorSettingsAreDpiSafeResizableAndAccessible()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MonitorSettingsForm.cs");

        Assert.Contains("AutoScaleMode = AutoScaleMode.Dpi", source, StringComparison.Ordinal);
        Assert.Contains("FormBorderStyle = FormBorderStyle.Sizable", source, StringComparison.Ordinal);
        Assert.Contains("MinimumSize = new Size(740, 540)", source, StringComparison.Ordinal);
        Assert.Contains("AccessibleName = \"Monitor settings\"", source, StringComparison.Ordinal);
        Assert.Contains("ConfigureAccessible(_autoReplyBox", source, StringComparison.Ordinal);
        Assert.Contains("_autoReplyBox.Focus();", source, StringComparison.Ordinal);
    }
}
