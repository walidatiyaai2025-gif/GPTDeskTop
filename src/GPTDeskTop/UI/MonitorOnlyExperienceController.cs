using System.Reflection;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

/// <summary>
/// Applies the premium Monitor Only composition without changing its business rules and owns
/// the read-only live chat footer stream. This controller is intentionally Monitor-Only local:
/// it never creates MainForm, ChatGptMonitorService, crash recovery, development runtime, or
/// any other Current GPTDeskTop business.
/// </summary>
internal sealed class MonitorOnlyExperienceController : IDisposable
{
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo GetConversationUrlMethod = typeof(SimpleMonitorForm).GetMethod(
        "GetConversationUrl",
        PrivateInstance)
        ?? throw new MissingMethodException(typeof(SimpleMonitorForm).FullName, "GetConversationUrl");
    private static readonly MethodInfo PassiveStateReader = typeof(ChromeDevToolsService).GetMethod(
        "ReadChatStateCoreAsync",
        PrivateInstance)
        ?? throw new MissingMethodException(typeof(ChromeDevToolsService).FullName, "ReadChatStateCoreAsync");

    private readonly SimpleMonitorForm _form;
    private readonly TableLayoutPanel _root;
    private readonly RadioButton _currentModeRadio;
    private readonly RadioButton _monitorModeRadio;
    private readonly Label _statusLabel;
    private readonly Label _cycleLabel;
    private readonly Label _liveStreamLabel = new();
    private readonly Label _liveDot = new();
    private readonly System.Windows.Forms.Timer _streamTimer = new() { Interval = 1500 };
    private readonly CancellationTokenSource _streamCancellation = new();
    private bool _streamReadInFlight;
    private bool _disposed;
    private string _lastRenderedStream = string.Empty;

    private MonitorOnlyExperienceController(SimpleMonitorForm form)
    {
        _form = form ?? throw new ArgumentNullException(nameof(form));
        _root = GetField<TableLayoutPanel>(form, "_root");
        _currentModeRadio = GetField<RadioButton>(form, "_currentModeRadio");
        _monitorModeRadio = GetField<RadioButton>(form, "_monitorModeRadio");
        _statusLabel = GetField<Label>(form, "_statusLabel");
        _cycleLabel = GetField<Label>(form, "_cycleLabel");

        ApplyPremiumLayout();
        ConfigureLiveFooter();

        _streamTimer.Tick += StreamTimerOnTick;
        _form.Shown += FormOnShown;
        _form.FormClosing += FormOnFormClosing;
        _form.FormClosed += FormOnFormClosed;
    }

    public bool SwitchToCurrentRequested { get; private set; }

    internal static MonitorOnlyExperienceController Attach(SimpleMonitorForm form)
        => new(form);

