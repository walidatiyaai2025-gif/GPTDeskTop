using System.Runtime.CompilerServices;

namespace GPTDeskTop.UI;

/// <summary>
/// Keeps the two primary operator-card headings visible while reclaiming the permanent subtitle
/// rows beneath them. Guidance moves to tooltip/accessibility metadata; the grids retain all
/// flexible height and all existing interaction owners remain untouched.
/// </summary>
internal static class CompactPrimarySectionExperience
{
    private const int HeaderLogicalHeight = 28;
    private const int HorizontalPaddingLogical = 10;
    private const int TopPaddingLogical = 4;
    private const int BottomPaddingLogical = 8;

    private static readonly string[] PrimaryTitles =
    [
        "Open ChatGPT Conversations",
        "Saved Monitors"
    ];

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

        var sections = new List<SectionRegistration>(PrimaryTitles.Length);
        foreach (var expectedTitle in PrimaryTitles)
        {
            var title = Descendants(form)
                .OfType<Label>()
                .FirstOrDefault(label => string.Equals(label.Text, expectedTitle, StringComparison.Ordinal));
            if (title?.Parent is not TableLayoutPanel layout || layout.RowCount != 3 || layout.ColumnCount != 1 || layout.RowStyles.Count < 3)
                return false;

            var subtitle = layout.GetControlFromPosition(0, 1);
            var content = layout.GetControlFromPosition(0, 2);
            if (subtitle is null || content is null || layout.Parent is not Panel card)
                return false;

            sections.Add(new SectionRegistration(card, layout, title, subtitle, content));
        }

        var installation = new Installation(form, sections);
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
        private readonly IReadOnlyList<SectionRegistration> _sections;
        private readonly ToolTip _toolTip = new();
        private readonly FormClosedEventHandler _formClosedHandler;
        private bool _disposed;

        internal Installation(MainForm form, IReadOnlyList<SectionRegistration> sections)
        {
            _form = form;
            _sections = sections;
            _formClosedHandler = (_, _) => Dispose();
            _form.FormClosed += _formClosedHandler;

            foreach (var section in _sections)
            {
                section.DpiChangedHandler = (_, _) => ApplySection(section);
                section.Layout.DpiChangedAfterParent += section.DpiChangedHandler;
            }
        }

        internal void Apply()
        {
            if (_disposed || _form.IsDisposed || _form.Disposing)
                return;

            foreach (var section in _sections)
                ApplySection(section);
        }

        private void ApplySection(SectionRegistration section)
        {
            if (_disposed || section.Layout.IsDisposed || section.Card.IsDisposed)
                return;

            section.Card.SuspendLayout();
            section.Layout.SuspendLayout();
            try
            {
                var guidance = section.Subtitle.Text;
                if (!string.IsNullOrWhiteSpace(guidance))
                {
                    _toolTip.SetToolTip(section.Title, guidance);
                    section.Title.AccessibleDescription = guidance;
                }

                section.Card.Padding = new Padding(
                    Scale(section.Card, HorizontalPaddingLogical),
                    Scale(section.Card, TopPaddingLogical),
                    Scale(section.Card, HorizontalPaddingLogical),
                    Scale(section.Card, BottomPaddingLogical));

                section.Layout.RowStyles[0].SizeType = SizeType.Absolute;
                section.Layout.RowStyles[0].Height = Scale(section.Layout, HeaderLogicalHeight);
                section.Layout.RowStyles[1].SizeType = SizeType.Absolute;
                section.Layout.RowStyles[1].Height = 0F;
                section.Layout.RowStyles[2].SizeType = SizeType.Percent;
                section.Layout.RowStyles[2].Height = 100F;

                section.Subtitle.Visible = false;
                section.Subtitle.TabStop = false;
                section.Title.Visible = true;
                section.Title.Dock = DockStyle.Fill;
                section.Title.TextAlign = ContentAlignment.MiddleLeft;
                section.Title.AutoEllipsis = true;
                section.Content.Dock = DockStyle.Fill;
            }
            finally
            {
                section.Layout.ResumeLayout(true);
                section.Card.ResumeLayout(true);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var section in _sections)
            {
                if (section.DpiChangedHandler is not null && !section.Layout.IsDisposed)
                    section.Layout.DpiChangedAfterParent -= section.DpiChangedHandler;
            }

            _form.FormClosed -= _formClosedHandler;
            _toolTip.Dispose();
        }
    }

    private sealed class SectionRegistration
    {
        internal SectionRegistration(Panel card, TableLayoutPanel layout, Label title, Control subtitle, Control content)
        {
            Card = card;
            Layout = layout;
            Title = title;
            Subtitle = subtitle;
            Content = content;
        }

        internal Panel Card { get; }
        internal TableLayoutPanel Layout { get; }
        internal Label Title { get; }
        internal Control Subtitle { get; }
        internal Control Content { get; }
        internal EventHandler? DpiChangedHandler { get; set; }
    }
}
