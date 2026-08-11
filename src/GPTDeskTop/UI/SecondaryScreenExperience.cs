using System.Runtime.CompilerServices;

namespace GPTDeskTop.UI;

internal static class SecondaryScreenExperienceBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
        => Application.Idle += OnApplicationIdle;

    private static void OnApplicationIdle(object? sender, EventArgs e)
    {
        if (Application.OpenForms.Count == 0) return;
        foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
            SecondaryScreenExperience.Apply(form);
    }
}

internal static class SecondaryScreenExperience
{
    private static readonly ConditionalWeakTable<Control, ControlRegistration> Registrations = new();

    internal static void Apply(Form form)
    {
        if (form.IsDisposed || form.Disposing) return;

        var registration = Registrations.GetValue(form, _ => new ControlRegistration());
        if (registration.InitialExperienceApplied) return;
        registration.InitialExperienceApplied = true;

        RegisterResponsive(form, () => ApplyPresentation(form));
        RegisterDynamicTree(form, form);
        ApplyPresentation(form);
    }

    private static void ApplyPresentation(Form form)
    {
        if (form.IsDisposed || form.Disposing) return;

        if (form is MainForm)
            EnhanceMainForm(form);
        else if (form is SettingsForm)
            EnhanceSettingsForm(form);
        else if (form is MonitorSettingsForm)
            EnhanceMonitorSettingsForm(form);

        foreach (var control in Descendants(form).ToArray())
        {
            switch (control)
            {
                case DevelopmentTaskDashboardControl development:
                    EnhanceDevelopmentDashboard(development);
                    break;
                case RuntimeHealthControl runtimeHealth:
                    EnhanceRuntimeHealth(runtimeHealth);
                    break;
                case HistoryWorkspaceControl history:
                    EnhanceHistory(history);
                    break;
                case SupportDiagnosticsControl support:
                    EnhanceSupportDiagnostics(support);
                    break;
            }
        }
    }

    private static void RegisterDynamicTree(Form form, Control root)
    {
        if (root.IsDisposed) return;

        var registration = Registrations.GetValue(root, _ => new ControlRegistration());
        if (!registration.ChildAddedHooked)
        {
            registration.ChildAddedHooked = true;
            root.ControlAdded += (_, e) =>
            {
                if (form.IsDisposed || e.Control is null || e.Control.IsDisposed) return;
                RegisterDynamicTree(form, e.Control);
                ApplyPresentation(form);
            };
        }

        foreach (Control child in root.Controls)
            RegisterDynamicTree(form, child);
    }

    private static void EnhanceMainForm(Form form)
    {
        EnsureMainWindowDockOrder(form);
        AppendAccessibleHint(form, "Primary operator workspace with reserved top and bottom command surfaces that never overlap the main content.");

        foreach (var button in Descendants(form).OfType<Button>())
        {
            var width = DesiredMainActionButtonWidth(button.Text);
            if (width <= 0) continue;
            EnsureButtonSize(button, width);
        }
    }

    private static void EnsureMainWindowDockOrder(Form form)
    {
        var development = form.Controls.OfType<DevelopmentTaskDashboardControl>().FirstOrDefault();
        var runtime = form.Controls.OfType<RuntimeHealthControl>().FirstOrDefault();
        var support = form.Controls.OfType<SupportDiagnosticsControl>().FirstOrDefault();
        var history = form.Controls.OfType<HistoryWorkspaceControl>().FirstOrDefault();
        var mainContent = form.Controls.Cast<Control>().FirstOrDefault(control => control.Dock == DockStyle.Fill);
        if (mainContent is null) return;

        // WinForms docks direct children in reverse z-order. Keep the Fill surface at index 0
        // and edge-docked surfaces after it so Top/Bottom controls reserve real layout space
        // instead of painting over the primary workspace.
        var desired = new Control?[] { mainContent, history, support, runtime, development }
            .Where(control => control is not null)
            .Cast<Control>()
            .ToArray();

        var alreadyOrdered = desired
            .Select((control, index) => form.Controls.GetChildIndex(control) == index)
            .All(matches => matches);
        if (alreadyOrdered) return;

        form.SuspendLayout();
        try
        {
            for (var index = 0; index < desired.Length; index++)
                form.Controls.SetChildIndex(desired[index], index);
        }
        finally
        {
            form.ResumeLayout(performLayout: true);
        }
    }

