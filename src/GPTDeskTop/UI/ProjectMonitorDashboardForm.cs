namespace GPTDeskTop.UI;

public sealed class ProjectMonitorDashboardForm : Form
{
    private readonly ProjectMonitorDashboardControl _dashboard = new();

    public ProjectMonitorDashboardForm()
    {
        Text = "GPTDeskTop · Project Monitor";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1050, 700);
        Size = new Size(1380, 860);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = FluentTheme.Background;
        Controls.Add(_dashboard);
        Shown += async (_, _) => await _dashboard.RefreshAsync();
        FluentTheme.Apply(this);
    }
}
