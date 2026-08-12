using System.Runtime.CompilerServices;

namespace GPTDeskTop.UI;

internal static class CompactOperatorHeaderExperienceBootstrap
{
    private static readonly ConditionalWeakTable<Form, Registration> Registrations = new();

    [ModuleInitializer]
    internal static void Initialize()
        => Application.Idle += ScheduleForOpenMainForms;

    private static void ScheduleForOpenMainForms(object? sender, EventArgs e)
    {
        foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
        {
            if (form is not MainForm || form.IsDisposed || form.Disposing || !form.IsHandleCreated)
                continue;

            var registration = Registrations.GetValue(form, _ => new Registration());
            if (registration.Applied || registration.Scheduled)
                continue;

            registration.Scheduled = true;
            try
            {
                form.BeginInvoke(new Action(() =>
                {
                    registration.Scheduled = false;
                    Apply(form, registration);
                }));
            }
            catch (InvalidOperationException)
            {
                registration.Scheduled = false;
            }
        }
    }

    private static void Apply(Form form, Registration registration)
    {
        if (form.IsDisposed || form.Disposing || registration.Applied)
            return;

        var controls = Descendants(form).ToArray();
        var root = controls.OfType<TableLayoutPanel>()
            .FirstOrDefault(table => table.Dock == DockStyle.Fill && table.RowCount == 5 && table.ColumnCount == 1);
        var title = controls.OfType<Label>()
            .FirstOrDefault(label => string.Equals(label.Text.Trim(), "GPTDeskTop", StringComparison.Ordinal));
        var subtitle = controls.OfType<Label>()
            .FirstOrDefault(label => label.Text.Contains(
                "monitoring, recovery and conversation automation",
                StringComparison.OrdinalIgnoreCase));

        if (root is null || title is null || subtitle is null)
            return;

        var titleBlock = FindAncestor<TableLayoutPanel>(title, table => table.RowCount == 2 && table.ColumnCount == 1);
        var header = FindAncestor<Panel>(title, panel => panel.Dock == DockStyle.Fill);
        var metrics = controls.OfType<FlowLayoutPanel>()
            .FirstOrDefault(panel => ContainsMetric(panel, "Running")
                                     && ContainsMetric(panel, "Monitors")
                                     && ContainsMetric(panel, "Conversation tabs")
                                     && ContainsMetric(panel, "Chrome window"));

        if (titleBlock is null || header is null || metrics is null)
            return;

        registration.Applied = true;
        registration.Root = root;
        registration.Header = header;
        registration.Metrics = metrics;
        registration.TitleBlock = titleBlock;
        registration.Title = title;
        registration.Subtitle = subtitle;

        ApplyCompactPresentation(registration);
        root.DpiChangedAfterParent += (_, _) => ApplyCompactPresentation(registration);
    }

    private static void ApplyCompactPresentation(Registration registration)
    {
        var root = registration.Root;
        var header = registration.Header;
        var metrics = registration.Metrics;
        var titleBlock = registration.TitleBlock;
        var title = registration.Title;
        var subtitle = registration.Subtitle;

        if (root is null || header is null || metrics is null || titleBlock is null || title is null || subtitle is null)
            return;
        if (root.IsDisposed || header.IsDisposed || metrics.IsDisposed || titleBlock.IsDisposed)
            return;

        if (root.RowStyles.Count > 0)
        {
            root.RowStyles[0].SizeType = SizeType.Absolute;
            root.RowStyles[0].Height = Scale(root, 58);
        }

        header.Padding = ScalePadding(header, 14, 4, 10, 4);
        header.Margin = ScalePadding(header, 0, 0, 0, 6);

        subtitle.Visible = false;
        subtitle.TabStop = false;
        title.TextAlign = ContentAlignment.MiddleLeft;

        if (titleBlock.RowStyles.Count >= 2)
        {
            titleBlock.RowStyles[0].SizeType = SizeType.Percent;
            titleBlock.RowStyles[0].Height = 100;
            titleBlock.RowStyles[1].SizeType = SizeType.Absolute;
            titleBlock.RowStyles[1].Height = 0;
        }

        metrics.Padding = Padding.Empty;
        metrics.Margin = Padding.Empty;
        metrics.WrapContents = false;

        foreach (var chip in metrics.Controls.OfType<Panel>())
        {
            chip.MinimumSize = new Size(Scale(chip, 100), Scale(chip, 40));
            chip.Padding = ScalePadding(chip, 10, 4, 10, 4);
            chip.Margin = ScalePadding(chip, 4, 0, 0, 0);
        }
    }

    private static bool ContainsMetric(Control root, string caption)
        => Descendants(root)
            .OfType<Label>()
            .Any(label => string.Equals(label.Text.Trim(), caption, StringComparison.OrdinalIgnoreCase));

    private static T? FindAncestor<T>(Control control, Func<T, bool> predicate) where T : Control
    {
        for (Control? current = control.Parent; current is not null; current = current.Parent)
        {
            if (current is T candidate && predicate(candidate))
                return candidate;
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

    internal static int Scale(Control control, int logicalPixels)
        => Math.Max(1, (int)Math.Round(logicalPixels * Math.Max(96, control.DeviceDpi) / 96d));

    private static Padding ScalePadding(Control control, int left, int top, int right, int bottom)
        => new(
            Scale(control, left),
            Scale(control, top),
            Scale(control, right),
            Scale(control, bottom));

    private sealed class Registration
    {
        internal bool Scheduled { get; set; }
        internal bool Applied { get; set; }
        internal TableLayoutPanel? Root { get; set; }
        internal Panel? Header { get; set; }
        internal FlowLayoutPanel? Metrics { get; set; }
        internal TableLayoutPanel? TitleBlock { get; set; }
        internal Label? Title { get; set; }
        internal Label? Subtitle { get; set; }
    }
}