    private static int DesiredMainActionButtonWidth(string? text)
        => Normalize(text) switch
        {
            "Launch Chrome" => 116,
            "Hide Chrome" => 104,
            "Show Chrome" => 106,
            "Refresh" => 88,
            "Add Monitor" => 110,
            "Edit Monitor" => 110,
            "Delete" => 84,
            "Start Selected" => 118,
            "Stop Selected" => 116,
            "Start All" => 90,
            "Stop All" => 90,
            "Settings" => 90,
            "Edit Selected Monitor" => 164,
            _ => 0
        };

    private static void EnhanceSettingsForm(Form form)
    {
        form.AccessibleName ??= "GPTDeskTop application settings";
        AppendAccessibleHint(form, "Responsive settings workspace optimized for standard and high-DPI displays.");

        var compact = form.ClientSize.Width < Scale(form, 820);
        var root = Descendants(form).OfType<TableLayoutPanel>()
            .FirstOrDefault(table => table.Dock == DockStyle.Fill && table.ColumnCount == 1 && table.RowCount == 4);
        if (root is not null)
        {
            root.Padding = new Padding(Scale(form, compact ? 12 : 20));
            root.BackColor = FluentTheme.Background;
        }

        var heading = FindLabel(form, "Application Settings");
        if (heading is not null)
        {
            heading.ForeColor = FluentTheme.Text;
            heading.AccessibleName = "Application Settings";
            heading.AccessibleDescription = "Global monitoring, recovery, notification and backup settings.";
        }

        foreach (var tabs in Descendants(form).OfType<TabControl>())
        {
            tabs.Padding = compact ? new Point(Scale(tabs, 9), Scale(tabs, 4)) : new Point(Scale(tabs, 15), Scale(tabs, 5));
            tabs.AccessibleDescription ??= "Settings categories. Use Ctrl+Tab to switch categories.";
            foreach (TabPage page in tabs.TabPages)
            {
                page.AutoScroll = true;
                page.Padding = new Padding(Scale(page, compact ? 10 : 16));
                page.AutoScrollMargin = new Size(Scale(page, 8), Scale(page, 10));
            }
        }

        var save = FindButton(form, "Save Settings");
        if (save is not null)
        {
            save.MinimumSize = new Size(Scale(save, 126), Scale(save, 40));
            FluentTheme.StyleButton(save, primary: true);
            AppendAccessibleHint(save, "Primary action. Ctrl+S.");
        }

        var cancel = FindButton(form, "Cancel");
        if (cancel is not null)
            cancel.MinimumSize = new Size(Scale(cancel, 88), Scale(cancel, 40));

        var export = FindButton(form, "Export Configuration Backup");
        if (export is not null)
        {
            export.MinimumSize = new Size(Scale(export, 182), Scale(export, 38));
            FluentTheme.StyleButton(export);
        }

        var import = FindButton(form, "Import Configuration Backup");
        if (import is not null)
            import.MinimumSize = new Size(Scale(import, 182), Scale(import, 38));

        foreach (var flow in Descendants(form).OfType<FlowLayoutPanel>())
        {
            if (flow.Controls.OfType<Button>().Count() < 2) continue;
            flow.WrapContents = compact;
            flow.AutoScroll = false;
            flow.Padding = new Padding(flow.Padding.Left, Scale(flow, 7), flow.Padding.Right, flow.Padding.Bottom);
        }

        foreach (var status in Descendants(form).OfType<Label>().Where(label => label.AccessibleRole == AccessibleRole.StatusBar))
            EnhanceStatusLabel(status, "Application settings status");

        foreach (var warning in Descendants(form).OfType<Label>().Where(label => label.Text.Contains("Sensitive data notice", StringComparison.OrdinalIgnoreCase)))
        {
            warning.BackColor = FluentTheme.WarningSubtle;
            warning.ForeColor = FluentTheme.Warning;
            warning.Padding = new Padding(Scale(warning, 10), Scale(warning, 7), Scale(warning, 10), Scale(warning, 7));
            warning.AccessibleName = "Sensitive backup data warning";
        }
    }

