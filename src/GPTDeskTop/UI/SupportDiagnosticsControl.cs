using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

public sealed class SupportDiagnosticsControl : UserControl
{
    private readonly SupportBundleService _service;
    private readonly Button _createButton = new()
    {
        Text = "&Create Support Bundle",
        AutoSize = true
    };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill,
        Text = "Privacy-safe diagnostics only — no chat content or database copy.",
        ForeColor = FluentTheme.Muted,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
        AccessibleRole = AccessibleRole.StatusBar
    };
    private readonly ToolTip _toolTip = new()
    {
        AutoPopDelay = 9000,
        InitialDelay = 450,
        ReshowDelay = 100
    };

    private bool _generating;

    public SupportDiagnosticsControl(SupportBundleService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        Dock = DockStyle.Top;
        Height = 58;
        MinimumSize = new Size(0, 58);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = FluentTheme.Background;
        Padding = new Padding(12, 3, 12, 5);
        AccessibleName = "Support diagnostics bundle";
        AccessibleDescription = "Creates a privacy-safe ZIP containing runtime health, counts and sanitized configuration without conversation content.";

        BuildUi();
        ConfigureAccessibility();
        _createButton.Click += async (_, _) => await CreateBundleAsync();
    }

    private void BuildUi()
    {
        var frame = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = FluentTheme.Surface,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(12, 6, 12, 6)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = FluentTheme.Surface,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label
        {
            Text = "Support Diagnostics",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Display", 10.5F, FontStyle.Bold),
            ForeColor = FluentTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        layout.Controls.Add(_status, 1, 0);
        layout.Controls.Add(_createButton, 2, 0);

        frame.Controls.Add(layout);
        Controls.Add(frame);
        FluentTheme.StyleButton(_createButton, primary: true);
        _toolTip.SetToolTip(
            _createButton,
            "Creates a ZIP with sanitized runtime/configuration data, aggregate history counts, and exception-file metadata only. Conversation content is excluded.");
    }

    private void ConfigureAccessibility()
    {
        _createButton.AccessibleName = "Create privacy-safe support bundle";
        _createButton.AccessibleDescription = "Choose a ZIP destination and collect bounded read-only diagnostic information.";
        _status.AccessibleName = "Support bundle status";
    }

    private async Task CreateBundleAsync()
    {
        if (_generating || IsDisposed) return;

        using var dialog = new SaveFileDialog
        {
            Title = "Create GPTDeskTop Support Bundle",
            Filter = "ZIP archive (*.zip)|*.zip",
            DefaultExt = "zip",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"GPTDeskTop-Support-{DateTime.Now:yyyyMMdd-HHmmss}.zip"
        };

        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        _generating = true;
        _createButton.Enabled = false;
        _createButton.Text = "Creating…";
        _status.Text = "Collecting privacy-safe diagnostics (maximum probe window: 5 seconds)…";
        _status.ForeColor = FluentTheme.Accent;

        try
        {
            var path = await _service.CreateAsync(dialog.FileName);
            if (IsDisposed) return;
            _status.Text = $"Support bundle created: {Path.GetFileName(path)}";
            _status.ForeColor = FluentTheme.Success;
            _toolTip.SetToolTip(_status, path);
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, "SupportDiagnosticsControl.CreateBundle");
            if (IsDisposed) return;
            _status.Text = $"Support bundle failed: {ex.GetType().Name}";
            _status.ForeColor = FluentTheme.Danger;
            MessageBox.Show(
                FindForm(),
                ex.Message,
                "Support Bundle",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            if (!IsDisposed)
            {
                _generating = false;
                _createButton.Enabled = true;
                _createButton.Text = "&Create Support Bundle";
            }
        }
    }
}
