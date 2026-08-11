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

        RegisterResponsive(form, () => Apply(form));

        if (form is SettingsForm)
            EnhanceSettingsForm(form);
        else if (form is MonitorSettingsForm)
            EnhanceMonitorSettingsForm(form);

        foreach (var control in Descendants(form).ToArray())
        {
            switch (control)
            {
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
            button.MinimumSize = new Size(Math.Max(button.MinimumSize.Width, Scale(button, 72)), Math.Max(button.MinimumSize.Height, Scale(button, 36)));
            button.AutoEllipsis = true;
        }
    }

    private static void ApplyRuntimeHeaderResponsive(Control owner, TableLayoutPanel header)
    {
        if (header.ColumnStyles.Count < 8) return;

        var compact = owner.Width < Scale(owner, 930);
        var veryCompact = owner.Width < Scale(owner, 760);
        var summary = header.GetControlFromPosition(2, 0);
        var lastChecked = header.GetControlFromPosition(3, 0);

        SetAbsoluteColumn(header, 0, Scale(header, compact ? 132 : 150));
        SetAbsoluteColumn(header, 1, Scale(header, compact ? 108 : 125));

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
            SetAbsoluteColumn(header, 3, Scale(header, 150));
        }

        for (var column = 4; column <= 7; column++)
            SetAbsoluteColumn(header, column, Scale(header, compact ? 72 : 86));
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
            SetAbsoluteColumn(header, 2, Scale(header, compact ? 88 : 100));
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
            button.MinimumSize = new Size(Math.Max(button.MinimumSize.Width, Scale(button, 78)), Math.Max(button.MinimumSize.Height, Scale(button, 36)));
            button.AutoEllipsis = true;
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
        internal bool ResponsiveHooked { get; set; }
        internal bool TextChangedHooked { get; set; }
    }
}