    private static void EnhanceMonitorSettingsForm(Form form)
    {
        form.AccessibleName ??= "Monitor settings";
        AppendAccessibleHint(form, "Responsive monitor configuration workspace with live runtime-state context.");

        var compact = form.ClientSize.Width < Scale(form, 800);
        var root = Descendants(form).OfType<TableLayoutPanel>()
            .FirstOrDefault(table => table.Dock == DockStyle.Fill && table.ColumnCount == 1 && table.RowCount == 3);
        if (root is not null)
            root.Padding = new Padding(Scale(form, compact ? 12 : 20));

        var header = Descendants(form).OfType<TableLayoutPanel>()
            .FirstOrDefault(table => table.ColumnCount == 2 && table.RowCount == 3 && table.GetControlFromPosition(1, 0) is Panel);
        if (header is not null)
        {
            var statusWidth = compact ? 108 : 136;
            header.ColumnStyles[1].SizeType = SizeType.Absolute;
            header.ColumnStyles[1].Width = Scale(header, statusWidth);
        }

        foreach (var tabs in Descendants(form).OfType<TabControl>())
        {
            tabs.Padding = compact ? new Point(Scale(tabs, 9), Scale(tabs, 4)) : new Point(Scale(tabs, 15), Scale(tabs, 5));
            tabs.AccessibleDescription ??= "Monitor settings categories. Use Ctrl+Tab to move between General, Rotation and Model Routing.";
            foreach (TabPage page in tabs.TabPages)
            {
                page.AutoScroll = true;
                page.Padding = new Padding(Scale(page, compact ? 10 : 16));
                page.AutoScrollMargin = new Size(Scale(page, 8), Scale(page, 10));
            }
        }

        var runtimeStatus = Descendants(form).OfType<Label>()
            .FirstOrDefault(label => label.Font.Bold && label.TextAlign == ContentAlignment.MiddleCenter && FindAncestorPanel(label) is not null);
        if (runtimeStatus is not null)
            EnhanceStatusLabel(runtimeStatus, "Selected monitor runtime state");

        var save = FindButton(form, "Save Monitor");
        if (save is not null)
        {
            save.MinimumSize = new Size(Scale(save, 126), Scale(save, 40));
            FluentTheme.StyleButton(save, primary: true);
            AppendAccessibleHint(save, "Primary action. Ctrl+S.");
        }

        var cancel = FindButton(form, "Cancel");
        if (cancel is not null)
            cancel.MinimumSize = new Size(Scale(cancel, 88), Scale(cancel, 40));

        foreach (var check in Descendants(form).OfType<CheckBox>())
        {
            check.Margin = new Padding(check.Margin.Left, Scale(check, 6), check.Margin.Right, Scale(check, 6));
            if (!check.Enabled)
                check.ForeColor = FluentTheme.DisabledText;
        }

        foreach (var box in Descendants(form).OfType<TextBox>().Where(box => box.PlaceholderText.Contains("Auto", StringComparison.OrdinalIgnoreCase)))
            box.AccessibleDescription ??= "Leave Auto to preserve the current ChatGPT model selection.";
    }

