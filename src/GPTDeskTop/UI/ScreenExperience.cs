using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;

namespace GPTDeskTop.UI;

internal static class ScreenExperienceBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
        => Application.Idle += OnApplicationIdle;

    private static void OnApplicationIdle(object? sender, EventArgs e)
        => ScreenExperience.ApplyOpenForms();
}

internal static class ScreenExperience
{
    private static readonly ConditionalWeakTable<Form, FormRegistration> FormRegistrations = new();
    private static readonly ConditionalWeakTable<Control, ControlRegistration> ControlRegistrations = new();
    private static readonly Font StatusFont = new("Segoe UI Variable Text", 9F, FontStyle.Bold, GraphicsUnit.Point);
    private static readonly Font EmptyStateFont = new("Segoe UI Variable Text", 10F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font UrlFont = new("Cascadia Mono", 8.25F, FontStyle.Regular, GraphicsUnit.Point);

    internal static void ApplyOpenForms()
    {
        if (Application.OpenForms.Count == 0) return;
        foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
            Apply(form);
    }

    internal static void Apply(Form form)
    {
        if (form.IsDisposed || form.Disposing) return;

        var registration = FormRegistrations.GetValue(form, _ => new FormRegistration());
        if (!registration.Initialized)
        {
            registration.Initialized = true;
            registration.ToolTip = CreateToolTip();
            form.KeyPreview = true;
            form.KeyDown += (_, e) => HandleFormShortcut(form, e);
            form.Resize += (_, _) => ApplyResponsiveLayout(form);
            form.FormClosed += (_, _) =>
            {
                registration.ToolTip?.Dispose();
                registration.ToolTip = null;
            };

            AppendAccessibleHint(
                form,
                "Keyboard: Ctrl+F search, Ctrl+Shift+F clear search, F5 refresh, Ctrl+S save, Ctrl+E export, Ctrl+Tab switch tabs, F6 move focus between major regions.");

            EnhanceTree(form, form, registration);
        }

        ApplyResponsiveLayout(form);
    }

    private static ToolTip CreateToolTip()
        => new()
        {
            AutoPopDelay = 9000,
            InitialDelay = 450,
            ReshowDelay = 100,
            ShowAlways = true
        };

    private static void EnhanceTree(Form form, Control control, FormRegistration formRegistration)
    {
        if (control.IsDisposed) return;
        RegisterDynamicChildren(form, control, formRegistration);
        EnhanceControl(form, control, formRegistration);

        foreach (Control child in control.Controls)
            EnhanceTree(form, child, formRegistration);
    }

    private static void RegisterDynamicChildren(Form form, Control control, FormRegistration formRegistration)
    {
        var registration = ControlRegistrations.GetValue(control, _ => new ControlRegistration());
        if (registration.ControlAddedHooked) return;

        registration.ControlAddedHooked = true;
        control.ControlAdded += (_, e) =>
        {
            if (form.IsDisposed || form.Disposing || e.Control.IsDisposed) return;
            EnhanceTree(form, e.Control, formRegistration);
            ApplyResponsiveLayout(form);
        };
    }

    private static void EnhanceControl(Form form, Control control, FormRegistration formRegistration)
    {
        switch (control)
        {
            case TabControl tabs:
                EnhanceTabs(tabs);
                break;
            case TabPage page:
                EnhanceTabPage(page);
                break;
            case TextBox box:
                EnhanceTextBox(box, formRegistration);
                break;
            case ComboBox combo:
                EnhanceComboBox(combo);
                break;
            case Button button:
                EnhanceButton(form, button, formRegistration);
                break;
            case Label label:
                EnhanceLabel(label, formRegistration);
                break;
            case DataGridView grid:
                EnhanceGrid(grid);
                break;
            case RichTextBox rich:
                EnhanceRichTextBox(rich);
                break;
            case FlowLayoutPanel flow:
                EnhanceActionFlow(flow);
                break;
            case SplitContainer split:
                EnhanceSplitContainer(split);
                break;
        }
    }

    private static void EnhanceTabs(TabControl tabs)
    {
        tabs.HotTrack = true;
        tabs.TabStop = true;
        tabs.Multiline = false;
        tabs.AccessibleName ??= "Workspace sections";
        AppendAccessibleHint(tabs, "Use Ctrl+Tab or Ctrl+PageDown to move forward and Ctrl+Shift+Tab or Ctrl+PageUp to move backward.");

        foreach (TabPage page in tabs.TabPages)
        {
            page.AccessibleName ??= string.IsNullOrWhiteSpace(page.Text) ? "Settings section" : page.Text.Replace("&", string.Empty).Trim();
            page.AccessibleDescription ??= $"{page.AccessibleName} content.";
        }
    }

    private static void EnhanceTabPage(TabPage page)
    {
        page.AutoScroll = true;
        page.AutoScrollMargin = new Size(8, 8);
        page.AccessibleName ??= string.IsNullOrWhiteSpace(page.Text) ? "Workspace section" : page.Text.Replace("&", string.Empty).Trim();
    }

    private static void EnhanceTextBox(TextBox box, FormRegistration registration)
    {
        if (IsSearchBox(box))
        {
            if (box.Dock == DockStyle.None) box.Width = Math.Max(box.Width, 280);
            box.AccessibleName ??= "Search";
            AppendAccessibleHint(box, "Ctrl+F focuses this search field. Ctrl+Shift+F clears the current search.");
            if (registration.ToolTip is not null)
                registration.ToolTip.SetToolTip(box, "Search this workspace (Ctrl+F). Clear search with Ctrl+Shift+F.");
        }

        if (LooksLikeUrl(box.Text) || ContainsIgnoreCase(box.AccessibleName, "url"))
        {
            box.Font = UrlFont;
            box.AccessibleDescription ??= "Conversation URL.";
        }
    }

    private static void EnhanceComboBox(ComboBox combo)
    {
        if (!ContainsIgnoreCase(combo.AccessibleName, "filter")) return;
        if (combo.Dock == DockStyle.None) combo.Width = Math.Max(combo.Width, 140);
        combo.MaxDropDownItems = Math.Max(combo.MaxDropDownItems, 12);
        combo.DropDownWidth = Math.Max(combo.DropDownWidth, combo.Width);
        AppendAccessibleHint(combo, "Use arrow keys to change this filter and Escape to close the list.");
    }

    private static void EnhanceButton(Form form, Button button, FormRegistration registration)
    {
        var text = Normalize(button.Text);
        button.AutoEllipsis = true;
        button.MinimumSize = new Size(button.MinimumSize.Width, Math.Max(button.MinimumSize.Height, 36));

        if (ReferenceEquals(form.AcceptButton, button))
            FluentTheme.StyleButton(button, primary: true);
        else if (text.Contains("delete", StringComparison.OrdinalIgnoreCase) || text.Contains("remove", StringComparison.OrdinalIgnoreCase))
            FluentTheme.StyleButton(button, danger: true);
        else if (IsStrongPrimaryAction(text))
            FluentTheme.StyleButton(button, primary: true);

        var shortcut = GetButtonShortcut(text, form, button);
        if (!string.IsNullOrWhiteSpace(shortcut))
        {
            AppendAccessibleHint(button, $"Keyboard shortcut: {shortcut}.");
            if (registration.ToolTip is not null && !IsInsideOwnTooltipControl(button))
            {
                var description = string.IsNullOrWhiteSpace(button.AccessibleDescription)
                    ? Normalize(button.Text)
                    : button.AccessibleDescription!.Trim();
                registration.ToolTip.SetToolTip(button, $"{description}  [{shortcut}]");
            }
        }
        else if (registration.ToolTip is not null && !IsInsideOwnTooltipControl(button) && !string.IsNullOrWhiteSpace(button.AccessibleDescription))
        {
            registration.ToolTip.SetToolTip(button, button.AccessibleDescription);
        }
    }

    private static bool IsStrongPrimaryAction(string text)
        => text.Contains("create support bundle", StringComparison.OrdinalIgnoreCase)
           || text.Contains("export visible", StringComparison.OrdinalIgnoreCase)
           || text.Contains("launch chrome", StringComparison.OrdinalIgnoreCase)
           || text.Contains("start all", StringComparison.OrdinalIgnoreCase);

    private static string? GetButtonShortcut(string text, Form form, Button button)
    {
        if (ReferenceEquals(form.AcceptButton, button) || text.StartsWith("save", StringComparison.OrdinalIgnoreCase)) return "Ctrl+S";
        if (text.Contains("refresh", StringComparison.OrdinalIgnoreCase)) return "F5";
        if (text.Contains("export", StringComparison.OrdinalIgnoreCase)) return "Ctrl+E";
        if (text.Contains("create support bundle", StringComparison.OrdinalIgnoreCase)) return "Ctrl+Shift+B";
        return null;
    }

    private static void EnhanceLabel(Label label, FormRegistration registration)
    {
        label.AutoEllipsis = label.AutoEllipsis || label.Dock == DockStyle.Fill;

        if (IsStatusLabel(label))
        {
            label.Font = StatusFont;
            label.Padding = EnsurePadding(label.Padding, 8, 3);
            label.AccessibleRole = AccessibleRole.StatusBar;
            UpdateSemanticStatus(label);

            var controlRegistration = ControlRegistrations.GetValue(label, _ => new ControlRegistration());
            if (!controlRegistration.TextChangedHooked)
            {
                controlRegistration.TextChangedHooked = true;
                label.TextChanged += (_, _) =>
                {
                    UpdateSemanticStatus(label);
                    if (registration.ToolTip is not null) registration.ToolTip.SetToolTip(label, label.Text);
                };
            }
        }
        else if (IsEmptyStateLabel(label))
        {
            label.Font = EmptyStateFont;
            label.ForeColor = FluentTheme.MutedStrong;
            label.BackColor = FluentTheme.SurfaceAlt;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.Padding = EnsurePadding(label.Padding, 28, 20);
            label.AccessibleDescription ??= "This area currently has no matching items.";
        }
        else if (IsHeaderLabel(label))
        {
            label.ForeColor = FluentTheme.Text;
            label.Padding = EnsurePadding(label.Padding, 0, 2);
            label.AccessibleDescription ??= label.Text;
        }
        else if (LooksLikeUrl(label.Text))
        {
            label.Font = UrlFont;
            label.ForeColor = FluentTheme.Info;
            label.AccessibleDescription ??= label.Text;
        }
        else if (label.Text.Contains("Sensitive data notice", StringComparison.OrdinalIgnoreCase))
        {
            label.BackColor = FluentTheme.WarningSubtle;
            label.ForeColor = FluentTheme.Warning;
            label.Padding = EnsurePadding(label.Padding, 10, 8);
        }

        if (registration.ToolTip is not null && label.AutoEllipsis && label.Text.Length > 36)
            registration.ToolTip.SetToolTip(label, label.Text);
    }

    private static bool IsStatusLabel(Label label)
        => label.AccessibleRole == AccessibleRole.StatusBar
           || ContainsIgnoreCase(label.AccessibleName, "status")
           || ContainsIgnoreCase(label.AccessibleName, "health")
           || ContainsIgnoreCase(label.AccessibleName, "result summary");

    private static bool IsEmptyStateLabel(Label label)
    {
        var text = label.Text.TrimStart();
        return text.StartsWith("No ", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("Select a ", StringComparison.OrdinalIgnoreCase)
               || text.Contains("will appear here", StringComparison.OrdinalIgnoreCase)
               || text.Contains("nothing to show", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHeaderLabel(Label label)
        => label.Font.Size >= 14F
           || label.Text.Equals("Stored History Explorer", StringComparison.OrdinalIgnoreCase)
           || label.Text.Equals("Runtime Health", StringComparison.OrdinalIgnoreCase)
           || label.Text.Equals("Support Diagnostics", StringComparison.OrdinalIgnoreCase)
           || label.Text.Equals("Selected Monitor", StringComparison.OrdinalIgnoreCase);

    private static void UpdateSemanticStatus(Label label)
    {
        var text = Normalize(label.Text);
        var (foreground, background) = ClassifyStatus(text);
        label.ForeColor = foreground;
        label.BackColor = background;
        label.AutoEllipsis = true;
    }

    private static (Color Foreground, Color Background) ClassifyStatus(string text)
    {
        if (ContainsAny(text, "error", "failed", "failure", "blocked", "invalid", "unhealthy", "crash"))
            return (FluentTheme.Danger, FluentTheme.DangerSubtle);
        if (ContainsAny(text, "running", "healthy", "ready", "connected", "success", "created", "clear", "verified"))
            return (FluentTheme.Success, FluentTheme.SuccessSubtle);
        if (ContainsAny(text, "checking", "loading", "creating", "refreshing", "working", "starting"))
            return (FluentTheme.Info, FluentTheme.InfoSubtle);
        if (ContainsAny(text, "warning", "pending", "stopped", "not checked", "unknown", "deferred", "retry"))
            return (FluentTheme.Warning, FluentTheme.WarningSubtle);
        return (FluentTheme.MutedStrong, FluentTheme.SurfaceAlt);
    }

    private static void EnhanceGrid(DataGridView grid)
    {
        grid.ShowCellToolTips = true;
        grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText;
        grid.AllowUserToOrderColumns = true;
        grid.AllowUserToResizeColumns = true;
        grid.AllowUserToResizeRows = false;
        grid.AccessibleDescription ??= "Use arrow keys to move between rows. Ctrl+Home and Ctrl+End jump to the first and last row. Ctrl+C copies the current selection with column headers.";

        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.HeaderCell.Style.WrapMode = DataGridViewTriState.False;
            if (column.Width <= 90 || IsCompactColumn(column.HeaderText))
            {
                column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
        }

        var registration = ControlRegistrations.GetValue(grid, _ => new ControlRegistration());
        if (registration.KeyHooked) return;
        registration.KeyHooked = true;
        grid.KeyDown += (_, e) =>
        {
            if (!e.Control || grid.Rows.Count == 0) return;
            if (e.KeyCode == Keys.Home)
            {
                SelectGridRow(grid, 0);
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.End)
            {
                SelectGridRow(grid, grid.Rows.Count - 1);
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
        };
    }

    private static bool IsCompactColumn(string? header)
        => ContainsAny(header ?? string.Empty, "id", "on", "flow", "delay", "poll", "monitor", "runtime");

    private static void SelectGridRow(DataGridView grid, int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= grid.Rows.Count) return;
        var row = grid.Rows[rowIndex];
        var cell = row.Cells.Cast<DataGridViewCell>().FirstOrDefault(candidate => candidate.Visible);
        if (cell is null) return;
        grid.ClearSelection();
        row.Selected = true;
        grid.CurrentCell = cell;
        if (rowIndex >= 0 && rowIndex < grid.RowCount)
            grid.FirstDisplayedScrollingRowIndex = rowIndex;
    }

    private static void EnhanceRichTextBox(RichTextBox rich)
    {
        if (!rich.ReadOnly) return;
        rich.DetectUrls = true;
        if (rich.Font.Name.Contains("Cascadia", StringComparison.OrdinalIgnoreCase)
            || ContainsIgnoreCase(rich.AccessibleName, "activity")
            || ContainsIgnoreCase(rich.AccessibleName, "log"))
        {
            rich.WordWrap = false;
            rich.ScrollBars = RichTextBoxScrollBars.Both;
        }
    }

    private static void EnhanceActionFlow(FlowLayoutPanel flow)
    {
        var buttons = flow.Controls.OfType<Button>().ToList();
        if (buttons.Count < 2) return;
        flow.WrapContents = true;
        flow.Padding = EnsurePadding(flow.Padding, 0, 3);
        foreach (var button in buttons)
            button.Margin = new Padding(4, 3, 4, 3);
    }

    private static void EnhanceSplitContainer(SplitContainer split)
    {
        split.SplitterWidth = Math.Max(split.SplitterWidth, 6);
        split.IsSplitterFixed = false;
        split.TabStop = false;
    }

    private static void HandleFormShortcut(Form form, KeyEventArgs e)
    {
        if (e.Control && e.Shift && e.KeyCode == Keys.F)
        {
            var search = FindSearchBox(form);
            if (search is not null)
            {
                search.Clear();
                search.Focus();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
            return;
        }

        if (e.Control && e.KeyCode == Keys.F)
        {
            var search = FindSearchBox(form);
            if (search is not null)
            {
                search.Focus();
                search.SelectAll();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
            return;
        }

        if (e.KeyCode == Keys.F5 && !e.Control && !e.Alt)
        {
            if (TryClickButton(form, "refresh"))
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
            return;
        }

        if (e.Control && e.KeyCode == Keys.S)
        {
            if (form.AcceptButton is Button accept && accept.Enabled && accept.Visible)
            {
                accept.PerformClick();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
            return;
        }

        if (e.Control && e.KeyCode == Keys.E)
        {
            if (TryClickButton(form, "export"))
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
            return;
        }

        if (e.Control && e.Shift && e.KeyCode == Keys.B)
        {
            if (TryClickButton(form, "create support bundle"))
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
            return;
        }

        if (e.KeyCode == Keys.F6)
        {
            form.SelectNextControl(form.ActiveControl, !e.Shift, true, true, true);
            e.SuppressKeyPress = true;
            e.Handled = true;
            return;
        }

        if (e.Control && (e.KeyCode == Keys.Tab || e.KeyCode == Keys.PageDown || e.KeyCode == Keys.PageUp))
        {
            var tabs = Descendants(form).OfType<TabControl>().FirstOrDefault(control => control.Visible && control.Enabled && control.TabCount > 1);
            if (tabs is null) return;
            var backwards = e.Shift || e.KeyCode == Keys.PageUp;
            tabs.SelectedIndex = (tabs.SelectedIndex + (backwards ? -1 : 1) + tabs.TabCount) % tabs.TabCount;
            tabs.Focus();
            e.SuppressKeyPress = true;
            e.Handled = true;
            return;
        }

        if (e.Alt && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9)
        {
            var tabs = Descendants(form).OfType<TabControl>().FirstOrDefault(control => control.Visible && control.Enabled);
            if (tabs is null) return;
            var index = (int)e.KeyCode - (int)Keys.D1;
            if (index >= tabs.TabCount) return;
            tabs.SelectedIndex = index;
            tabs.Focus();
            e.SuppressKeyPress = true;
            e.Handled = true;
        }
    }

    private static bool TryClickButton(Form form, string textFragment)
    {
        var button = Descendants(form)
            .OfType<Button>()
            .FirstOrDefault(candidate => candidate.Visible && candidate.Enabled && Normalize(candidate.Text).Contains(textFragment, StringComparison.OrdinalIgnoreCase));
        if (button is null) return false;
        button.PerformClick();
        return true;
    }

    private static TextBox? FindSearchBox(Form form)
        => Descendants(form).OfType<TextBox>().FirstOrDefault(box => box.Visible && box.Enabled && IsSearchBox(box));

    private static bool IsSearchBox(TextBox box)
        => ContainsIgnoreCase(box.PlaceholderText, "search")
           || ContainsIgnoreCase(box.AccessibleName, "search")
           || ContainsIgnoreCase(box.AccessibleDescription, "search");

    private static void ApplyResponsiveLayout(Form form)
    {
        if (form.IsDisposed || form.Disposing) return;
        var compact = form.ClientSize.Width < 860;

        foreach (var tabs in Descendants(form).OfType<TabControl>())
            tabs.Padding = compact ? new Point(10, 6) : new Point(16, 7);

        foreach (var flow in Descendants(form).OfType<FlowLayoutPanel>())
        {
            if (!flow.Controls.OfType<Button>().Any()) continue;
            flow.WrapContents = compact || flow.Controls.OfType<Button>().Count() > 3;
        }

        foreach (var label in Descendants(form).OfType<Label>().Where(label => label.AutoEllipsis))
            label.MaximumSize = Size.Empty;
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

    private static bool IsInsideOwnTooltipControl(Control control)
    {
        for (Control? parent = control.Parent; parent is not null; parent = parent.Parent)
        {
            var name = parent.GetType().Name;
            if (name is "RuntimeHealthControl" or "SupportDiagnosticsControl") return true;
        }
        return false;
    }

    private static bool LooksLikeUrl(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase));

    private static bool ContainsIgnoreCase(string? value, string fragment)
        => !string.IsNullOrWhiteSpace(value) && value.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAny(string value, params string[] fragments)
        => fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));

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

    private static Padding EnsurePadding(Padding padding, int horizontal, int vertical)
        => new(
            Math.Max(padding.Left, horizontal),
            Math.Max(padding.Top, vertical),
            Math.Max(padding.Right, horizontal),
            Math.Max(padding.Bottom, vertical));

    private sealed class FormRegistration
    {
        public bool Initialized { get; set; }
        public ToolTip? ToolTip { get; set; }
    }

    private sealed class ControlRegistration
    {
        public bool ControlAddedHooked { get; set; }
        public bool TextChangedHooked { get; set; }
        public bool KeyHooked { get; set; }
    }
}