using System.Runtime.CompilerServices;

namespace GPTDeskTop.UI;

/// <summary>
/// GPTDeskTop 2.0 operator workspace. All persistent runtime surfaces stay inside MainForm.
/// Secondary dialogs are created only when the operator explicitly opens them; this experience
/// never creates, hides, or keeps a second top-level Form alive behind the main window.
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

        // CompactTopCommandMenuExperience owns the command menu. Wait until it has harvested the
        // canonical command buttons before compacting permanent dashboard surfaces.
        if (form.MainMenuStrip is null)
            return false;

        var development = Descendants(form).OfType<DevelopmentTaskDashboardControl>().FirstOrDefault();
        var runtimeHealth = Descendants(form).OfType<RuntimeHealthControl>().FirstOrDefault();
        var root = form.Controls
            .OfType<TableLayoutPanel>()
            .FirstOrDefault(candidate => candidate.Dock == DockStyle.Fill && candidate.RowCount == 5 && candidate.ColumnCount == 1);
        if (development is null || runtimeHealth is null || root is null || root.RowStyles.Count < 5)
            return false;

        // MainForm already owns exactly one canonical diagnostics surface: Live Activity + Stored
        // History. Keep that control in MainForm and collapse its row until the operator asks for it.
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
            diagnostics.Dock = DockStyle.Fill;
            diagnostics.Margin = Padding.Empty;
            diagnostics.MinimumSize = Size.Empty;
            diagnostics.MaximumSize = Size.Empty;
            diagnostics.Visible = false;

            root.RowStyles[2].SizeType = SizeType.Percent;
            root.RowStyles[2].Height = 100F;
            root.RowStyles[3].SizeType = SizeType.Absolute;
            root.RowStyles[3].Height = 0F;

            // Runtime Health and the lazily-created Support Diagnostics remain direct children of
            // MainForm. Never detach or re-parent them into another Form.
            var footerStatus = BuildFooter(root, versionLabel, development);

            // Keep the development control alive because its existing buttons remain the single
            // command source used by the compact Commands menu, but remove its permanent header.
            development.Visible = false;
            development.Height = 0;
            development.MinimumSize = Size.Empty;
            runtimeHealth.Visible = false;
            runtimeHealth.Height = 0;
            runtimeHealth.MinimumSize = Size.Empty;

            var refreshTimer = new System.Windows.Forms.Timer { Interval = 500 };
            refreshTimer.Tick += (_, _) => footerStatus.Text = BuildDevelopmentFooterText(development);
            refreshTimer.Start();

            var installation = new Installation(form, root, diagnostics, footerStatus, refreshTimer);
            Installations.Add(form, installation);
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

        installation.ShowLiveMonitor();
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
        private readonly TableLayoutPanel _root;
        private readonly Control _diagnostics;
        private readonly System.Windows.Forms.Timer _timer;
        private bool _disposed;

        public Installation(
            MainForm owner,
            TableLayoutPanel root,
            Control diagnostics,
            Label footerStatus,
            System.Windows.Forms.Timer timer)
        {
            _owner = owner;
            _root = root;
            _diagnostics = diagnostics;
            FooterStatus = footerStatus;
            _timer = timer;
        }

        public Label FooterStatus { get; }

        internal void ShowLiveMonitor()
        {
            if (_disposed || _owner.IsDisposed || _owner.Disposing)
                return;

            _root.SuspendLayout();
            _owner.SuspendLayout();
            try
            {
                // Reveal the canonical diagnostics inside the one main window. No secondary Form is
                // created, no hidden window is retained, and the same controls keep their state.
                _diagnostics.Visible = true;
                _root.RowStyles[2].SizeType = SizeType.Percent;
                _root.RowStyles[2].Height = 58F;
                _root.RowStyles[3].SizeType = SizeType.Percent;
                _root.RowStyles[3].Height = 42F;
                _diagnostics.BringToFront();
            }
            finally
            {
                _root.ResumeLayout(true);
                _owner.ResumeLayout(true);
            }

            if (_diagnostics.CanFocus)
                _diagnostics.Focus();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _timer.Dispose();
        }
    }
}
