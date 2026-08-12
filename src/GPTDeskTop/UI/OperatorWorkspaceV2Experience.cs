using System.Runtime.CompilerServices;

namespace GPTDeskTop.UI;

/// <summary>
/// GPTDeskTop 2.0 operator workspace: the main surface is reserved for open ChatGPT tabs and
/// monitor state. Live activity, history, runtime health and development details remain available
/// on demand.
/// </summary>
internal static class OperatorWorkspaceV2Experience
{
    private static readonly ConditionalWeakTable<MainForm, Installation> Installations = new();

    [ModuleInitializer]
    internal static void Initialize()
        => Application.Idle += InstallOnOpenMainForms;

    private static void InstallOnOpenMainForms(object? sender, EventArgs e)
    {
        foreach (Form form in Application.OpenForms)
        {
            if (form is MainForm main && !main.IsDisposed && !main.Disposing)
                TryInstall(main);
        }
    }

    internal static bool TryInstall(MainForm form)
    {
        if (Installations.TryGetValue(form, out _))
            return true;

        // CompactTopCommandMenuExperience is the command owner. Do not remove development or
        // runtime-health controls from MainForm until their buttons have been harvested by the
        // menu proxy.
        if (form.MainMenuStrip is null)
            return false;

        var development = Descendants(form).OfType<DevelopmentTaskDashboardControl>().FirstOrDefault();
        var runtimeHealth = Descendants(form).OfType<RuntimeHealthControl>().FirstOrDefault();
        var supportDiagnostics = Descendants(form).OfType<SupportDiagnosticsControl>().FirstOrDefault();
        var history = Descendants(form).OfType<HistoryWorkspaceControl>().FirstOrDefault();
        var root = form.Controls
            .OfType<TableLayoutPanel>()
            .FirstOrDefault(candidate => candidate.Dock == DockStyle.Fill && candidate.RowCount == 5 && candidate.ColumnCount == 1);
        if (development is null || runtimeHealth is null || supportDiagnostics is null || history is null || root is null || root.RowStyles.Count < 5)
            return false;

        var diagnostics = root.Controls
            .Cast<Control>()
            .FirstOrDefault(control => root.GetRow(control) == 3);
        var versionLabel = root.Controls
            .OfType<Label>()
            .FirstOrDefault(label => root.GetRow(label) == 4 && label.Text.StartsWith("GPTDeskTop v", StringComparison.Ordinal));
        if (diagnostics is null || versionLabel is null)
            return false;

        root.SuspendLayout();
        form.SuspendLayout();
        try
        {
            root.Controls.Remove(diagnostics);
            root.RowStyles[2].SizeType = SizeType.Percent;
            root.RowStyles[2].Height = 100F;
            root.RowStyles[3].SizeType = SizeType.Absolute;
            root.RowStyles[3].Height = 0F;

            PrepareHistoryForOnDemand(history);
            PrepareRuntimeHealthForOnDemand(runtimeHealth, supportDiagnostics);
            var liveWindow = BuildLiveMonitorWindow(
                form,
                diagnostics,
                history,
                runtimeHealth,
                supportDiagnostics);
            var footerStatus = BuildFooter(root, versionLabel, development);

            // Keep the control alive because its existing buttons remain the single command source
            // used by the compact Commands menu, but remove its permanent header from the workspace.
            development.Visible = false;
            development.Height = 0;
            development.MinimumSize = Size.Empty;

            var refreshTimer = new System.Windows.Forms.Timer { Interval = 500 };
            refreshTimer.Tick += (_, _) => footerStatus.Text = BuildDevelopmentFooterText(development);
            refreshTimer.Start();

            var installation = new Installation(form, liveWindow, footerStatus, refreshTimer);
            Installations.Add(form, installation);
            form.FormClosing += (_, _) => installation.OwnerClosing = true;
            form.FormClosed += (_, _) => installation.Dispose();
        }
        finally
        {
            root.ResumeLayout(true);
            form.ResumeLayout(true);
        }

        return true;
    }

    internal static void ShowLiveMonitor(MainForm form)
    {
        if (!TryInstall(form) || !Installations.TryGetValue(form, out var installation))
            return;

        if (!installation.LiveWindow.Visible)
            installation.LiveWindow.Show(form);

        if (installation.LiveWindow.WindowState == FormWindowState.Minimized)
            installation.LiveWindow.WindowState = FormWindowState.Normal;

        installation.LiveWindow.BringToFront();
        installation.LiveWindow.Activate();
    }