    private void ApplyPremiumLayout()
    {
        var target = _root.GetControlFromPosition(0, 1)
            ?? throw new InvalidOperationException("Monitor Only target card is unavailable.");
        var messages = _root.GetControlFromPosition(0, 2)
            ?? throw new InvalidOperationException("Monitor Only message-plan card is unavailable.");
        var runtime = _root.GetControlFromPosition(0, 3)
            ?? throw new InvalidOperationException("Monitor Only runtime card is unavailable.");
        var inspector = _root.GetControlFromPosition(0, 4)
            ?? throw new InvalidOperationException("Monitor Only Runtime Inspector card is unavailable.");

        _root.SuspendLayout();
        try
        {
            _root.Controls.Clear();
            _root.ColumnStyles.Clear();
            _root.RowStyles.Clear();
            _root.ColumnCount = 1;
            _root.RowCount = 4;
            _root.Padding = new Padding(12);
            _root.AutoScroll = true;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

            var header = BuildPremiumHeader();
            var topCards = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0, 8, 0, 8),
                BackColor = FluentTheme.Background
            };
            topCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            topCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23));
            topCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
            topCards.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            target.Dock = DockStyle.Fill;
            runtime.Dock = DockStyle.Fill;
            inspector.Dock = DockStyle.Fill;
            target.Margin = new Padding(0, 0, 8, 0);
            runtime.Margin = new Padding(0, 0, 8, 0);
            inspector.Margin = Padding.Empty;
            topCards.Controls.Add(target, 0, 0);
            topCards.Controls.Add(runtime, 1, 0);
            topCards.Controls.Add(inspector, 2, 0);

            messages.Dock = DockStyle.Fill;
            messages.Margin = new Padding(0, 0, 0, 8);

            _root.Controls.Add(header, 0, 0);
            _root.Controls.Add(topCards, 0, 1);
            _root.Controls.Add(messages, 0, 2);
            _root.Controls.Add(BuildPremiumFooter(), 0, 3);

            _form.MinimumSize = new Size(1100, 700);
            if (_form.WindowState == FormWindowState.Normal)
                _form.ClientSize = new Size(Math.Max(_form.ClientSize.Width, 1500), Math.Max(_form.ClientSize.Height, 850));
        }
        finally
        {
            _root.ResumeLayout(true);
        }

        // Re-apply the dark premium theme to newly-created composition controls, then restore
        // semantic emphasis on the existing business buttons.
        FluentTheme.Apply(_form);
        FluentTheme.StyleButton(GetField<Button>(_form, "_connectButton"), primary: true);
        FluentTheme.StyleButton(GetField<Button>(_form, "_startButton"), primary: true);
        FluentTheme.StyleButton(GetField<Button>(_form, "_loadPlanButton"), primary: true);
        FluentTheme.StyleButton(GetField<Button>(_form, "_removeMessageButton"), danger: true);
    }

    private Control BuildPremiumHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = FluentTheme.Surface,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(12, 7, 12, 7),
            Margin = Padding.Empty
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = FluentTheme.Surface
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));

        var titleArea = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = FluentTheme.Surface
        };
        titleArea.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        titleArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        titleArea.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        titleArea.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        var mark = new Label
        {
            Text = "◇",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Display", 23F, FontStyle.Bold),
            ForeColor = FluentTheme.Info,
            TextAlign = ContentAlignment.MiddleCenter
        };
        titleArea.Controls.Add(mark, 0, 0);
        titleArea.SetRowSpan(mark, 2);
        titleArea.Controls.Add(new Label
        {
            Text = "Monitor Only",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Display", 15F, FontStyle.Bold),
            TextAlign = ContentAlignment.BottomLeft
        }, 1, 0);
        titleArea.Controls.Add(new Label
        {
            Text = "Monitor the same chat until assistant response is complete.",
            Dock = DockStyle.Fill,
            ForeColor = FluentTheme.Muted,
            TextAlign = ContentAlignment.TopLeft,
            AutoEllipsis = true
        }, 1, 1);

        var modesCard = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = FluentTheme.SurfaceAlt,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(10, 7, 10, 5),
            Margin = new Padding(8, 3, 0, 3)
        };
        var modes = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = FluentTheme.SurfaceAlt,
            Padding = new Padding(0, 3, 0, 0)
        };
        _monitorModeRadio.Text = "Monitor Only — Same Chat";
        _currentModeRadio.Text = "Current GPTDeskTop";
        _monitorModeRadio.Margin = new Padding(16, 3, 0, 0);
        _currentModeRadio.Margin = new Padding(0, 3, 0, 0);
        modes.Controls.Add(_monitorModeRadio);
        modes.Controls.Add(_currentModeRadio);
        modesCard.Controls.Add(modes);

        layout.Controls.Add(titleArea, 0, 0);
        layout.Controls.Add(modesCard, 1, 0);
        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildPremiumFooter()
    {
        var footer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = FluentTheme.Surface,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(12, 6, 12, 6),
            Margin = Padding.Empty
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = FluentTheme.Surface
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.AutoEllipsis = true;

        _liveStreamLabel.Dock = DockStyle.Fill;
        _liveStreamLabel.Text = "LIVE CHAT • Connect a profile to start the footer stream.";
        _liveStreamLabel.ForeColor = FluentTheme.MutedStrong;
        _liveStreamLabel.TextAlign = ContentAlignment.MiddleLeft;
        _liveStreamLabel.AutoEllipsis = true;
        _liveStreamLabel.AccessibleName = "Live ChatGPT response stream";
        _liveStreamLabel.Font = new Font("Segoe UI Variable Text", 8.75F, FontStyle.Regular);
        _liveStreamLabel.Padding = new Padding(12, 0, 8, 0);

        _cycleLabel.Dock = DockStyle.Fill;
        _cycleLabel.TextAlign = ContentAlignment.MiddleRight;
        _cycleLabel.AutoEllipsis = true;

        _liveDot.Dock = DockStyle.Fill;
        _liveDot.Text = "●";
        _liveDot.ForeColor = FluentTheme.Muted;
        _liveDot.TextAlign = ContentAlignment.MiddleCenter;
        _liveDot.Font = new Font("Segoe UI Symbol", 11F, FontStyle.Bold);
        _liveDot.AccessibleName = "Live stream state";

        layout.Controls.Add(_statusLabel, 0, 0);
        layout.Controls.Add(_liveStreamLabel, 1, 0);
        layout.Controls.Add(_cycleLabel, 2, 0);
        layout.Controls.Add(_liveDot, 3, 0);
        footer.Controls.Add(layout);
        return footer;
    }

    private void ConfigureLiveFooter()
    {
        _liveStreamLabel.Text = "LIVE CHAT • Waiting for the selected same-chat target.";
        _liveDot.ForeColor = FluentTheme.Muted;
    }

    private void FormOnShown(object? sender, EventArgs e)
    {
        if (_disposed) return;
        _streamTimer.Start();
    }

    private void FormOnFormClosing(object? sender, FormClosingEventArgs e)
    {
        // Cold-start legacy business is authorized only by the explicit Current GPTDeskTop radio.
        // Closing the window with X/Alt+F4 while Monitor Only remains selected exits the app instead.
        SwitchToCurrentRequested = _currentModeRadio.Checked;
    }

    private void FormOnFormClosed(object? sender, FormClosedEventArgs e)
        => Dispose();

    private async void StreamTimerOnTick(object? sender, EventArgs e)
    {
        if (_disposed || _streamReadInFlight || !_form.Visible) return;
        _streamReadInFlight = true;
        try
        {
            var session = GetOptionalField<SimpleMonitorProfileSession>(_form, "_session");
            if (session is null)
            {
                RenderStream("Connect Profile to stream the selected ChatGPT conversation live.", isGenerating: false, isConnected: false);
                return;
            }

            var conversationUrl = (string?)GetConversationUrlMethod.Invoke(_form, null) ?? string.Empty;
            if (!SimpleMonitorProfileSession.TryGetConversationId(conversationUrl, out _))
            {
                RenderStream("Select a stable /c/{conversation-id} chat to start live streaming.", isGenerating: false, isConnected: true);
                return;
            }

            var tab = await session.ResolveConversationAsync(
                conversationUrl,
                openIfMissing: false,
                _streamCancellation.Token);
            if (tab is null)
            {
                RenderStream("Selected same chat is not currently open.", isGenerating: false, isConnected: true);
                return;
            }

            var snapshot = await ReadLiveSnapshotAsync(session.Chrome, tab, _streamCancellation.Token);
            var text = string.IsNullOrWhiteSpace(snapshot.Text)
                ? snapshot.IsGenerating ? "Assistant is generating…" : "Waiting for assistant activity in the selected chat."
                : CompactTail(snapshot.Text, 360);
            RenderStream(text, snapshot.IsGenerating, isConnected: true);
        }
        catch (OperationCanceledException) when (_streamCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // This stream is diagnostic/read-only. A transient footer read must never change Monitor
            // business state, trigger a resend, stop the runner, or start legacy recovery.
            RenderStream($"Live stream temporarily unavailable: {CompactTail(ex.Message, 180)}", isGenerating: false, isConnected: true);
        }
        finally
        {
            _streamReadInFlight = false;
        }
    }

    private void RenderStream(string text, bool isGenerating, bool isConnected)
    {
        if (_disposed || _form.IsDisposed || _form.Disposing) return;
        var prefix = isGenerating ? "LIVE CHAT ● " : isConnected ? "LIVE CHAT • " : "LIVE CHAT ○ ";
        var rendered = prefix + text;
        if (string.Equals(rendered, _lastRenderedStream, StringComparison.Ordinal)) return;
        _lastRenderedStream = rendered;
        _liveStreamLabel.Text = rendered;
        _liveStreamLabel.ForeColor = isGenerating ? FluentTheme.Text : FluentTheme.MutedStrong;
        _liveDot.ForeColor = isGenerating ? FluentTheme.Success : isConnected ? FluentTheme.Info : FluentTheme.Muted;
    }

    private static Task<LiveChatSnapshot> ReadLiveSnapshotAsync(
        ChromeDevToolsService chrome,
        ChromeTab tab,
        CancellationToken cancellationToken)
        => SimpleMonitorPassiveReadGate.RunAsync(async () =>
        {
            try
            {
                var task = (Task<ChatPageState>)(PassiveStateReader.Invoke(
                    chrome,
                    new object[] { tab, cancellationToken })
                    ?? throw new InvalidOperationException("Passive live chat reader returned no task."));
                var state = await task.ConfigureAwait(true);
                return new LiveChatSnapshot(state.IsGenerating, state.LastAssistantText ?? string.Empty);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw ex.InnerException;
            }
        }, cancellationToken);

    private static string CompactTail(string value, int maxLength)
    {
        var singleLine = value.Replace("\r", " ").Replace("\n", " ").Trim();
        if (singleLine.Length <= maxLength) return singleLine;
        return "…" + singleLine[^Math.Max(1, maxLength - 1)..];
    }

    private static T GetField<T>(object instance, string name) where T : class
        => typeof(SimpleMonitorForm).GetField(name, PrivateInstance)?.GetValue(instance) as T
            ?? throw new MissingFieldException(typeof(SimpleMonitorForm).FullName, name);

    private static T? GetOptionalField<T>(object instance, string name) where T : class
        => typeof(SimpleMonitorForm).GetField(name, PrivateInstance)?.GetValue(instance) as T;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _streamTimer.Stop();
        _streamTimer.Tick -= StreamTimerOnTick;
        _streamTimer.Dispose();
        _streamCancellation.Cancel();
        _streamCancellation.Dispose();
        _form.Shown -= FormOnShown;
        _form.FormClosing -= FormOnFormClosing;
        _form.FormClosed -= FormOnFormClosed;
    }

    private sealed record LiveChatSnapshot(bool IsGenerating, string Text);
}
