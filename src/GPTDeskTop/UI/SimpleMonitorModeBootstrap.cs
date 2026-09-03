using System.Reflection;
using System.Runtime.CompilerServices;
using GPTDeskTop.Data;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

internal static class SimpleMonitorModeBootstrap
{
    private static readonly HashSet<nint> InstalledHandles = new();
    private static SimpleMonitorForm? _monitorOnlyForm;
    private static bool _switching;

    [ModuleInitializer]
    internal static void Initialize()
    {
        Application.Idle += InstallIntoOpenMainForm;
    }

    private static void InstallIntoOpenMainForm(object? sender, EventArgs e)
    {
        foreach (var main in Application.OpenForms.OfType<MainForm>().ToArray())
            Install(main);
    }

    internal static void Install(MainForm main)
    {
        ArgumentNullException.ThrowIfNull(main);
        if (main.IsDisposed || main.Disposing || !main.IsHandleCreated) return;
        if (InstalledHandles.Contains(main.Handle)) return;
        if (FindDescendants(main).Any(control => string.Equals(control.Name, "MonitorOnlyModeSelector", StringComparison.Ordinal)))
        {
            InstalledHandles.Add(main.Handle);
            return;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var monitor = typeof(MainForm).GetField("_monitor", flags)?.GetValue(main) as ChatGptMonitorService
            ?? throw new InvalidOperationException("GPTDeskTop monitor service is unavailable for Monitor Only mode.");
        var database = typeof(MainForm).GetField("_database", flags)?.GetValue(main) as LocalDatabase
            ?? throw new InvalidOperationException("GPTDeskTop database is unavailable for Monitor Only mode.");

        var bar = new Panel
        {
            Name = "MonitorOnlyModeSelector",
            Dock = DockStyle.Top,
            Height = 46,
            BackColor = FluentTheme.Surface,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(14, 7, 14, 7)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = FluentTheme.Surface
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 390));

        layout.Controls.Add(new Label
        {
            Text = "Operating Mode",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var choices = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = FluentTheme.Surface,
            Padding = new Padding(0, 3, 0, 0)
        };
        var monitorOnly = new RadioButton
        {
            Text = "Monitor Only — Same Chat",
            AutoSize = true,
            AccessibleName = "Switch to Monitor Only same chat mode"
        };
        var current = new RadioButton
        {
            Text = "Current GPTDeskTop",
            AutoSize = true,
            Checked = true,
            AccessibleName = "Use current GPTDeskTop mode"
        };
        choices.Controls.Add(monitorOnly);
        choices.Controls.Add(current);
        layout.Controls.Add(choices, 1, 0);
        bar.Controls.Add(layout);

        monitorOnly.CheckedChanged += async (_, _) =>
        {
            if (!monitorOnly.Checked || _switching) return;
            _switching = true;
            monitorOnly.Enabled = false;
            current.Enabled = false;
            try
            {
                await monitor.StopAllAsync();
                await LastWorkingStateService.ReplaceDesiredMonitorIdsAsync(database, Array.Empty<long>());

                if (_monitorOnlyForm is null || _monitorOnlyForm.IsDisposed)
                {
                    _monitorOnlyForm = new SimpleMonitorForm(database);
                    MonitorOnlyExperienceController.Attach(_monitorOnlyForm);
                    _monitorOnlyForm.FormClosed += (_, _) =>
                    {
                        _monitorOnlyForm = null;
                        if (!main.IsDisposed && !main.Disposing)
                        {
                            current.Checked = true;
                            monitorOnly.Checked = false;
                            main.Show();
                            if (main.WindowState == FormWindowState.Minimized) main.WindowState = FormWindowState.Normal;
                            main.BringToFront();
                            main.Activate();
                        }
                    };
                }

                main.Hide();
                _monitorOnlyForm.Show();
                _monitorOnlyForm.BringToFront();
                _monitorOnlyForm.Activate();
            }
            catch (Exception ex)
            {
                ExceptionLogService.Log(ex, "SimpleMonitorModeBootstrap.Switch");
                current.Checked = true;
                monitorOnly.Checked = false;
                MessageBox.Show(
                    main,
                    $"Monitor Only mode could not be opened.\r\n\r\n{ex.Message}",
                    "Monitor Only",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                monitorOnly.Enabled = true;
                current.Enabled = true;
                _switching = false;
            }
        };

        main.Controls.Add(bar);
        main.Controls.SetChildIndex(bar, 0);
        bar.BringToFront();
        InstalledHandles.Add(main.Handle);
    }

    private static IEnumerable<Control> FindDescendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in FindDescendants(child)) yield return descendant;
        }
    }
}