    private static void EnhanceDevelopmentDashboard(DevelopmentTaskDashboardControl control)
    {
        RegisterResponsive(control, () => EnhanceDevelopmentDashboard(control));
        control.BackColor = FluentTheme.Background;
        control.Padding = new Padding(Scale(control, 12), Scale(control, 6), Scale(control, 12), Scale(control, 4));
        control.AccessibleDescription ??= "Development-plan command center with fully visible lifecycle and configuration actions.";

        var frame = control.Controls.OfType<Panel>().FirstOrDefault(panel => panel.Dock == DockStyle.Fill);
        if (frame is not null)
        {
            frame.BackColor = FluentTheme.SurfaceRaised;
            frame.Padding = new Padding(Scale(frame, 11), Scale(frame, 6), Scale(frame, 11), Scale(frame, 8));
        }

        var header = Descendants(control).OfType<TableLayoutPanel>()
            .FirstOrDefault(table => table.ColumnCount == 5 && table.RowCount == 1 && Descendants(table).OfType<Label>().Any(label => label.Text == "Development Plan"));
        if (header is not null && header.ColumnStyles.Count >= 5)
        {
            var compact = control.Width < Scale(control, 920);
            SetAbsoluteColumn(header, 0, Scale(header, compact ? 150 : 175));
            SetAbsoluteColumn(header, 1, Scale(header, compact ? 112 : 120));
            header.ColumnStyles[2].SizeType = SizeType.Percent;
            header.ColumnStyles[2].Width = 100;
            SetAbsoluteColumn(header, 3, Scale(header, compact ? 92 : 108));
            SetAbsoluteColumn(header, 4, Scale(header, 112));
        }

        var actions = Descendants(control).OfType<FlowLayoutPanel>()
            .FirstOrDefault(flow => flow.Controls.OfType<Button>().Any(button => Normalize(button.Text) == "Start")
                                    && flow.Controls.OfType<Button>().Any(button => Normalize(button.Text) == "Schedule"));
        if (actions is not null)
        {
            actions.WrapContents = false;
            actions.AutoScroll = false;
            actions.Padding = new Padding(0, Scale(actions, 1), 0, 0);
        }

        foreach (var button in Descendants(control).OfType<Button>())
        {
            var width = DesiredDevelopmentButtonWidth(button.Text);
            EnsureButtonSize(button, width > 0 ? width : 90);
        }
    }

    private static int DesiredDevelopmentButtonWidth(string? text)
        => Normalize(text) switch
        {
            "Start" => 78,
            "Pause" => 78,
            "Resume" => 88,
            "Stop" => 78,
            "Messages" => 96,
            "Schedule" => 96,
            "Collapse" => 100,
            "Details" => 92,
            _ => 90
        };

    private static void EnhanceRuntimeHealth(RuntimeHealthControl control)
    {
        RegisterResponsive(control, () => EnhanceRuntimeHealth(control));
        control.BackColor = FluentTheme.Background;
        control.Padding = new Padding(Scale(control, 12), Scale(control, 4), Scale(control, 12), Scale(control, 4));
        control.AccessibleDescription ??= "Responsive runtime health center for Chrome, SQLite, conversations, monitors and recovery state.";

        var frame = control.Controls.OfType<Panel>().FirstOrDefault(panel => panel.Dock == DockStyle.Fill);
        if (frame is not null)
        {
            frame.BackColor = FluentTheme.SurfaceRaised;
            frame.Padding = new Padding(Scale(frame, 11), Scale(frame, 6), Scale(frame, 11), Scale(frame, 8));
        }

        var header = Descendants(control).OfType<TableLayoutPanel>()
            .FirstOrDefault(table => table.ColumnCount == 8 && table.RowCount == 1);
        if (header is not null)
            ApplyRuntimeHeaderResponsive(control, header);

        var metrics = Descendants(control).OfType<TableLayoutPanel>()
            .FirstOrDefault(table => table.ColumnCount == 5 && table.RowCount == 1);
        if (metrics is not null)
        {
            metrics.Padding = new Padding(0, Scale(metrics, 7), 0, 0);
            foreach (var label in Descendants(metrics).OfType<Label>())
                label.AutoEllipsis = true;
        }

        foreach (var status in Descendants(control).OfType<Label>().Where(label => label.AccessibleRole == AccessibleRole.StatusBar))
            EnhanceStatusLabel(status, "Runtime health status");

        foreach (var button in Descendants(control).OfType<Button>())
        {
            var width = DesiredRuntimeButtonWidth(button.Text);
            EnsureButtonSize(button, width);
        }
    }

