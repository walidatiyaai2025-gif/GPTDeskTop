using System.Runtime.CompilerServices;

namespace GPTDeskTop.UI;

/// <summary>
/// GPTDeskTop 2.0 operator workspace: the main surface is reserved for open ChatGPT tabs and
/// monitor state. The canonical MainForm live-activity/stored-history surface, runtime health and
/// lazily-created support diagnostics remain available on demand without adding cold-start work.
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
        var root = form.Controls
            .OfType<TableLayoutPanel>()
            .FirstOrDefault(candidate => candidate.Dock == DockStyle.Fill && candidate.RowCount == 5 && candidate.ColumnCount == 1);
        if (development is null || runtimeHealth is null || root is null || root.RowStyles.Count < 5)
            return false;

        // MainForm already owns the canonical diagnostics split: Live Activity on one side and
        // Stored History on the other. Reuse that exact control instead of requiring the removed
        // eager HistoryWorkspaceControl and creating a second history data/grid pipeline.
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

            PrepareDiagnosticsForOnDemand(diagnostics);
            PrepareRuntimeHealthForOnDemand(runtimeHealth);
            var liveWindow = BuildLiveMonitorWindow(form, diagnostics, runtimeHealth);
            var footerStatus = BuildFooter(root, versionLabel, development);

            // Keep the control alive because its existing buttons remain the single command source
            // used by the compact Commands menu, but remove its permanent header from the workspace.
            development.Visible = false;
            development.Height = 0;
            development.MinimumSize = Size.Empty;

            var refreshTimer = new System.Windows.Forms.Timer { Interval = 500 };
            refreshTimer.Tick += (_, _) => footerStatus.Text = BuildDevelopmentFooterText(development);
            refreshTimer.Start();

            var installation = new Installation(form, liveWindow, runtimeHealth, footerStatus, refreshTimer);
            Installations.Add(form, installation);
            installation.TryAttachSupportDiagnostics();
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

    private static void PrepareDiagnosticsForOnDemand(Control diagnostics)
    {
        diagnostics.Dock = DockStyle.Fill;
        diagnostics.Margin = Padding.Empty;
        diagnostics.MinimumSize = Size.Empty;
        diagnostics.MaximumSize = Size.Empty;
    }

    private static void PrepareRuntimeHealthForOnDemand(RuntimeHealthControl runtimeHealth)
    {
        // Runtime Health used to be a permanent top-docked strip on MainForm. Keep the same
        // control instance alive for Commands-menu proxies, but let the on-demand tab own where
        // it is displayed. Do not change IsExpanded here: Program.cs persists that user choice.
        ExpandableWorkspaceLayout.UseHostManagedHeight(runtimeHealth);
        runtimeHealth.Parent?.Controls.Remove(runtimeHealth);
        runtimeHealth.Dock = DockStyle.Top;
        runtimeHealth.Margin = Padding.Empty;
        runtimeHealth.MinimumSize = Size.Empty;
        runtimeHealth.MaximumSize = Size.Empty;
    }

    private static LiveWindowParts BuildLiveMonitorWindow(
        MainForm owner,
        Control diagnostics,
        RuntimeHealthControl runtimeHealth)
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
            AccessibleName = "Live monitor, stored history, runtime health, and support diagnostics tabs"
        };
        var livePage = new TabPage("Live Monitor & History")
        {
            BackColor = FluentTheme.Background,
            Padding = new Padding(8),
            AccessibleDescription = "Canonical MainForm diagnostics surface containing Live Activity and Stored History."
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
        var supportPlaceholder = BuildSupportPlaceholder(runtimeHealth);

        diagnostics.Dock = DockStyle.Fill;
        diagnostics.Margin = Padding.Empty;
        livePage.Controls.Add(diagnostics);

        runtimeHost.Controls.Add(supportPlaceholder);
        runtimeHost.Controls.Add(runtimeHealth);
        runtimeHost.Controls.SetChildIndex(runtimeHealth, 0);
        runtimeHost.Controls.SetChildIndex(supportPlaceholder, 1);
        runtimePage.Controls.Add(runtimeHost);

        tabs.TabPages.Add(livePage);
        tabs.TabPages.Add(runtimePage);
        window.Controls.Add(tabs);

        FluentTheme.Apply(window);
        return new LiveWindowParts(window, tabs, runtimePage, runtimeHost, supportPlaceholder);
    }

    private static Control BuildSupportPlaceholder(RuntimeHealthControl runtimeHealth)
    {
        var placeholder = new Panel
        {
            Dock = DockStyle.Top,
            Height = 58,
            MinimumSize = new Size(0, 58),
            BackColor = FluentTheme.Background,
            Padding = new Padding(12, 3, 12, 5),
            AccessibleName = "Lazy support diagnostics placeholder"
        };
        var frame = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = FluentTheme.Surface,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(12, 6, 12, 6)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = FluentTheme.Surface
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var message = new Label
        {
            Text = "Support Diagnostics stays unloaded until requested, preserving fast startup.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            ForeColor = FluentTheme.Muted
        };
        var load = new Button
        {
            Text = "Load Support Diagnostics",
            AutoSize = true,
            AccessibleName = "Load support diagnostics on demand"
        };
        FluentTheme.StyleButton(load, primary: true);
        load.Click += (_, _) =>
        {
            // Program.cs owns creation and persistence. Expanding the existing Runtime Health
            // control invokes its registered lazy factory; the owner ControlAdded hook below then
            // rehosts that same SupportDiagnosticsControl instance into this on-demand window.
            if (!runtimeHealth.IsExpanded)
                runtimeHealth.IsExpanded = true;
        };

        layout.Controls.Add(message, 0, 0);
        layout.Controls.Add(load, 1, 0);
        frame.Controls.Add(layout);
        placeholder.Controls.Add(frame);
        return placeholder;
    }

    private static Label BuildFooter(
        TableLayoutPanel root,
        Label versionLabel,
        DevelopmentTaskDashboardControl development)
    {
        root.Controls.Remove(versionLabel);

        var footerStatus = new Label
        {
            Text = BuildDevelopmentFooterText(development),
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(8, 0, 8, 0),
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true,
            ForeColor = FluentTheme.Muted,
            Font = new Font("Segoe UI Variable Text", 8.5F),
            AccessibleName = "Development runtime status"
        };

        var leftSpacer = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };

        versionLabel.Dock = DockStyle.Fill;
        versionLabel.Margin = Padding.Empty;
        versionLabel.Padding = new Padding(8, 0, 0, 0);
        versionLabel.TextAlign = ContentAlignment.MiddleRight;
        versionLabel.AutoEllipsis = true;

        // MainForm originally reserves only 24px for row 4. That is too small once the footer
        // contains two independently rendered text regions and becomes especially fragile under
        // Windows DPI scaling. Let the v2 footer own a single, font-aware row height instead.
        var footerHeight = Math.Max(
            34F,
            Math.Max(versionLabel.Font.Height, footerStatus.Font.Height) + 14F);
        root.RowStyles[4].SizeType = SizeType.Absolute;
        root.RowStyles[4].Height = footerHeight;

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(0, 2, 0, 0),
            MinimumSize = new Size(0, (int)Math.Ceiling(footerHeight)),
            BackColor = versionLabel.BackColor
        };

        // Preserve a visually centered development status while giving it more horizontal room.
        // Both text regions ellipsize inside their own cells instead of painting into neighbours.
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24F));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        footer.Controls.Add(leftSpacer, 0, 0);
        footer.Controls.Add(footerStatus, 1, 0);
        footer.Controls.Add(versionLabel, 2, 0);
        root.Controls.Add(footer, 0, 4);

        var tip = new ToolTip();
        tip.SetToolTip(footerStatus, "Development controls and sent-message catalog are available from ☰ Commands.");
        tip.SetToolTip(versionLabel, versionLabel.Text);
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
        private readonly RuntimeHealthControl _runtimeHealth;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly Panel _runtimeHost;
        private Control? _supportPlaceholder;
        private SupportDiagnosticsControl? _supportDiagnostics;
        private bool _attachScheduled;

        public Installation(
            MainForm owner,
            LiveWindowParts liveWindow,
            RuntimeHealthControl runtimeHealth,
            Label footerStatus,
            System.Windows.Forms.Timer timer)
        {
            _owner = owner;
            _runtimeHealth = runtimeHealth;
            LiveWindow = liveWindow.Window;
            FooterStatus = footerStatus;
            _timer = timer;
            _runtimeHost = liveWindow.RuntimeHost;
            _supportPlaceholder = liveWindow.SupportPlaceholder;

            LiveWindow.FormClosing += OnLiveWindowClosing;
            _owner.ControlAdded += OnOwnerControlAdded;
            _runtimeHealth.ExpandedChanged += OnRuntimeHealthExpandedChanged;
        }

        public Form LiveWindow { get; }
        public Label FooterStatus { get; }
        public bool OwnerClosing { get; set; }

        private void OnOwnerControlAdded(object? sender, ControlEventArgs e)
        {
            if (e.Control is SupportDiagnosticsControl)
                ScheduleSupportAttach();
        }

        private void OnRuntimeHealthExpandedChanged(object? sender, EventArgs e)
            => ScheduleSupportAttach();

        private void ScheduleSupportAttach()
        {
            if (_attachScheduled || _supportDiagnostics is not null || _owner.IsDisposed || _owner.Disposing)
                return;

            _attachScheduled = true;
            try
            {
                _owner.BeginInvoke((Action)(() =>
                {
                    _attachScheduled = false;
                    TryAttachSupportDiagnostics();
                }));
            }
            catch (InvalidOperationException)
            {
                _attachScheduled = false;
            }
        }

        internal void TryAttachSupportDiagnostics()
        {
            if (_supportDiagnostics is not null || _owner.IsDisposed || _owner.Disposing)
                return;

            // Program.cs adds the lazy SupportDiagnosticsControl directly to MainForm. Never create
            // a second instance here: wait for that canonical factory and move the exact instance.
            var support = _owner.Controls.OfType<SupportDiagnosticsControl>().FirstOrDefault();
            if (support is null)
                return;

            support.Parent?.Controls.Remove(support);
            support.Dock = DockStyle.Top;
            support.Margin = Padding.Empty;
            support.MaximumSize = Size.Empty;

            if (_supportPlaceholder is not null)
            {
                _runtimeHost.Controls.Remove(_supportPlaceholder);
                _supportPlaceholder.Dispose();
                _supportPlaceholder = null;
            }

            _runtimeHost.Controls.Add(support);
            _runtimeHost.Controls.SetChildIndex(_runtimeHealth, 0);
            _runtimeHost.Controls.SetChildIndex(support, 1);
            _supportDiagnostics = support;
        }

        private void OnLiveWindowClosing(object? sender, FormClosingEventArgs e)
        {
            if (OwnerClosing || _owner.IsDisposed || _owner.Disposing)
                return;

            e.Cancel = true;
            LiveWindow.Hide();
        }

        public void Dispose()
        {
            _owner.ControlAdded -= OnOwnerControlAdded;
            _runtimeHealth.ExpandedChanged -= OnRuntimeHealthExpandedChanged;
            _timer.Stop();
            _timer.Dispose();
            LiveWindow.FormClosing -= OnLiveWindowClosing;
            LiveWindow.Dispose();
        }
    }

    private sealed record LiveWindowParts(
        Form Window,
        TabControl Tabs,
        TabPage RuntimePage,
        Panel RuntimeHost,
        Control SupportPlaceholder);
}
