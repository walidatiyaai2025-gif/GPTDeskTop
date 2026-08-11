using System.Runtime.CompilerServices;

namespace GPTDeskTop.UI;

internal static class MainDashboardExperienceBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
        => Application.Idle += ApplyDashboardExperience;

    private static void ApplyDashboardExperience(object? sender, EventArgs e)
    {
        foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
        {
            if (form is MainForm)
                MainDashboardExperience.Apply(form);
        }
    }
}

internal static class MainDashboardExperience
{
    private static readonly ConditionalWeakTable<Form, DashboardRegistration> Registrations = new();
    private static readonly ConditionalWeakTable<Control, ControlRegistration> ControlRegistrations = new();

    internal static void Apply(Form form)
    {
        if (form.IsDisposed || form.Disposing) return;

        var registration = Registrations.GetValue(form, _ => new DashboardRegistration());
        if (!registration.Initialized)
        {
            registration.Initialized = true;
            registration.ToolTip = CreateToolTip();
            form.AccessibleName ??= "GPTDeskTop operations dashboard";
            AppendAccessibleHint(form, "Primary operator workspace for ChatGPT conversations, saved monitors, runtime state, live activity and stored history.");
            form.FormClosed += (_, _) =>
            {
                registration.ToolTip?.Dispose();
                registration.ToolTip = null;
            };
            form.Resize += (_, _) => ApplyResponsiveLayout(form);
        }

        EnhanceDashboard(form, registration);
        ApplyResponsiveLayout(form);
    }

    private static ToolTip CreateToolTip()
        => new()
        {
            AutoPopDelay = 9000,
            InitialDelay = 350,
            ReshowDelay = 100,
            ShowAlways = true
        };

    private static void EnhanceDashboard(Form form, DashboardRegistration registration)
    {
        var controls = Descendants(form).ToList();

        EnhanceRootLayout(controls);
        EnhanceHeader(controls);
        EnhanceMetrics(controls, registration);
        EnhanceToolbar(controls, registration);
        EnhanceSections(controls);
        EnhanceSelectedMonitor(controls, registration);
        EnhanceGrids(controls, registration);
        EnhanceActivity(controls, registration);
        EnhanceEmptyStates(controls);
        EnhanceFooter(controls);
    }

    private static void EnhanceRootLayout(IReadOnlyCollection<Control> controls)
    {
        var root = controls.OfType<TableLayoutPanel>()
            .FirstOrDefault(table => table.Dock == DockStyle.Fill && table.RowCount == 5 && table.ColumnCount == 1);
        if (root is null) return;

        root.BackColor = FluentTheme.Background;
        root.Padding = new Padding(18, 16, 18, 14);
        root.AccessibleName ??= "Operations dashboard layout";
        root.AccessibleDescription ??= "Header, command bar, live workspace, diagnostics and build identity.";
    }

    private static void EnhanceHeader(IReadOnlyCollection<Control> controls)
    {
        var title = controls.OfType<Label>().FirstOrDefault(label => Normalize(label.Text) == "GPTDeskTop");
        if (title is null) return;

        title.ForeColor = FluentTheme.Text;
        title.AccessibleRole = AccessibleRole.TitleBar;
        title.AccessibleName = "GPTDeskTop";
        title.AccessibleDescription = "Operations console home.";

        var subtitle = controls.OfType<Label>()
            .FirstOrDefault(label => label.Text.Contains("monitoring, recovery and conversation automation", StringComparison.OrdinalIgnoreCase));
        if (subtitle is not null)
        {
            subtitle.ForeColor = FluentTheme.MutedStrong;
            subtitle.AccessibleDescription = "ChatGPT monitoring, recovery and conversation automation workspace.";
        }

        var header = FindAncestor<Panel>(title, panel => panel.Dock == DockStyle.Fill);
        if (header is null) return;

        header.BackColor = FluentTheme.SurfaceRaised;
        header.Padding = new Padding(20, 11, 14, 11);
        header.Margin = new Padding(0, 0, 0, 10);
        RoundPanel(header, 12);
    }

    private static void EnhanceMetrics(IReadOnlyCollection<Control> controls, DashboardRegistration registration)
    {
        var captions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Running"] = "Currently running monitors",
            ["Monitors"] = "Saved monitors",
            ["Conversation tabs"] = "Open ChatGPT conversation tabs",
            ["Chrome window"] = "Dedicated monitor Chrome window"
        };

