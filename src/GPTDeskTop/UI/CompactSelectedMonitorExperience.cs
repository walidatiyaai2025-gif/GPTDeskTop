using System.Runtime.CompilerServices;

namespace GPTDeskTop.UI;

/// <summary>
/// Reclaims the permanent 32% Selected Monitor area on the primary operator surface.
/// The Saved Monitors list owns all flexible height while the existing live summary and
/// Edit Selected Monitor button remain visible in a compact DPI-aware status strip.
/// </summary>
internal static class CompactSelectedMonitorExperience
{
    private const int CompactStripLogicalHeight = 58;
    private const int HeadingLogicalWidth = 112;
    private const int EditButtonLogicalWidth = 164;

    private static readonly ConditionalWeakTable<MainForm, Installation> Installations = new();

    [ModuleInitializer]
    internal static void Initialize()
        => Application.Idle += (_, _) => ApplyOpenMainForms();

    private static void ApplyOpenMainForms()
    {
        foreach (Form openForm in Application.OpenForms)
        {
            if (openForm is MainForm main && !main.IsDisposed && !main.Disposing)
                TryInstall(main);
        }
    }

    internal static bool TryInstall(MainForm form)
    {
        if (Installations.TryGetValue(form, out _))
            return true;

        var editButton = Descendants(form)
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(button.Text, "Edit Selected Monitor", StringComparison.Ordinal));
        if (editButton?.Parent is not TableLayoutPanel editor || editor.RowCount != 3 || editor.ColumnCount != 4)
            return false;

        if (editor.Parent is not Panel border || border.Parent is not TableLayoutPanel monitorPane ||
            monitorPane.RowCount != 2 || monitorPane.ColumnCount != 1 || monitorPane.RowStyles.Count < 2)
            return false;

        var heading = editor.GetControlFromPosition(0, 0);
        var autoReplyLabel = editor.GetControlFromPosition(0, 1);
        var autoReplyValue = editor.GetControlFromPosition(1, 1);
        var enabledValue = editor.GetControlFromPosition(2, 1);
        var summaryLabel = editor.GetControlFromPosition(0, 2);
        var summaryValue = editor.GetControlFromPosition(1, 2);
        if (heading is null || autoReplyLabel is null || autoReplyValue is null || enabledValue is null ||
            summaryLabel is null || summaryValue is null)
            return false;

        var installation = new Installation(
            form,
            monitorPane,
            border,
            editor,
            heading,
            autoReplyLabel,
            autoReplyValue,
            enabledValue,
            summaryLabel,
            summaryValue,
            editButton);
        Installations.Add(form, installation);
        installation.Apply();
        return true;
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

    private sealed class Installation : IDisposable
    {
        private readonly MainForm _form;
        private readonly TableLayoutPanel _monitorPane;
        private readonly Panel _border;
        private readonly TableLayoutPanel _editor;
        private readonly Control _heading;
        private readonly Control _autoReplyLabel;
        private readonly Control _autoReplyValue;
        private readonly Control _enabledValue;
        private readonly Control _summaryLabel;
        private readonly Control _summaryValue;
        private readonly Button _editButton;
        private readonly EventHandler _dpiChangedHandler;
        private readonly FormClosedEventHandler _formClosedHandler;
        private bool _disposed;

        internal Installation(
            MainForm form,
            TableLayoutPanel monitorPane,
            Panel border,
            TableLayoutPanel editor,
            Control heading,
            Control autoReplyLabel,
            Control autoReplyValue,
            Control enabledValue,
            Control summaryLabel,
            Control summaryValue,
            Button editButton)
        {
            _form = form;
            _monitorPane = monitorPane;
            _border = border;
            _editor = editor;
            _heading = heading;
            _autoReplyLabel = autoReplyLabel;
            _autoReplyValue = autoReplyValue;
            _enabledValue = enabledValue;
            _summaryLabel = summaryLabel;
            _summaryValue = summaryValue;
            _editButton = editButton;

            _dpiChangedHandler = (_, _) => Apply();
            _formClosedHandler = (_, _) => Dispose();
            _monitorPane.DpiChangedAfterParent += _dpiChangedHandler;
            _form.FormClosed += _formClosedHandler;
        }

