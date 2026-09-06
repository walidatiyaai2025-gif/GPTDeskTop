using System.Reflection;
using System.Text;

namespace GPTDeskTop.UI;

/// <summary>
/// Adds a visible export action to the Runtime Inspector card that is actually rendered in
/// Monitor Only. The export is intentionally limited to the live inspector values already
/// visible on screen; it does not include message bodies, the selected conversation URL,
/// cookies, tokens, or database contents.
/// </summary>
internal static class MonitorOnlyRuntimeInspectorExport
{
    private const string ExportButtonName = "MonitorOnlyRuntimeInspectorExportButton";
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Install(SimpleMonitorForm form)
    {
        ArgumentNullException.ThrowIfNull(form);

        var inspectorCard = DescendantsAndSelf(form)
            .OfType<GroupBox>()
            .FirstOrDefault(group => group.Text.Contains("Runtime Inspector", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Monitor Only Runtime Inspector card is unavailable.");

        if (DescendantsAndSelf(inspectorCard)
            .OfType<Button>()
            .Any(button => string.Equals(button.Name, ExportButtonName, StringComparison.Ordinal)))
            return;

        var layout = inspectorCard.Controls
            .OfType<TableLayoutPanel>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Monitor Only Runtime Inspector layout is unavailable.");

        var state = GetField<Label>(form, "_inspectorState");
        var message = GetField<Label>(form, "_inspectorMessage");
        var progress = GetField<Label>(form, "_inspectorProgress");
        var retries = GetField<Label>(form, "_inspectorRetries");
        var cdp = GetField<Label>(form, "_inspectorCdp");
        var error = GetField<Label>(form, "_inspectorError");

        var exportButton = new Button
        {
            Name = ExportButtonName,
            Text = "Export",
            AutoSize = true,
            MinimumSize = new Size(86, 32),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Margin = new Padding(8, 2, 0, 2),
            AccessibleName = "Export Runtime Inspector",
            AccessibleDescription = "Save the live Monitor Only Runtime Inspector values to a UTF-8 text file."
        };
        exportButton.Click += (_, _) => Export(form, state, message, progress, retries, cdp, error);

        layout.SuspendLayout();
        try
        {
            layout.ColumnCount = 2;
            layout.ColumnStyles.Clear();
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.Controls.Add(exportButton, 1, 0);
            layout.SetColumnSpan(error, 2);
        }
        finally
        {
            layout.ResumeLayout(true);
        }

        FluentTheme.StyleButton(exportButton, primary: true);
        exportButton.BringToFront();
    }

    private static void Export(
        Form owner,
        Label state,
        Label message,
        Label progress,
        Label retries,
        Label cdp,
        Label error)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Export Monitor Only Runtime Inspector",
            Filter = "Text file (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"GPTDeskTop-Monitor-Only-Runtime-Inspector-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            AddExtension = true,
            DefaultExt = "txt",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(owner) != DialogResult.OK)
            return;

        try
        {
            var lines = new[]
            {
                "GPTDeskTop Monitor Only — Runtime Inspector",
                $"Captured: {DateTimeOffset.Now:O}",
                $"Version: {Application.ProductVersion}",
                state.Text,
                message.Text,
                progress.Text,
                retries.Text,
                cdp.Text,
                error.Text
            };
            File.WriteAllText(dialog.FileName, string.Join(Environment.NewLine, lines) + Environment.NewLine, new UTF8Encoding(false));
            MessageBox.Show(
                owner,
                $"Runtime Inspector exported to:\n{dialog.FileName}",
                "Runtime Inspector",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                owner,
                ex.Message,
                "Runtime Inspector export failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static T GetField<T>(SimpleMonitorForm form, string name)
        where T : class
        => typeof(SimpleMonitorForm).GetField(name, PrivateInstance)?.GetValue(form) as T
           ?? throw new MissingFieldException(typeof(SimpleMonitorForm).FullName, name);

    private static IEnumerable<Control> DescendantsAndSelf(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
        {
            foreach (var descendant in DescendantsAndSelf(child))
                yield return descendant;
        }
    }
}
