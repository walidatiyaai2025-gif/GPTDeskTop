using System.Reflection;
using System.Runtime.CompilerServices;

namespace GPTDeskTop.UI;

/// <summary>
/// Keeps Monitor Only's primary runtime actions and product version visible at all supported
/// window sizes/DPI settings. This is presentation-only: it does not start, stop, resend,
/// recover, rotate, or otherwise alter Monitor Only business state.
/// </summary>
internal static class MonitorOnlyVisualHotfix
{
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly HashSet<nint> InstalledHandles = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        Application.Idle += InstallIntoOpenMonitorForms;
    }

    private static void InstallIntoOpenMonitorForms(object? sender, EventArgs e)
    {
        foreach (var form in Application.OpenForms.OfType<SimpleMonitorForm>().ToArray())
            Apply(form);
    }

    internal static void Apply(SimpleMonitorForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (form.IsDisposed || form.Disposing || !form.IsHandleCreated) return;
        if (InstalledHandles.Contains(form.Handle)) return;

        var root = GetField<TableLayoutPanel>(form, "_root");
        if (root.RowCount < 4) return;

        root.SuspendLayout();
        try
        {
            // The premium shell is designed to fit the supported minimum window. AutoScroll on
            // the outer table can make Percent rows grow to preferred size and push the fixed
            // footer below the viewport, so keep scrolling inside the message panes instead.
            root.AutoScroll = false;
            root.RowStyles[1] = new RowStyle(SizeType.Absolute, 210);
            root.RowStyles[3] = new RowStyle(SizeType.Absolute, 60);

            EnsureRuntimeActions(form, root);
            EnsureVersionFooter(root);

            form.MinimumSize = new Size(1100, 720);
            form.Text = $"GPTDeskTop v{GetProductVersion()} — Monitor Only";
        }
        finally
        {
            root.ResumeLayout(true);
        }

        InstalledHandles.Add(form.Handle);
    }

    private static void EnsureRuntimeActions(SimpleMonitorForm form, TableLayoutPanel root)
    {
        var topCards = root.GetControlFromPosition(0, 1) as TableLayoutPanel;
        var runtime = topCards?.GetControlFromPosition(1, 0) as GroupBox;
        if (runtime is null) return;
        if (runtime.Controls.Find("MonitorOnlyRuntimeActionBar", true).Length > 0) return;

        var start = GetField<Button>(form, "_startButton");
        var stop = GetField<Button>(form, "_stopButton");

        var runtimeLayout = runtime.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
        if (runtimeLayout is not null)
        {
            var padding = runtimeLayout.Padding;
            runtimeLayout.Padding = new Padding(padding.Left, padding.Top, padding.Right, Math.Max(padding.Bottom, 50));
        }

        var actions = new TableLayoutPanel
        {
            Name = "MonitorOnlyRuntimeActionBar",
            Dock = DockStyle.Bottom,
            Height = 48,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = FluentTheme.Surface,
            Padding = new Padding(6, 4, 6, 4),
            Margin = Padding.Empty
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        start.AutoSize = false;
        stop.AutoSize = false;
        start.Dock = DockStyle.Fill;
        stop.Dock = DockStyle.Fill;
        start.Margin = new Padding(3, 0, 5, 0);
        stop.Margin = new Padding(5, 0, 3, 0);
        start.MinimumSize = new Size(0, 36);
        stop.MinimumSize = new Size(0, 36);

        actions.Controls.Add(start, 0, 0);
        actions.Controls.Add(stop, 1, 0);
        runtime.Controls.Add(actions);
        actions.BringToFront();

        FluentTheme.StyleButton(start, primary: true);
        FluentTheme.StyleButton(stop, danger: true);
    }

    private static void EnsureVersionFooter(TableLayoutPanel root)
    {
        var footer = root.GetControlFromPosition(0, 3) as Panel;
        var layout = footer?.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
        if (layout is null) return;
        if (layout.Controls.Find("MonitorOnlyVersionLabel", true).Length > 0) return;

        var liveDot = layout.Controls
            .OfType<Label>()
            .FirstOrDefault(label => string.Equals(label.AccessibleName, "Live stream state", StringComparison.Ordinal));

        layout.SuspendLayout();
        try
        {
            layout.ColumnCount = 5;
            layout.ColumnStyles.Clear();
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 13));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));

            if (liveDot is not null)
                layout.SetColumn(liveDot, 4);

            var version = new Label
            {
                Name = "MonitorOnlyVersionLabel",
                Text = $"v{GetProductVersion()}",
                Dock = DockStyle.Fill,
                ForeColor = FluentTheme.MutedStrong,
                Font = new Font("Segoe UI Variable Text", 8.75F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = false,
                AccessibleName = "GPTDeskTop version"
            };
            layout.Controls.Add(version, 3, 0);
        }
        finally
        {
            layout.ResumeLayout(true);
        }
    }

    private static string GetProductVersion()
    {
        var version = typeof(SimpleMonitorForm).Assembly.GetName().Version;
        return version is null
            ? Application.ProductVersion
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static T GetField<T>(SimpleMonitorForm form, string name) where T : class
        => typeof(SimpleMonitorForm).GetField(name, PrivateInstance)?.GetValue(form) as T
            ?? throw new InvalidOperationException($"Monitor Only control '{name}' is unavailable.");
}
