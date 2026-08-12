using System.Runtime.CompilerServices;

namespace GPTDeskTop.UI;

internal static class CompactDashboardHeaderLayoutBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
        => Application.Idle += (_, _) => CompactDashboardHeaderLayout.ApplyOpenForms();
}

/// <summary>
/// Final layout owner for the compact main-dashboard status header. The visual makeover is
/// deliberately applied first, then this layer removes fixed vertical chrome so Live Activity
/// keeps the reclaimed space without changing any metric binding or operator behavior.
/// </summary>
internal static class CompactDashboardHeaderLayout
{
    private const int HeaderLogicalHeight = 56;
    private const int RootVerticalPadding = 10;
    private const int HeaderHorizontalPadding = 14;
    private const int HeaderVerticalPadding = 5;
    private const int HeaderBottomMargin = 6;
    private const int MetricLogicalWidth = 108;
    private const int MetricLogicalHeight = 40;

    private const string HeaderGuidance = "ChatGPT monitoring, recovery and conversation automation";

    private static readonly ConditionalWeakTable<Form, Registration> Registrations = new();

    internal static void ApplyOpenForms()
    {
        foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
        {
            if (form is MainForm)
                Apply(form);
        }
    }

    internal static bool Apply(Form form)
    {
        if (form.IsDisposed || form.Disposing) return false;

        var registration = Registrations.GetValue(form, _ => new Registration());
        if (registration.Installed)
            return true;

        // MainDashboardExperience is the visual makeover owner. Calling it here makes ordering
        // deterministic even if module-initializer subscription order changes between builds.
        MainDashboardExperience.Apply(form);

        if (!TryResolveHeader(form, out var parts))
            return false;

        registration.Installed = true;
        registration.Parts = parts;
        registration.ToolTip = new ToolTip
        {
            AutoPopDelay = 9000,
            InitialDelay = 350,
            ReshowDelay = 100,
            ShowAlways = true
        };

        form.FormClosed += (_, _) =>
        {
            registration.ToolTip?.Dispose();
            registration.ToolTip = null;
        };
        form.DpiChanged += (_, _) => ApplyPhysicalLayout(registration);

        ApplySemantics(registration);
        ApplyPhysicalLayout(registration);
        return true;
    }

    private static bool TryResolveHeader(Form form, out HeaderParts parts)
    {
        parts = default!;

        var root = Descendants(form)
            .OfType<TableLayoutPanel>()
            .FirstOrDefault(table => table.Dock == DockStyle.Fill && table.RowCount == 5 && table.ColumnCount == 1);
        if (root is null || root.RowStyles.Count == 0)
            return false;

        var header = root.GetControlFromPosition(0, 0) as Panel;
        if (header is null)
            return false;

        var headerLayout = header.Controls
            .OfType<TableLayoutPanel>()
            .FirstOrDefault(table => table.ColumnCount == 2 && table.RowCount == 1);
        if (headerLayout is null)
            return false;

        var titleBlock = headerLayout.GetControlFromPosition(0, 0) as TableLayoutPanel;
        var metrics = headerLayout.GetControlFromPosition(1, 0) as FlowLayoutPanel;
        if (titleBlock is null || metrics is null || titleBlock.RowStyles.Count < 2)
            return false;

        var title = Descendants(titleBlock)
            .OfType<Label>()
            .FirstOrDefault(label => string.Equals(label.Text.Trim(), "GPTDeskTop", StringComparison.Ordinal));
        var subtitle = Descendants(titleBlock)
            .OfType<Label>()
            .FirstOrDefault(label => label.Text.Contains(HeaderGuidance, StringComparison.OrdinalIgnoreCase));
        if (title is null || subtitle is null)
            return false;

        var chips = metrics.Controls.OfType<Panel>().ToArray();
        if (chips.Length != 4)
            return false;

        parts = new HeaderParts(root, header, headerLayout, titleBlock, title, subtitle, metrics, chips);
        return true;
    }