    private static int DesiredRuntimeButtonWidth(string? text)
        => Normalize(text) switch
        {
            "Refresh" => 90,
            "Repair…" => 90,
            "Retry" => 80,
            "Collapse" => 100,
            "Details" => 94,
            _ => 88
        };

    private static void ApplyRuntimeHeaderResponsive(Control owner, TableLayoutPanel header)
    {
        if (header.ColumnStyles.Count < 8) return;

        var compact = owner.Width < Scale(owner, 1080);
        var veryCompact = owner.Width < Scale(owner, 900);
        var summary = header.GetControlFromPosition(2, 0);
        var lastChecked = header.GetControlFromPosition(3, 0);

        SetAbsoluteColumn(header, 0, Scale(header, compact ? 140 : 150));
        SetAbsoluteColumn(header, 1, Scale(header, compact ? 116 : 125));

        if (veryCompact)
        {
            if (summary is not null) summary.Visible = false;
            SetAbsoluteColumn(header, 2, 0);
        }
        else
        {
            if (summary is not null) summary.Visible = true;
            header.ColumnStyles[2].SizeType = SizeType.Percent;
            header.ColumnStyles[2].Width = 100;
        }

        if (compact)
        {
            if (lastChecked is not null) lastChecked.Visible = false;
            SetAbsoluteColumn(header, 3, 0);
        }
        else
        {
            if (lastChecked is not null) lastChecked.Visible = true;
            SetAbsoluteColumn(header, 3, Scale(header, 140));
        }

        // Each action column includes the button's minimum width plus Fluent margins.
        // Never collapse these columns to text-truncating widths.
        SetAbsoluteColumn(header, 4, Scale(header, 102));
        SetAbsoluteColumn(header, 5, Scale(header, 102));
        SetAbsoluteColumn(header, 6, Scale(header, 92));
        SetAbsoluteColumn(header, 7, Scale(header, 112));
    }

    private static void EnhanceHistory(HistoryWorkspaceControl control)
    {
        RegisterResponsive(control, () => EnhanceHistory(control));
        control.BackColor = FluentTheme.Background;
        control.Padding = new Padding(Scale(control, 12), Scale(control, 4), Scale(control, 12), Scale(control, 8));
        control.AccessibleDescription ??= "Responsive stored-history workspace with search, filters, copy and CSV export.";

        var compact = control.Width < Scale(control, 900);
        var header = Descendants(control).OfType<TableLayoutPanel>()
            .FirstOrDefault(table => table.ColumnCount == 3 && table.RowCount == 1 && Descendants(table).OfType<Label>().Any(label => label.Text == "Stored History Explorer"));
        if (header is not null && header.ColumnStyles.Count >= 3)
        {
            SetAbsoluteColumn(header, 0, Scale(header, compact ? 148 : 180));
            SetAbsoluteColumn(header, 2, Scale(header, compact ? 108 : 116));
        }

        var filters = Descendants(control).OfType<FlowLayoutPanel>()
            .FirstOrDefault(flow => Descendants(flow).OfType<TextBox>().Any(box => box.PlaceholderText.Contains("Search history", StringComparison.OrdinalIgnoreCase)));
        if (filters is not null)
        {
            filters.WrapContents = true;
            filters.AutoScroll = false;
            filters.Padding = new Padding(0, Scale(filters, 3), 0, Scale(filters, 3));

            var bodyLayout = filters.Parent as TableLayoutPanel;
            if (bodyLayout is not null && bodyLayout.RowStyles.Count > 0)
            {
                bodyLayout.RowStyles[0].SizeType = compact ? SizeType.AutoSize : SizeType.Absolute;
                bodyLayout.RowStyles[0].Height = compact ? 0 : Scale(bodyLayout, 46);
            }
        }

        var search = Descendants(control).OfType<TextBox>()
            .FirstOrDefault(box => box.PlaceholderText.Contains("Search history", StringComparison.OrdinalIgnoreCase));
        if (search is not null)
        {
            search.Width = Scale(search, compact ? 220 : 280);
            search.AccessibleDescription ??= "Search visible and persisted history. Ctrl+F focuses this field.";
        }

        foreach (var combo in Descendants(control).OfType<ComboBox>())
        {
            combo.Width = Math.Max(combo.Width, Scale(combo, 128));
            combo.MaxDropDownItems = Math.Max(combo.MaxDropDownItems, 12);
        }

        foreach (var button in Descendants(control).OfType<Button>())
        {
            var width = DesiredHistoryButtonWidth(button.Text);
            EnsureButtonSize(button, width);
        }

        foreach (var grid in Descendants(control).OfType<DataGridView>())
        {
            grid.RowTemplate.Height = Math.Max(grid.RowTemplate.Height, Scale(grid, 34));
            grid.ColumnHeadersHeight = Math.Max(grid.ColumnHeadersHeight, Scale(grid, 36));
            grid.ShowCellToolTips = true;
            grid.BackgroundColor = FluentTheme.Surface;
            grid.GridColor = FluentTheme.Border;
        }

        foreach (var status in Descendants(control).OfType<Label>().Where(label => label.AccessibleRole == AccessibleRole.StatusBar))
            EnhanceStatusLabel(status, "Stored history result summary");
    }

