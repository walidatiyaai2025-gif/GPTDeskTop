using GPTDeskTop.Data;

namespace GPTDeskTop.UI;

internal static class MonitorOnlyStartupCoordinator
{
    private static SimpleMonitorForm? _startupMonitor;
    private static MainForm? _mainForm;
    private static bool _prepared;
    private static bool _switchingToDashboard;

    internal static void Prepare(MainForm mainForm, LocalDatabase database)
    {
        ArgumentNullException.ThrowIfNull(mainForm);
        ArgumentNullException.ThrowIfNull(database);
        if (_prepared) return;
        _prepared = true;
        _mainForm = mainForm;

        // MainForm remains the application lifetime owner so all existing recovery/handoff
        // behavior stays intact, but it is never painted during cold start. Monitor Only is
        // the first visible operator surface.
        mainForm.Opacity = 0d;
        mainForm.ShowInTaskbar = false;
        mainForm.Shown += (_, _) => ShowMonitorOnlyFirst(database);
        mainForm.FormClosed += (_, _) =>
        {
            _switchingToDashboard = false;
            if (_startupMonitor is { IsDisposed: false })
                _startupMonitor.Close();
            _startupMonitor = null;
            _mainForm = null;
        };
    }

    private static void ShowMonitorOnlyFirst(LocalDatabase database)
    {
        var mainForm = _mainForm;
        if (mainForm is null || mainForm.IsDisposed || mainForm.Disposing) return;

        mainForm.BeginInvoke(new Action(() =>
        {
            if (mainForm.IsDisposed || mainForm.Disposing) return;

            mainForm.Hide();
            mainForm.Opacity = 1d;
            mainForm.ShowInTaskbar = true;

            if (_startupMonitor is null || _startupMonitor.IsDisposed)
            {
                _startupMonitor = new SimpleMonitorForm(database);
                _startupMonitor.FormClosed += (_, _) =>
                {
                    _startupMonitor = null;
                    ShowCurrentDashboard();
                };
            }

            _startupMonitor.Show();
            if (_startupMonitor.WindowState == FormWindowState.Minimized)
                _startupMonitor.WindowState = FormWindowState.Normal;
            _startupMonitor.BringToFront();
            _startupMonitor.Activate();
        }));
    }

    private static void ShowCurrentDashboard()
    {
        if (_switchingToDashboard) return;
        var mainForm = _mainForm;
        if (mainForm is null || mainForm.IsDisposed || mainForm.Disposing) return;

        _switchingToDashboard = true;
        try
        {
            mainForm.Opacity = 1d;
            mainForm.ShowInTaskbar = true;
            mainForm.Show();
            if (mainForm.WindowState == FormWindowState.Minimized)
                mainForm.WindowState = FormWindowState.Normal;
            mainForm.BringToFront();
            mainForm.Activate();
        }
        finally
        {
            _switchingToDashboard = false;
        }
    }
}
