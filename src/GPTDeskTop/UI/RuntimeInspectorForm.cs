using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

internal sealed class RuntimeInspectorForm : Form
{
    private readonly Form _runtimeOwner;
    private readonly ChatGptMonitorService _monitor;
    private readonly TextBox _text = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill };

    public RuntimeInspectorForm(Form runtimeOwner, ChatGptMonitorService monitor)
    {
        _runtimeOwner = runtimeOwner;
        _monitor = monitor;
        Text = "GPTDeskTop · Runtime Inspector";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1100, 760);
        MinimumSize = new Size(800, 560);
        AutoScaleMode = AutoScaleMode.Dpi;

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(8) };
        var refresh = new Button { Text = "Refresh Snapshot", AutoSize = true };
        var copy = new Button { Text = "Copy Diagnostics", AutoSize = true };
        var export = new Button { Text = "Export Support Bundle", AutoSize = true };
        refresh.Click += (_, _) => RefreshSnapshot();
        copy.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(_text.Text)) Clipboard.SetText(_text.Text); };
        export.Click += (_, _) => ExportBundle();
        actions.Controls.AddRange([refresh, copy, export]);
        Controls.Add(_text);
        Controls.Add(actions);
        Shown += (_, _) => RefreshSnapshot();
    }

    private void RefreshSnapshot()
    {
        var snapshot = RuntimeInspectorService.Capture(_runtimeOwner, _monitor);
        _text.Text = RuntimeInspectorService.Summary(snapshot) + Environment.NewLine + RuntimeInspectorService.ToSanitizedJson(snapshot);
    }

    private void ExportBundle()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "ZIP archive (*.zip)|*.zip",
            FileName = $"GPTDeskTop-Support-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            RuntimeInspectorService.ExportBundle(_runtimeOwner, _monitor, dialog.FileName);
            MessageBox.Show(this, "Support bundle exported. It contains sanitized runtime/UI/process diagnostics and bounded logs; GitHub tokens/cookies are not intentionally exported.", "Runtime Inspector", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Support bundle export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
