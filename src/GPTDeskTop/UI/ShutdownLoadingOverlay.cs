namespace GPTDeskTop.UI;

public sealed class ShutdownLoadingOverlay : Panel
{
    private readonly Label _statusLabel = new()
    {
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI Variable Text", 10F, FontStyle.Regular),
        ForeColor = FluentTheme.Muted,
        AccessibleName = "Shutdown progress status"
    };

    private readonly ProgressBar _progress = new()
    {
        Dock = DockStyle.Fill,
        Style = ProgressBarStyle.Marquee,
        MarqueeAnimationSpeed = 24,
        TabStop = false,
        AccessibleName = "Closing progress"
    };

    public ShutdownLoadingOverlay()
    {
        Dock = DockStyle.Fill;
        Visible = false;
        BackColor = FluentTheme.Background;
        TabStop = false;
        AccessibleName = "GPTDeskTop closing progress";

        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = FluentTheme.Surface,
            Padding = new Padding(28),
            Margin = Padding.Empty
        };
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var title = new Label
        {
            Text = "Closing GPTDeskTop…",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomCenter,
            Font = new Font("Segoe UI Variable Display", 18F, FontStyle.Bold),
            ForeColor = FluentTheme.Text,
            AccessibleName = "Closing GPTDeskTop"
        };

        var progressHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 11, 16, 11),
            BackColor = FluentTheme.Surface
        };
        progressHost.SizeChanged += (_, _) => ApplyResponsiveProgressPadding(progressHost);
        progressHost.Controls.Add(_progress);

        card.Controls.Add(title, 0, 0);
        card.Controls.Add(_statusLabel, 0, 1);
        card.Controls.Add(progressHost, 0, 2);
        Controls.Add(card);
    }

    public void ShowStatus(string status)
    {
        _statusLabel.Text = status;
        Visible = true;
        BringToFront();

        if (Parent is not null)
        {
            foreach (Control sibling in Parent.Controls)
            {
                if (!ReferenceEquals(sibling, this))
                    sibling.Enabled = false;
            }
        }

        // Paint the overlay before shutdown work begins. The actual cleanup remains asynchronous,
        // so the WinForms message loop keeps the marquee and status text responsive.
        Update();
    }

    public void SetStatus(string status)
    {
        _statusLabel.Text = status;
        if (Visible)
            Update();
    }

    private static void ApplyResponsiveProgressPadding(Panel progressHost)
    {
        var width = Math.Max(0, progressHost.ClientSize.Width);
        var horizontalPadding = Math.Clamp(width / 8, 16, 120);
        if (horizontalPadding * 2 >= width)
            horizontalPadding = Math.Max(0, (width - 1) / 2);

        if (progressHost.Padding.Left == horizontalPadding
            && progressHost.Padding.Right == horizontalPadding)
            return;

        progressHost.Padding = new Padding(horizontalPadding, 11, horizontalPadding, 11);
    }
}