        foreach (var caption in controls.OfType<Label>().Where(label => captions.ContainsKey(Normalize(label.Text))))
        {
            var normalized = Normalize(caption.Text);
            caption.ForeColor = FluentTheme.MutedStrong;
            caption.AccessibleName = captions[normalized];

            var chip = FindAncestor<Panel>(caption, panel => panel.Controls.OfType<Label>().Count() >= 2);
            if (chip is null) continue;

            chip.BackColor = FluentTheme.SurfaceAlt;
            chip.Padding = new Padding(12, 6, 12, 6);
            chip.Margin = new Padding(5, 2, 0, 2);
            chip.MinimumSize = new Size(Math.Max(chip.MinimumSize.Width, 112), Math.Max(chip.MinimumSize.Height, 48));
            RoundPanel(chip, 10);

            var value = chip.Controls.OfType<Label>().FirstOrDefault(label => !ReferenceEquals(label, caption));
            if (value is null) continue;

            value.AccessibleName = captions[normalized] + " value";
            UpdateMetricPresentation(normalized, value);
            HookTextChanged(value, () => UpdateMetricPresentation(normalized, value));

            if (registration.ToolTip is not null)
                registration.ToolTip.SetToolTip(chip, captions[normalized]);
        }
    }

    private static void UpdateMetricPresentation(string metric, Label value)
    {
        var text = Normalize(value.Text);
        value.BackColor = Color.Transparent;

        if (metric.Equals("Chrome window", StringComparison.OrdinalIgnoreCase))
        {
            value.ForeColor = text.Contains("visible", StringComparison.OrdinalIgnoreCase)
                ? FluentTheme.Success
                : text.Contains("hidden", StringComparison.OrdinalIgnoreCase)
                    ? FluentTheme.Warning
                    : FluentTheme.MutedStrong;
            return;
        }

        if (int.TryParse(text, out var count))
        {
            value.ForeColor = count > 0
                ? metric.Equals("Running", StringComparison.OrdinalIgnoreCase) ? FluentTheme.Success : FluentTheme.Info
                : FluentTheme.MutedStrong;
        }
    }

    private static void EnhanceToolbar(IReadOnlyCollection<Control> controls, DashboardRegistration registration)
    {
        var actionHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BROWSER", "MONITOR", "RUNTIME", "APP" };
        foreach (var header in controls.OfType<Label>().Where(label => actionHeaders.Contains(Normalize(label.Text))))
        {
            header.ForeColor = FluentTheme.MutedStrong;
            header.AccessibleDescription = $"{Normalize(header.Text).ToLowerInvariant()} command group.";
            var group = FindAncestor<TableLayoutPanel>(header, table => table.RowCount == 2 && table.ColumnCount == 1);
            if (group is null) continue;
            group.BackColor = FluentTheme.Surface;
            group.Padding = new Padding(8, 5, 8, 5);
            group.Margin = new Padding(0, 0, 10, 4);
            RoundPanelLike(group, 10);
        }

        var shortcuts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Launch Chrome"] = "Open the dedicated monitor Chrome workspace.",
            ["Hide Chrome"] = "Hide Chrome without stopping monitoring.",
            ["Show Chrome"] = "Show the dedicated monitor Chrome window.",
            ["Refresh"] = "Refresh open ChatGPT conversations. F5",
            ["Add Monitor"] = "Create monitor(s) from selected conversation rows. Ctrl+N",
            ["Edit Monitor"] = "Edit the selected saved monitor. Ctrl+E",
            ["Delete"] = "Delete the selected saved monitor. Delete",
            ["Start Selected"] = "Start the selected monitor.",
            ["Stop Selected"] = "Stop the selected monitor.",
            ["Start All"] = "Start all enabled saved monitors.",
            ["Stop All"] = "Stop all running monitors.",
            ["Settings"] = "Open application settings. Ctrl+,"
        };

        foreach (var button in controls.OfType<Button>())
        {
            var text = Normalize(button.Text);
            if (!shortcuts.TryGetValue(text, out var description)) continue;

            button.AccessibleName = text;
            AppendAccessibleHint(button, description);
            button.MinimumSize = new Size(Math.Max(button.MinimumSize.Width, text.Length >= 12 ? 104 : 78), Math.Max(button.MinimumSize.Height, 38));
            button.Margin = new Padding(4, 3, 4, 3);

            if (text is "Start All" or "Launch Chrome")
                FluentTheme.StyleButton(button, primary: true);
            else if (text == "Delete")
                FluentTheme.StyleButton(button, danger: true);

            if (registration.ToolTip is not null)
                registration.ToolTip.SetToolTip(button, description);
        }
    }

    private static void EnhanceSections(IReadOnlyCollection<Control> controls)
    {
        var sectionTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Open ChatGPT Conversations",
            "Saved Monitors",
            "Selected Monitor",
            "Live Activity",
            "Stored History"
        };

        foreach (var title in controls.OfType<Label>().Where(label => sectionTitles.Contains(Normalize(label.Text))))
        {
            title.ForeColor = FluentTheme.Text;
            title.AccessibleRole = AccessibleRole.StaticText;
            title.AccessibleName = Normalize(title.Text) + " section";

            var card = FindAncestor<Panel>(title, panel => panel.Dock == DockStyle.Fill || panel.BorderStyle == BorderStyle.None);
            if (card is null) continue;
            card.BackColor = FluentTheme.Surface;
            card.Padding = EnsurePadding(card.Padding, 13, 9);
            RoundPanel(card, 10);
        }

        foreach (var subtitle in controls.OfType<Label>().Where(label =>
                     label.Text.Contains("stable ChatGPT conversation URLs", StringComparison.OrdinalIgnoreCase)
                     || label.Text.Contains("Double-click a monitor", StringComparison.OrdinalIgnoreCase)
                     || label.Text.Contains("Real-time monitor and recovery events", StringComparison.OrdinalIgnoreCase)
                     || label.Text.Contains("Persisted inbound, outbound", StringComparison.OrdinalIgnoreCase)))
        {
            subtitle.ForeColor = FluentTheme.Muted;
            subtitle.AutoEllipsis = true;
        }
    }

    private static void EnhanceSelectedMonitor(IReadOnlyCollection<Control> controls, DashboardRegistration registration)
    {
        var heading = controls.OfType<Label>().FirstOrDefault(label => Normalize(label.Text) == "Selected Monitor");
        if (heading is null) return;

        var editor = FindAncestor<TableLayoutPanel>(heading, table => table.RowCount == 3 && table.ColumnCount == 4);
        if (editor is not null)
        {
            editor.BackColor = FluentTheme.SurfaceRaised;
            editor.Padding = new Padding(15, 9, 15, 10);
            editor.AccessibleName = "Selected monitor summary";
            editor.AccessibleDescription = "Read-only selected-monitor configuration summary with a quick edit action.";
        }

        var autoReply = controls.OfType<TextBox>().FirstOrDefault(box => box.ReadOnly && Normalize(box.Text).Length > 0 && FindSiblingLabel(box, "Auto reply"));
        if (autoReply is not null)
        {
            autoReply.BackColor = FluentTheme.SurfaceAlt;
            autoReply.ForeColor = FluentTheme.Text;
            autoReply.BorderStyle = BorderStyle.FixedSingle;
            autoReply.AccessibleName = "Selected monitor auto reply";
            autoReply.AccessibleDescription = "Read-only. Use Edit Selected Monitor to change this message.";
        }

        var enabled = controls.OfType<CheckBox>().FirstOrDefault(check => Normalize(check.Text) == "Enabled" && !check.Enabled);
        if (enabled is not null)
        {
            enabled.AccessibleName = "Selected monitor enabled state";
            enabled.AccessibleDescription = "Read-only selected monitor enabled state.";
        }

        var edit = controls.OfType<Button>().FirstOrDefault(button => Normalize(button.Text) == "Edit Selected Monitor");
        if (edit is not null)
        {
            FluentTheme.StyleButton(edit, primary: true);
            edit.MinimumSize = new Size(Math.Max(edit.MinimumSize.Width, 170), 38);
            edit.AccessibleDescription = "Open settings for the currently selected monitor. Ctrl+E";
            registration.ToolTip?.SetToolTip(edit, "Edit the selected monitor configuration. Ctrl+E");
        }

        var summary = controls.OfType<Label>().FirstOrDefault(label => label.Text.StartsWith("Select a monitor", StringComparison.OrdinalIgnoreCase));
        if (summary is not null)
        {
            summary.BackColor = FluentTheme.AccentSubtle;
            summary.ForeColor = FluentTheme.MutedStrong;
            summary.Padding = new Padding(10, 5, 10, 5);
            summary.AccessibleName = "Selected monitor summary text";
        }
    }

    private static void EnhanceGrids(IReadOnlyCollection<Control> controls, DashboardRegistration registration)
    {
        foreach (var grid in controls.OfType<DataGridView>())
        {
            grid.BackgroundColor = FluentTheme.Surface;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.GridColor = FluentTheme.Border;
            grid.RowTemplate.Height = Math.Max(grid.RowTemplate.Height, 36);
            grid.ColumnHeadersHeight = Math.Max(grid.ColumnHeadersHeight, 38);
            grid.EnableHeadersVisualStyles = false;
            grid.ShowCellToolTips = true;
            grid.AccessibleRole = AccessibleRole.Table;

            var purpose = DetectGridPurpose(grid);
            grid.AccessibleName = purpose.Name;
            grid.AccessibleDescription = purpose.Description;

            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.HeaderCell.Style.WrapMode = DataGridViewTriState.False;
                column.HeaderCell.ToolTipText = column.HeaderText;
                if (column.HeaderText is "ID" or "On" or "Runtime" or "Flow" or "Delay" or "Poll" or "Monitor")
                    column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }

            if (registration.ToolTip is not null)
                registration.ToolTip.SetToolTip(grid, purpose.Description);
        }
    }

    private static (string Name, string Description) DetectGridPurpose(DataGridView grid)
    {
        var headers = grid.Columns.Cast<DataGridViewColumn>().Select(column => column.HeaderText).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (headers.Contains("Tab ID"))
            return ("Open ChatGPT conversations", "Open stable ChatGPT conversation tabs. Select one or more rows and use Add Monitor. Ctrl+N adds the selection.");
        if (headers.Contains("Runtime") && headers.Contains("Auto reply"))
            return ("Saved monitors", "Saved monitor list with live runtime status. Double-click a row or press Ctrl+E to edit it.");
        return ("Stored activity history", "Persisted monitor activity and delivery history. Status colors highlight success, warnings and failures.");
    }

    private static void EnhanceActivity(IReadOnlyCollection<Control> controls, DashboardRegistration registration)
    {
        var activity = controls.OfType<RichTextBox>().FirstOrDefault(box => box.ReadOnly && box.Font.Name.Contains("Cascadia", StringComparison.OrdinalIgnoreCase));
        if (activity is null) return;

        activity.BackColor = Color.FromArgb(17, 24, 39);
        activity.ForeColor = Color.FromArgb(226, 232, 240);
        activity.DetectUrls = true;
        activity.WordWrap = false;
        activity.ScrollBars = RichTextBoxScrollBars.Both;
        activity.AccessibleName = "Live monitor activity";
        activity.AccessibleDescription = "Real-time monitor, recovery and automation events. New entries are appended as operations occur.";
        registration.ToolTip?.SetToolTip(activity, "Live monitor and recovery event stream.");
    }

    private static void EnhanceEmptyStates(IReadOnlyCollection<Control> controls)
    {
        foreach (var label in controls.OfType<Label>().Where(IsDashboardEmptyState))
        {
            label.BackColor = FluentTheme.SurfaceAlt;
            label.ForeColor = FluentTheme.MutedStrong;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.Padding = new Padding(26, 18, 26, 18);
            label.AccessibleRole = AccessibleRole.StaticText;
            label.AccessibleName = "Empty workspace guidance";
            label.AccessibleDescription ??= "Actionable guidance is shown because this dashboard area has no rows yet.";
        }
    }

    private static bool IsDashboardEmptyState(Label label)
        => label.Text.StartsWith("No ChatGPT conversations", StringComparison.OrdinalIgnoreCase)
           || label.Text.StartsWith("No saved monitors", StringComparison.OrdinalIgnoreCase)
           || label.Text.StartsWith("No stored history", StringComparison.OrdinalIgnoreCase);

    private static void EnhanceFooter(IReadOnlyCollection<Control> controls)
    {
        var version = controls.OfType<Label>().FirstOrDefault(label => label.Text.StartsWith("GPTDeskTop v", StringComparison.OrdinalIgnoreCase));
        if (version is null) return;
        version.ForeColor = FluentTheme.MutedStrong;
        version.Padding = new Padding(0, 3, 2, 0);
        version.AccessibleName = "Application build identity";
        version.AccessibleDescription = version.Text;
    }

    private static void ApplyResponsiveLayout(Form form)
    {
        if (form.IsDisposed || form.Disposing) return;
        var compact = form.ClientSize.Width < 1180;
        var narrow = form.ClientSize.Width < 1020;

        foreach (var flow in Descendants(form).OfType<FlowLayoutPanel>())
        {
            var buttons = flow.Controls.OfType<Button>().ToList();
            if (buttons.Count > 1)
            {
                flow.WrapContents = compact || buttons.Count >= 4;
                flow.AutoScroll = narrow && flow.Dock == DockStyle.Fill;
            }
        }

        foreach (var group in Descendants(form).OfType<TableLayoutPanel>())
        {
            if (!group.Controls.OfType<Label>().Any(label => new[] { "BROWSER", "MONITOR", "RUNTIME", "APP" }.Contains(Normalize(label.Text), StringComparer.OrdinalIgnoreCase)))
                continue;
            group.Margin = compact ? new Padding(0, 0, 7, 4) : new Padding(0, 0, 10, 4);
        }
    }

    private static void HookTextChanged(Control control, Action handler)
    {
        var registration = ControlRegistrations.GetValue(control, _ => new ControlRegistration());
        if (registration.TextChangedHooked) return;
        registration.TextChangedHooked = true;
        control.TextChanged += (_, _) => handler();
    }

    private static void RoundPanel(Panel panel, int radius)
    {
        var registration = ControlRegistrations.GetValue(panel, _ => new ControlRegistration());
        ApplyRoundedRegion(panel, radius);
        if (registration.ResizeHooked) return;
        registration.ResizeHooked = true;
        panel.Resize += (_, _) => ApplyRoundedRegion(panel, radius);
    }

    private static void RoundPanelLike(Control control, int radius)
    {
        var registration = ControlRegistrations.GetValue(control, _ => new ControlRegistration());
        ApplyRoundedRegion(control, radius);
        if (registration.ResizeHooked) return;
        registration.ResizeHooked = true;
        control.Resize += (_, _) => ApplyRoundedRegion(control, radius);
    }

    private static void ApplyRoundedRegion(Control control, int radius)
    {
        if (control.Width <= 0 || control.Height <= 0) return;
        var diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(control.Width, control.Height)));
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        var bounds = new Rectangle(0, 0, Math.Max(1, control.Width - 1), Math.Max(1, control.Height - 1));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        var previous = control.Region;
        control.Region = new Region(path);
        previous?.Dispose();
    }

    private static bool FindSiblingLabel(Control control, string text)
        => control.Parent?.Controls.OfType<Label>().Any(label => Normalize(label.Text) == text) == true;

    private static T? FindAncestor<T>(Control control, Func<T, bool> predicate) where T : Control
    {
        for (Control? current = control.Parent; current is not null; current = current.Parent)
        {
            if (current is T typed && predicate(typed)) return typed;
        }
        return null;
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

    private static Padding EnsurePadding(Padding padding, int horizontal, int vertical)
        => new(
            Math.Max(padding.Left, horizontal),
            Math.Max(padding.Top, vertical),
            Math.Max(padding.Right, horizontal),
            Math.Max(padding.Bottom, vertical));

    private static string Normalize(string? value)
        => (value ?? string.Empty).Replace("&", string.Empty).Trim();

    private static void AppendAccessibleHint(Control control, string hint)
    {
        if (string.IsNullOrWhiteSpace(hint)) return;
        if (string.IsNullOrWhiteSpace(control.AccessibleDescription))
        {
            control.AccessibleDescription = hint;
            return;
        }
        if (!control.AccessibleDescription.Contains(hint, StringComparison.OrdinalIgnoreCase))
            control.AccessibleDescription = $"{control.AccessibleDescription.Trim()} {hint}";
    }

    private sealed class DashboardRegistration
    {
        public bool Initialized { get; set; }
        public ToolTip? ToolTip { get; set; }
    }

    private sealed class ControlRegistration
    {
        public bool TextChangedHooked { get; set; }
        public bool ResizeHooked { get; set; }
    }
}