        internal void Apply()
        {
            if (_disposed || _form.IsDisposed || _monitorPane.IsDisposed || _editor.IsDisposed)
                return;

            _monitorPane.SuspendLayout();
            _border.SuspendLayout();
            _editor.SuspendLayout();
            try
            {
                // The monitor list receives every flexible pixel; selection context becomes one
                // fixed strip instead of permanently reserving 32% of the pane.
                _monitorPane.RowStyles[0].SizeType = SizeType.Percent;
                _monitorPane.RowStyles[0].Height = 100F;
                _monitorPane.RowStyles[1].SizeType = SizeType.Absolute;
                _monitorPane.RowStyles[1].Height = Scale(_monitorPane, CompactStripLogicalHeight);

                _border.Margin = new Padding(0, Scale(_border, 6), 0, 0);
                _border.Padding = new Padding(1);

                _editor.Padding = new Padding(
                    Scale(_editor, 10),
                    Scale(_editor, 4),
                    Scale(_editor, 10),
                    Scale(_editor, 4));
                _editor.Margin = Padding.Empty;

                _editor.RowStyles[0].SizeType = SizeType.Percent;
                _editor.RowStyles[0].Height = 100F;
                _editor.RowStyles[1].SizeType = SizeType.Absolute;
                _editor.RowStyles[1].Height = 0F;
                _editor.RowStyles[2].SizeType = SizeType.Absolute;
                _editor.RowStyles[2].Height = 0F;

                _editor.ColumnStyles[0].SizeType = SizeType.Absolute;
                _editor.ColumnStyles[0].Width = Scale(_editor, HeadingLogicalWidth);
                _editor.ColumnStyles[1].SizeType = SizeType.Percent;
                _editor.ColumnStyles[1].Width = 100F;
                _editor.ColumnStyles[2].SizeType = SizeType.Absolute;
                _editor.ColumnStyles[2].Width = 0F;
                _editor.ColumnStyles[3].SizeType = SizeType.Absolute;
                _editor.ColumnStyles[3].Width = Scale(_editor, EditButtonLogicalWidth);

                // Reuse the live _editorLabel and the original edit button so monitor selection,
                // enabled/running rules and the existing edit click handler remain the behavior owner.
                _editor.SetColumnSpan(_heading, 1);
                _editor.SetCellPosition(_heading, new TableLayoutPanelCellPosition(0, 0));
                _editor.SetColumnSpan(_summaryValue, 2);
                _editor.SetCellPosition(_summaryValue, new TableLayoutPanelCellPosition(1, 0));
                _editor.SetColumnSpan(_editButton, 1);
                _editor.SetCellPosition(_editButton, new TableLayoutPanelCellPosition(3, 0));

                _autoReplyLabel.Visible = false;
                _autoReplyValue.Visible = false;
                _enabledValue.Visible = false;
                _summaryLabel.Visible = false;
                _heading.Visible = true;
                _summaryValue.Visible = true;
                _editButton.Visible = true;

                if (_heading is Label headingLabel)
                {
                    headingLabel.Dock = DockStyle.Fill;
                    headingLabel.TextAlign = ContentAlignment.MiddleLeft;
                    headingLabel.AutoEllipsis = true;
                }

                if (_summaryValue is Label summaryLabel)
                {
                    summaryLabel.Dock = DockStyle.Fill;
                    summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
                    summaryLabel.AutoEllipsis = true;
                }

                _editButton.Dock = DockStyle.Fill;
                _editButton.AutoSize = false;
                _editButton.Margin = new Padding(Scale(_editButton, 6), Scale(_editButton, 4), 0, Scale(_editButton, 4));
            }
            finally
            {
                _editor.ResumeLayout(true);
                _border.ResumeLayout(true);
                _monitorPane.ResumeLayout(true);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _monitorPane.DpiChangedAfterParent -= _dpiChangedHandler;
            _form.FormClosed -= _formClosedHandler;
        }
    }
}