    private static void PrepareHistoryForOnDemand(HistoryWorkspaceControl history)
    {
        // Stored History used to own a permanent 56/330px strip on MainForm. In v2 it is a
        // full-height on-demand surface, so the tab host owns its dimensions instead.
        ExpandableWorkspaceLayout.UseHostManagedHeight(history);
        history.Parent?.Controls.Remove(history);
        history.Dock = DockStyle.Fill;
        history.Margin = Padding.Empty;
        history.MinimumSize = Size.Empty;
        history.MaximumSize = Size.Empty;

        // Show the existing history body without mutating IsExpanded. That property is persisted
        // by Program.cs; opening/re-hosting the on-demand window must not rewrite user settings.
        var bodyLayout = Descendants(history)
            .OfType<TableLayoutPanel>()
            .FirstOrDefault(layout =>
                layout.RowCount == 2 &&
                layout.RowStyles.Count >= 2 &&
                layout.RowStyles[0].SizeType == SizeType.Absolute &&
                Math.Abs(layout.RowStyles[0].Height - 46F) < 0.1F);
        if (bodyLayout?.Parent is { } historyBody)
            historyBody.Visible = true;

        var toggle = Descendants(history)
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(
                button.AccessibleName,
                "Expand or collapse stored history explorer",
                StringComparison.Ordinal));
        if (toggle is not null)
        {
            toggle.Visible = false;
            toggle.TabStop = false;
        }
    }

    private static void PrepareRuntimeHealthForOnDemand(
        RuntimeHealthControl runtimeHealth,
        SupportDiagnosticsControl supportDiagnostics)
    {
        // Runtime Health used to be a permanent top-docked strip on MainForm. Keep the same
        // control instance alive for Commands-menu proxies, but let the on-demand tab own where
        // it is displayed. Do not change IsExpanded here: Program.cs persists that user choice.
        ExpandableWorkspaceLayout.UseHostManagedHeight(runtimeHealth);
        runtimeHealth.Parent?.Controls.Remove(runtimeHealth);
        supportDiagnostics.Parent?.Controls.Remove(supportDiagnostics);

        runtimeHealth.Dock = DockStyle.Top;
        runtimeHealth.Margin = Padding.Empty;
        runtimeHealth.MinimumSize = Size.Empty;
        runtimeHealth.MaximumSize = Size.Empty;

        supportDiagnostics.Dock = DockStyle.Top;
        supportDiagnostics.Margin = Padding.Empty;
    }

    private static Form BuildLiveMonitorWindow(
        MainForm owner,
        Control diagnostics,
        HistoryWorkspaceControl history,
        RuntimeHealthControl runtimeHealth,
        SupportDiagnosticsControl supportDiagnostics)
    {
        var window = new Form
        {
            Text = "Live Monitor & History",
            StartPosition = FormStartPosition.CenterParent,
            ShowInTaskbar = false,
            AutoScaleMode = AutoScaleMode.Dpi,
            MinimumSize = new Size(920, 560),
            ClientSize = new Size(1180, 720),
            BackColor = FluentTheme.Background
        };

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Point(18, 6),
            AccessibleName = "Live monitor, stored history, and runtime health tabs"
        };
        var livePage = new TabPage("Live Activity")
        {
            BackColor = FluentTheme.Background,
            Padding = new Padding(8)
        };
        var historyPage = new TabPage("Stored History")
        {
            BackColor = FluentTheme.Background,
            Padding = new Padding(8)
        };
        var runtimePage = new TabPage("Runtime Health")
        {
            BackColor = FluentTheme.Background,
            Padding = new Padding(8)
        };
        var runtimeHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = FluentTheme.Background,
            AutoScroll = true,
            Margin = Padding.Empty
        };

        diagnostics.Dock = DockStyle.Fill;
        diagnostics.Margin = Padding.Empty;
        history.Dock = DockStyle.Fill;

        livePage.Controls.Add(diagnostics);
        historyPage.Controls.Add(history);
        runtimeHost.Controls.Add(supportDiagnostics);
        runtimeHost.Controls.Add(runtimeHealth);
        runtimeHost.Controls.SetChildIndex(runtimeHealth, 0);
        runtimeHost.Controls.SetChildIndex(supportDiagnostics, 1);
        runtimePage.Controls.Add(runtimeHost);

        tabs.TabPages.Add(livePage);
        tabs.TabPages.Add(historyPage);
        tabs.TabPages.Add(runtimePage);
        window.Controls.Add(tabs);

        FluentTheme.Apply(window);
        return window;
    }

    private static Label BuildFooter(
        TableLayoutPanel root,
        Label versionLabel,
        DevelopmentTaskDashboardControl development)
    {
        root.Controls.Remove(versionLabel);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = versionLabel.BackColor
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var footerStatus = new Label
        {
            Text = BuildDevelopmentFooterText(development),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true,
            ForeColor = FluentTheme.Muted,
            Font = new Font("Segoe UI Variable Text", 8.5F),
            AccessibleName = "Development runtime status"
        };

        var leftSpacer = new Label { Dock = DockStyle.Fill };
        versionLabel.Dock = DockStyle.Fill;
        versionLabel.TextAlign = ContentAlignment.MiddleRight;

        footer.Controls.Add(leftSpacer, 0, 0);
        footer.Controls.Add(footerStatus, 1, 0);
        footer.Controls.Add(versionLabel, 2, 0);
        root.Controls.Add(footer, 0, 4);

        var tip = new ToolTip();
        tip.SetToolTip(footerStatus, "Development controls and sent-message catalog are available from ☰ Commands.");
        footerStatus.Disposed += (_, _) => tip.Dispose();
        return footerStatus;
    }

    private static string BuildDevelopmentFooterText(DevelopmentTaskDashboardControl development)
    {
        var summary = development.FooterSummary;
        return string.IsNullOrWhiteSpace(summary) ? "Development • Ready" : $"Development • {summary}";
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private sealed class Installation : IDisposable
    {
        private readonly MainForm _owner;
        private readonly System.Windows.Forms.Timer _timer;

        public Installation(MainForm owner, Form liveWindow, Label footerStatus, System.Windows.Forms.Timer timer)
        {
            _owner = owner;
            LiveWindow = liveWindow;
            FooterStatus = footerStatus;
            _timer = timer;

            LiveWindow.FormClosing += OnLiveWindowClosing;
        }

        public Form LiveWindow { get; }
        public Label FooterStatus { get; }
        public bool OwnerClosing { get; set; }

        private void OnLiveWindowClosing(object? sender, FormClosingEventArgs e)
        {
            if (OwnerClosing || _owner.IsDisposed || _owner.Disposing)
                return;

            e.Cancel = true;
            LiveWindow.Hide();
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();
            LiveWindow.FormClosing -= OnLiveWindowClosing;
            LiveWindow.Dispose();
        }
    }
}