    private static int DesiredHistoryButtonWidth(string? text)
        => Normalize(text) switch
        {
            "Clear Filters" => 104,
            "Refresh" => 88,
            "Copy Selected" => 118,
            "Export Visible CSV" => 142,
            "Collapse" => 100,
            "History" => 92,
            _ => 88
        };

    private static void EnhanceSupportDiagnostics(SupportDiagnosticsControl control)
    {
        RegisterResponsive(control, () => EnhanceSupportDiagnostics(control));
        control.BackColor = FluentTheme.Background;
        control.Padding = new Padding(Scale(control, 12), Scale(control, 3), Scale(control, 12), Scale(control, 5));

        var compact = control.Width < Scale(control, 720);
        var layout = Descendants(control).OfType<TableLayoutPanel>()
            .FirstOrDefault(table => table.ColumnCount == 3 && table.RowCount == 1 && Descendants(table).OfType<Label>().Any(label => label.Text == "Support Diagnostics"));
        if (layout is not null && layout.ColumnStyles.Count >= 3)
            SetAbsoluteColumn(layout, 0, Scale(layout, compact ? 138 : 170));

        var create = FindButton(control, "Create Support Bundle");
        if (create is not null)
        {
            create.MinimumSize = new Size(Scale(create, compact ? 150 : 174), Scale(create, 38));
            FluentTheme.StyleButton(create, primary: true);
            create.AccessibleDescription ??= "Create a privacy-safe support ZIP. Ctrl+Shift+B.";
        }

        foreach (var status in Descendants(control).OfType<Label>().Where(label => label.AccessibleRole == AccessibleRole.StatusBar))
            EnhanceStatusLabel(status, "Support bundle status");
    }

    private static void EnhanceStatusLabel(Label label, string accessibleName)
    {
        label.AccessibleName ??= accessibleName;
        label.AutoEllipsis = true;
        label.Padding = new Padding(Scale(label, 8), Scale(label, 3), Scale(label, 8), Scale(label, 3));
        ApplyStatusPresentation(label);

        var registration = Registrations.GetValue(label, _ => new ControlRegistration());
        if (registration.TextChangedHooked) return;
        registration.TextChangedHooked = true;
        label.TextChanged += (_, _) => ApplyStatusPresentation(label);
    }