    private static void ApplySemantics(Registration registration)
    {
        var parts = registration.Parts!;

        parts.Header.AccessibleName = "GPTDeskTop status strip";
        parts.Header.AccessibleDescription = HeaderGuidance + " with live runtime, monitor, conversation-tab and Chrome-window status.";
        parts.Title.AccessibleName = "GPTDeskTop";
        parts.Title.AccessibleDescription = HeaderGuidance + ".";
        parts.Subtitle.AccessibleName = "Dashboard purpose";
        parts.Subtitle.AccessibleDescription = HeaderGuidance + ".";
        registration.ToolTip?.SetToolTip(parts.Title, HeaderGuidance + ".");
        registration.ToolTip?.SetToolTip(parts.Header, HeaderGuidance + " with live status metrics.");
    }

    private static void ApplyPhysicalLayout(Registration registration)
    {
        var parts = registration.Parts;
        if (parts is null || parts.Root.IsDisposed || parts.Header.IsDisposed)
            return;

        var rootHorizontal = Scale(parts.Root, 18);
        var rootVertical = Scale(parts.Root, RootVerticalPadding);
        parts.Root.Padding = new Padding(rootHorizontal, rootVertical, rootHorizontal, rootVertical);
        parts.Root.RowStyles[0].SizeType = SizeType.Absolute;
        parts.Root.RowStyles[0].Height = Scale(parts.Root, HeaderLogicalHeight);

        parts.Header.Padding = new Padding(
            Scale(parts.Header, HeaderHorizontalPadding),
            Scale(parts.Header, HeaderVerticalPadding),
            Scale(parts.Header, 10),
            Scale(parts.Header, HeaderVerticalPadding));
        parts.Header.Margin = new Padding(0, 0, 0, Scale(parts.Header, HeaderBottomMargin));

        parts.HeaderLayout.Margin = Padding.Empty;
        parts.HeaderLayout.Padding = Padding.Empty;
        parts.TitleBlock.Margin = Padding.Empty;
        parts.TitleBlock.Padding = Padding.Empty;
        parts.TitleBlock.RowStyles[0].SizeType = SizeType.Percent;
        parts.TitleBlock.RowStyles[0].Height = 100;
        parts.TitleBlock.RowStyles[1].SizeType = SizeType.Absolute;
        parts.TitleBlock.RowStyles[1].Height = 0;

        // Keep the purpose text for accessibility/tooltips while removing its visual row.
        parts.Subtitle.Visible = false;
        parts.Subtitle.TabStop = false;
        parts.Title.TextAlign = ContentAlignment.MiddleLeft;
        parts.Title.Dock = DockStyle.Fill;

        parts.Metrics.Padding = Padding.Empty;
        parts.Metrics.Margin = Padding.Empty;
        parts.Metrics.WrapContents = false;

        foreach (var chip in parts.MetricChips)
        {
            chip.Width = Scale(chip, MetricLogicalWidth);
            chip.Height = Scale(chip, MetricLogicalHeight);
            chip.MinimumSize = new Size(Scale(chip, MetricLogicalWidth), Scale(chip, MetricLogicalHeight));
            chip.Padding = new Padding(Scale(chip, 8), Scale(chip, 3), Scale(chip, 8), Scale(chip, 2));
            chip.Margin = new Padding(Scale(chip, 4), 0, 0, 0);

            var chipLayout = chip.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
            if (chipLayout is null) continue;
            chipLayout.Margin = Padding.Empty;
            chipLayout.Padding = Padding.Empty;
            if (chipLayout.RowStyles.Count >= 2)
            {
                chipLayout.RowStyles[0].SizeType = SizeType.Percent;
                chipLayout.RowStyles[0].Height = 44;
                chipLayout.RowStyles[1].SizeType = SizeType.Percent;
                chipLayout.RowStyles[1].Height = 56;
            }
        }
    }

    private static int Scale(Control control, int logicalPixels)
        => Math.Max(1, (int)Math.Round(logicalPixels * Math.Max(96, control.DeviceDpi) / 96d));

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private sealed class Registration
    {
        internal bool Installed { get; set; }
        internal HeaderParts? Parts { get; set; }
        internal ToolTip? ToolTip { get; set; }
    }

    private sealed record HeaderParts(
        TableLayoutPanel Root,
        Panel Header,
        TableLayoutPanel HeaderLayout,
        TableLayoutPanel TitleBlock,
        Label Title,
        Label Subtitle,
        FlowLayoutPanel Metrics,
        Panel[] MetricChips);
}
