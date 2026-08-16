using System.Drawing;

namespace GPTDeskTop.Setup;

internal sealed class InstallerWizardForm : Form
{
    internal const int WizardContractVersion = 1;
    internal static readonly string[] RequiredWizardStages = ["Welcome", "Options", "Ready", "Installing", "Complete"];

    private readonly Label _title = new() { AutoSize = false, Dock = DockStyle.Top, Height = 46, Font = new Font("Segoe UI", 18F, FontStyle.Bold) };
    private readonly Label _body = new() { AutoSize = false, Dock = DockStyle.Top, Height = 115, Font = new Font("Segoe UI", 10.5F), Padding = new Padding(0, 8, 0, 0) };
    private readonly Label _destination = new() { AutoSize = false, Dock = DockStyle.Top, Height = 54, Font = new Font("Segoe UI", 9.5F), Padding = new Padding(12, 10, 12, 10), BorderStyle = BorderStyle.FixedSingle };
    private readonly CheckBox _desktopShortcut = new() { AutoSize = true, Text = "Create a desktop shortcut", Checked = true, Font = new Font("Segoe UI", 10F) };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Top, Height = 22, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 24, Visible = false };
    private readonly Label _status = new() { AutoSize = false, Dock = DockStyle.Top, Height = 52, Font = new Font("Segoe UI", 9.5F), Padding = new Padding(0, 12, 0, 0), Visible = false };
    private readonly CheckBox _launchAfterInstall = new() { AutoSize = true, Text = "Launch GPTDeskTop after setup", Checked = true, Font = new Font("Segoe UI", 10F), Visible = false };
    private readonly Button _back = new() { Text = "< Back", Width = 92, Height = 32 };
    private readonly Button _next = new() { Text = "Next >", Width = 92, Height = 32 };
    private readonly Button _cancel = new() { Text = "Cancel", Width = 92, Height = 32 };
    private readonly Panel _optionPanel = new() { Dock = DockStyle.Top, Height = 45 };

    private int _page;
    private bool _installing;
    private bool _installed;

    internal InstallerWizardForm()
    {
        Text = "GPTDeskTop Setup";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(640, 410);
        MinimumSize = new Size(640, 410);
        MaximumSize = new Size(640, 410);
        MaximizeBox = false;
        MinimizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F);
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath); } catch { }

        var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Color.White, Padding = new Padding(28, 18, 28, 10) };
        header.Controls.Add(new Label
        {
            Text = "GPTDeskTop Setup Wizard",
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        });

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 62, BackColor = SystemColors.Control, Padding = new Padding(0, 14, 18, 12) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 310, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        buttons.Controls.AddRange([_back, _next, _cancel]);
        footer.Controls.Add(buttons);

        _optionPanel.Padding = new Padding(0, 8, 0, 0);
        _optionPanel.Controls.Add(_desktopShortcut);

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(32, 26, 32, 20) };
        content.Controls.Add(_launchAfterInstall);
        content.Controls.Add(_status);
        content.Controls.Add(_progress);
        content.Controls.Add(_optionPanel);
        content.Controls.Add(_destination);
        content.Controls.Add(_body);
        content.Controls.Add(_title);

        Controls.Add(content);
        Controls.Add(footer);
        Controls.Add(header);

        _back.Click += (_, _) => NavigateBack();
        _next.Click += async (_, _) => await NavigateNextAsync();
        _cancel.Click += (_, _) => Close();
        FormClosing += OnFormClosing;

        RenderPage();
    }

    internal static bool VerifyWizardContract() =>
        WizardContractVersion >= 1
        && RequiredWizardStages.SequenceEqual(["Welcome", "Options", "Ready", "Installing", "Complete"], StringComparer.Ordinal)
        && typeof(InstallerWizardForm).IsSubclassOf(typeof(Form));

    private void NavigateBack()
    {
        if (_installing || _page <= 0) return;
        _page--;
        RenderPage();
    }

    private async Task NavigateNextAsync()
    {
        if (_installing) return;
        if (_page < 2)
        {
            _page++;
            RenderPage();
            return;
        }

        if (_page == 2)
        {
            _page = 3;
            RenderPage();
            await RunInstallationAsync();
            return;
        }

        if (_page == 4)
        {
            if (_installed && _launchAfterInstall.Checked) Program.LaunchInstalledApplication();
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private void RenderPage()
    {
        _destination.Visible = false;
        _optionPanel.Visible = false;
        _progress.Visible = false;
        _status.Visible = false;
        _launchAfterInstall.Visible = false;
        _back.Enabled = !_installing && _page is > 0 and < 3;
        _cancel.Enabled = !_installing && _page != 4;
        _next.Enabled = !_installing;
        _next.Text = "Next >";

        switch (_page)
        {
            case 0:
                _title.Text = "Welcome";
                _body.Text = "Welcome to the GPTDeskTop Setup Wizard.\r\n\r\nThis wizard will install GPTDeskTop for the current Windows user. Click Next to continue.";
                break;
            case 1:
                _title.Text = "Installation options";
                _body.Text = "GPTDeskTop will be installed in the following location. Start Menu shortcuts and the uninstaller are created automatically.";
                _destination.Text = Program.GetInstallDirectory();
                _destination.Visible = true;
                _optionPanel.Visible = true;
                break;
            case 2:
                _title.Text = "Ready to install";
                _body.Text = $"Setup is ready to install GPTDeskTop v{Program.Version}.\r\n\r\nClick Install to copy application files and create Windows shortcuts.";
                _destination.Text = Program.GetInstallDirectory();
                _destination.Visible = true;
                _next.Text = "Install";
                break;
            case 3:
                _title.Text = "Installing GPTDeskTop";
                _body.Text = "Setup is installing GPTDeskTop. Please do not close this window.";
                _progress.Visible = true;
                _status.Visible = true;
                _status.Text = "Copying application files and creating shortcuts...";
                _back.Enabled = false;
                _next.Enabled = false;
                _cancel.Enabled = false;
                break;
            case 4:
                _title.Text = _installed ? "Setup complete" : "Setup failed";
                _body.Text = _installed
                    ? "GPTDeskTop was installed successfully. Click Finish to close Setup."
                    : "Setup could not complete the installation. Review the error below and try again.";
                _status.Visible = !_installed;
                _launchAfterInstall.Visible = _installed;
                _back.Enabled = false;
                _cancel.Enabled = false;
                _next.Enabled = true;
                _next.Text = "Finish";
                break;
        }
    }

    private Task RunInstallationOnStaThreadAsync(bool createDesktopShortcut)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Program.Install(createDesktopShortcut);
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        }) { IsBackground = true, Name = "GPTDeskTop Setup Installer" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private async Task RunInstallationAsync()
    {
        _installing = true;
        RenderPage();
        try
        {
            await RunInstallationOnStaThreadAsync(_desktopShortcut.Checked);
            _installed = true;
            _status.Text = string.Empty;
        }
        catch (Exception ex)
        {
            _installed = false;
            _status.Text = ex.Message;
        }
        finally
        {
            _installing = false;
            _page = 4;
            RenderPage();
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_installing)
        {
            e.Cancel = true;
            return;
        }

        if (!_installed && _page is > 0 and < 4 && e.CloseReason == CloseReason.UserClosing)
        {
            var result = MessageBox.Show("Exit GPTDeskTop Setup? The application has not been installed yet.", "GPTDeskTop Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes) e.Cancel = true;
        }
    }
}