    private static void ApplyStatusPresentation(Label label)
    {
        var text = Normalize(label.Text);
        var (foreground, surface) = text switch
        {
            _ when ContainsAny(text, "error", "failed", "invalid", "blocked", "crash") => (FluentTheme.Danger, FluentTheme.DangerSubtle),
            _ when ContainsAny(text, "healthy", "ready", "running", "connected", "success", "created", "clear", "verified") => (FluentTheme.Success, FluentTheme.SuccessSubtle),
            _ when ContainsAny(text, "checking", "loading", "creating", "collecting", "refreshing", "working", "starting", "saving", "importing", "exporting") => (FluentTheme.Info, FluentTheme.InfoSubtle),
            _ when ContainsAny(text, "warning", "pending", "stopped", "unknown", "not checked", "retry", "deferred") => (FluentTheme.Warning, FluentTheme.WarningSubtle),
            _ => (FluentTheme.MutedStrong, FluentTheme.SurfaceAlt)
        };

        label.ForeColor = foreground;
        if (label.Parent is Panel host)
            host.BackColor = surface;
    }

    private static void EnsureButtonSize(Button button, int logicalWidth, int logicalHeight = 36)
    {
        button.MinimumSize = new Size(
            Math.Max(button.MinimumSize.Width, Scale(button, logicalWidth)),
            Math.Max(button.MinimumSize.Height, Scale(button, logicalHeight)));
        button.Padding = new Padding(Scale(button, 10), Scale(button, 5), Scale(button, 10), Scale(button, 5));
        button.AutoEllipsis = false;
    }

    private static void RegisterResponsive(Control control, Action callback)
    {
        var registration = Registrations.GetValue(control, _ => new ControlRegistration());
        if (registration.ResponsiveHooked) return;

        registration.ResponsiveHooked = true;
        control.SizeChanged += (_, _) =>
        {
            if (!control.IsDisposed) callback();
        };

        if (control is Form form)
        {
            form.DpiChanged += (_, _) =>
            {
                if (!form.IsDisposed) callback();
            };
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child))
                yield return nested;
        }
    }

    private static Button? FindButton(Control root, string text)
        => Descendants(root).OfType<Button>().FirstOrDefault(button => Normalize(button.Text).Equals(text, StringComparison.OrdinalIgnoreCase));

    private static Label? FindLabel(Control root, string text)
        => Descendants(root).OfType<Label>().FirstOrDefault(label => Normalize(label.Text).Equals(text, StringComparison.OrdinalIgnoreCase));

    private static Panel? FindAncestorPanel(Control control)
    {
        for (var parent = control.Parent; parent is not null; parent = parent.Parent)
            if (parent is Panel panel) return panel;
        return null;
    }

    private static void SetAbsoluteColumn(TableLayoutPanel table, int index, int width)
    {
        if (index < 0 || index >= table.ColumnStyles.Count) return;
        table.ColumnStyles[index].SizeType = SizeType.Absolute;
        table.ColumnStyles[index].Width = width;
    }

    private static void AppendAccessibleHint(Control control, string hint)
    {
        if (string.IsNullOrWhiteSpace(control.AccessibleDescription))
        {
            control.AccessibleDescription = hint;
            return;
        }

        if (!control.AccessibleDescription.Contains(hint, StringComparison.OrdinalIgnoreCase))
            control.AccessibleDescription = $"{control.AccessibleDescription.Trim()} {hint}";
    }

    private static int Scale(Control control, int logicalPixels)
        => Math.Max(1, (int)Math.Round(logicalPixels * Math.Max(96, control.DeviceDpi) / 96d));

    private static string Normalize(string? value)
        => (value ?? string.Empty).Replace("&", string.Empty).Trim();

    private static bool ContainsAny(string value, params string[] tokens)
        => tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private sealed class ControlRegistration
    {
        internal bool InitialExperienceApplied { get; set; }
        internal bool ChildAddedHooked { get; set; }
        internal bool ResponsiveHooked { get; set; }
        internal bool TextChangedHooked { get; set; }
    }
}
