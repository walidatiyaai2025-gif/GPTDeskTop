using System.Reflection;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

internal static class MonitorOnlyHardCutoverUi
{
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Apply(SimpleMonitorForm form)
    {
        ArgumentNullException.ThrowIfNull(form);

        var current = GetRadio(form, "_currentModeRadio");
        var monitor = GetRadio(form, "_monitorModeRadio");

        current.Checked = false;
        current.Enabled = false;
        current.Visible = false;
        monitor.Checked = true;
        monitor.Enabled = false;
        monitor.Visible = false;

        if (current.Parent is FlowLayoutPanel modes)
        {
            modes.SuspendLayout();
            try
            {
                modes.Controls.Clear();
                modes.FlowDirection = FlowDirection.RightToLeft;
                modes.Controls.Add(new Label
                {
                    AutoSize = true,
                    Text = "Monitor Only",
                    Font = new Font("Segoe UI Variable Text", 10F, FontStyle.Bold),
                    ForeColor = FluentTheme.Info,
                    Margin = new Padding(0, 4, 4, 0)
                });
            }
            finally
            {
                modes.ResumeLayout(true);
            }
        }

        form.Text = $"GPTDeskTop {ApplicationBuildIdentity.DisplayVersion} — Monitor Only";
    }

    private static RadioButton GetRadio(SimpleMonitorForm form, string fieldName)
        => (RadioButton?)(typeof(SimpleMonitorForm).GetField(fieldName, PrivateInstance)?.GetValue(form))
           ?? throw new MissingFieldException(typeof(SimpleMonitorForm).FullName, fieldName);
}
