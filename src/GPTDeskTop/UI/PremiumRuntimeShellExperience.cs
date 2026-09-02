using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GPTDeskTop.UI;

/// <summary>
/// Premium operator shell over the existing GPTDeskTop runtime. Owned product destinations are
/// lazily swapped inside exactly one content host; no alternate monitoring/delivery runtime exists.
/// </summary>
internal static class PremiumRuntimeShellExperience
{
    internal const int NavigationRailWidth = 216;
    internal const int MinimumShellWidth = 1100;
    internal const int MinimumShellHeight = 680;
    private const string DashboardDestination = "Dashboard";
    private const string ProjectsDestination = "Projects";
    private const string DevelopmentMessagesDestination = "Development Messages";
    private const string GitSettingsDestination = "GitHub / Git Settings";
    private static readonly ConditionalWeakTable<Form, Registration> Registrations = new();

    internal static Size CalculateLogicalViewport(Size physicalViewport, int deviceDpi)
    {
        var scale = Math.Max(96, deviceDpi) / 96d;
        return new Size(
            Math.Max(0, (int)Math.Floor(physicalViewport.Width / scale)),
            Math.Max(0, (int)Math.Floor(physicalViewport.Height / scale)));
    }

    internal static bool SupportsViewport(Size physicalViewport, int deviceDpi)
    {
        var logical = CalculateLogicalViewport(physicalViewport, deviceDpi);
        return logical.Width >= MinimumShellWidth && logical.Height >= MinimumShellHeight;
    }

    [ModuleInitializer]
    internal static void Initialize()
        => Application.Idle += ApplyToOpenForms;

    internal static bool InstallNow(MainForm main)
    {
        ArgumentNullException.ThrowIfNull(main);
        if (main.IsDisposed || main.Disposing) return false;
        var registration = Registrations.GetValue(main, _ => new Registration());
        if (!registration.ThemeApplied)
        {
            FluentTheme.Apply(main);
            if (main.IsHandleCreated) TryEnableImmersiveDarkTitleBar(main);
            registration.ThemeApplied = true;
        }
        if (!registration.NavigationInstalled)
            registration.NavigationInstalled = TryInstallNavigation(main, registration);
        return registration.NavigationInstalled;
    }

    internal static bool NavigateTo(MainForm main, string destination)
    {
        ArgumentNullException.ThrowIfNull(main);
        if (string.IsNullOrWhiteSpace(destination) || !InstallNow(main)) return false;
        var registration = Registrations.GetValue(main, _ => new Registration());
        return ShowDestination(main, registration, NormalizeDestination(destination));
    }

    internal static string? CurrentDestination(MainForm main)
    {
        if (!Registrations.TryGetValue(main, out var registration)) return null;
        return registration.CurrentDestination;
    }

    private static void ApplyToOpenForms(object? sender, EventArgs e)
    {
        foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
        {
            if (form.IsDisposed || form.Disposing || !form.IsHandleCreated)
                continue;

            var registration = Registrations.GetValue(form, _ => new Registration());
            if (!registration.ThemeApplied)
            {
                FluentTheme.Apply(form);
                TryEnableImmersiveDarkTitleBar(form);
                registration.ThemeApplied = true;
            }

            if (form is MainForm main && !registration.NavigationInstalled)
                registration.NavigationInstalled = TryInstallNavigation(main, registration);
        }
    }

    private static bool TryInstallNavigation(MainForm main, Registration registration)
    {
        if (main.Controls.Find("PremiumShellRoot", searchAllChildren: false).Length != 0)
            return registration.ContentHost is not null;

        if (main.MainMenuStrip is null)
            return false;

        var descendants = Descendants(main).ToArray();
        var projects = descendants.OfType<Button>().FirstOrDefault(button => TextEquals(button, "Projects"));
        var inspector = descendants.OfType<Button>().FirstOrDefault(button => TextEquals(button, "Runtime Inspector"));
        var settings = descendants.OfType<Button>().FirstOrDefault(button => TextEquals(button, "Settings"));
        var development = descendants.OfType<DevelopmentTaskDashboardControl>().FirstOrDefault();
        var messages = development is null
            ? null
            : Descendants(development).OfType<Button>().FirstOrDefault(button => TextEquals(button, "Messages"));

        if (projects is null || inspector is null || settings is null || messages is null || development is null)
            return false;

        var existingSurface = main.Controls.Cast<Control>()
            .FirstOrDefault(control => control.Dock == DockStyle.Fill && control is TableLayoutPanel);
        if (existingSurface is null)
            return false;

        registration.Main = main;
        registration.DashboardSurface = existingSurface;
        registration.DevelopmentDashboard = development;

        var rail = new Panel
        {
            Name = "PremiumNavigationRail",
            Dock = DockStyle.Left,
            Width = NavigationRailWidth,
            BackColor = FluentTheme.SurfaceRaised,
            Padding = new Padding(12, 14, 12, 12),
            AccessibleName = "GPTDeskTop primary navigation",
            AccessibleDescription = "Premium navigation for existing GPTDeskTop runtime features."
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = FluentTheme.SurfaceRaised,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));

        layout.Controls.Add(BuildBrand(), 0, 0);
        layout.Controls.Add(BuildNavigation(registration, main, projects, inspector, messages, settings), 0, 1);
        layout.Controls.Add(BuildRuntimeFooter(inspector), 0, 2);
        rail.Controls.Add(layout);

        var contentHost = new Panel
        {
            Name = "PremiumContentHost",
            Dock = DockStyle.Fill,
            BackColor = FluentTheme.Background,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            AccessibleName = "Current workspace destination",
            AccessibleDescription = "Exactly one active premium product destination is rendered here."
        };
        main.Controls.Remove(existingSurface);
        contentHost.Controls.Add(existingSurface);
        registration.ContentHost = contentHost;
        registration.CurrentDestination = DashboardDestination;

        var shell = new SplitContainer
        {
            Name = "PremiumShellRoot",
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            IsSplitterFixed = true,
            SplitterWidth = 1,
            SplitterDistance = NavigationRailWidth,
            BackColor = FluentTheme.Border,
            TabStop = false
        };
        shell.Panel1MinSize = NavigationRailWidth;
        shell.Panel1.Controls.Add(rail);
        shell.Panel2.Controls.Add(contentHost);
        main.Controls.Add(shell);
        shell.SendToBack();
        main.MinimumSize = new Size(Math.Max(main.MinimumSize.Width, MinimumShellWidth), Math.Max(main.MinimumSize.Height, MinimumShellHeight));
        if (main.Width < MinimumShellWidth || main.Height < MinimumShellHeight)
            main.Size = new Size(Math.Max(main.Width, MinimumShellWidth), Math.Max(main.Height, MinimumShellHeight));

        SetActiveDestination(registration, DashboardDestination);
        return true;
    }

    private static Control BuildBrand()
    {
        var brand = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = FluentTheme.SurfaceRaised,
            Margin = Padding.Empty,
            Padding = new Padding(2, 0, 0, 8)
        };
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        brand.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        brand.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        var mark = new Label
        {
            Text = "G",
            Dock = DockStyle.Fill,
            BackColor = FluentTheme.AccentSubtle,
            ForeColor = FluentTheme.Accent,
            Font = new Font("Segoe UI Variable Display", 20F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0, 0, 8, 0)
        };
        brand.Controls.Add(mark, 0, 0);
        brand.SetRowSpan(mark, 2);

        brand.Controls.Add(new Label
        {
            Text = "GPTDeskTop",
            Dock = DockStyle.Fill,
            ForeColor = FluentTheme.Text,
            Font = new Font("Segoe UI Variable Display", 14F, FontStyle.Bold),
            TextAlign = ContentAlignment.BottomLeft
        }, 1, 0);
        brand.Controls.Add(new Label
        {
            Text = $"v{GetProductVersion()}  •  Premium",
            Dock = DockStyle.Fill,
            ForeColor = FluentTheme.Muted,
            Font = new Font("Segoe UI Variable Text", 8.5F),
            TextAlign = ContentAlignment.TopLeft
        }, 1, 1);
        return brand;
    }

    private static Control BuildNavigation(Registration registration, MainForm main, Button projects, Button inspector, Button messages, Button settings)
    {
        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = FluentTheme.SurfaceRaised,
            Padding = new Padding(0, 12, 0, 8),
            Margin = Padding.Empty
        };

        AddNavigationDestination(registration, nav, DashboardDestination, "▦   Dashboard", () => ShowDestination(main, registration, DashboardDestination), active: true);
        AddNavigationDestination(registration, nav, ProjectsDestination, "▱   Projects", () => ShowDestination(main, registration, ProjectsDestination));
        AddNavigationDestination(registration, nav, "Open Conversations", "◌   Open Conversations", () => ShowDashboardAndFocus(main, registration, FocusConversationGrid));
        AddNavigationDestination(registration, nav, "Saved Monitors", "▣   Saved Monitors", () => ShowDashboardAndFocus(main, registration, FocusMonitorGrid));
        AddNavigationDestination(registration, nav, "Recovery / Runtime Inspector", "♢   Recovery / Runtime Inspector", () => InvokeCanonicalButton(inspector));
        AddNavigationDestination(registration, nav, DevelopmentMessagesDestination, "</>  Development Messages", () => ShowDestination(main, registration, DevelopmentMessagesDestination));
        AddNavigationDestination(registration, nav, GitSettingsDestination, "◉   GitHub / Git Settings", () => ShowDestination(main, registration, GitSettingsDestination));
        AddNavigationDestination(registration, nav, "Settings", "⚙   Settings", () => InvokeCanonicalButton(settings));
        return nav;
    }

    private static void AddNavigationDestination(Registration registration, FlowLayoutPanel navigation, string destination, string text, Action action, bool active = false)
    {
        var button = CreateNavButton(text, destination, () =>
        {
            SetActiveDestination(registration, destination);
            action();
        }, active);
        registration.NavigationButtons[destination] = button;
        navigation.Controls.Add(button);
    }

    private static void ShowDashboardAndFocus(MainForm main, Registration registration, Action<MainForm> focus)
    {
        if (!ShowDestination(main, registration, DashboardDestination)) return;
        focus(main);
    }

    private static bool ShowDestination(MainForm main, Registration registration, string destination)
    {
        var host = registration.ContentHost;
        var dashboard = registration.DashboardSurface;
        if (host is null || dashboard is null || host.IsDisposed) return false;

        Control? surface;
        switch (destination)
        {
            case DashboardDestination:
                surface = dashboard;
                break;
            case ProjectsDestination:
                surface = GetOrCreate(registration, ProjectsDestination, () => ProjectMonitorUiBootstrap.CreateEmbeddedProjectsSurface(main));
                break;
            case DevelopmentMessagesDestination:
                if (registration.DevelopmentDashboard is null) return false;
                surface = GetOrCreate(registration, DevelopmentMessagesDestination, () => new DevelopmentMessagesWorkspaceControl(registration.DevelopmentDashboard.RuntimeBinding));
                break;
            case GitSettingsDestination:
                surface = GetOrCreate(registration, GitSettingsDestination, () => GitHubIntegrationUiBootstrap.CreateEmbeddedGitSettingsSurface(main));
                break;
            default:
                return false;
        }

        if (surface.IsDisposed) return false;
        host.SuspendLayout();
        try
        {
            if (host.Controls.Count != 1 || !ReferenceEquals(host.Controls[0], surface))
            {
                host.Controls.Clear();
                surface.Dock = DockStyle.Fill;
                surface.Margin = Padding.Empty;
                host.Controls.Add(surface);
            }
            surface.Visible = true;
            surface.BringToFront();
            registration.CurrentDestination = destination;
            SetActiveDestination(registration, destination);
            FluentTheme.Apply(main);
        }
        finally
        {
            host.ResumeLayout(performLayout: true);
        }

        if (surface is ProjectMonitorDashboardControl projects)
            _ = projects.RefreshAsync();
        FocusControl(surface);
        return host.Controls.Count == 1;
    }

    private static Control GetOrCreate(Registration registration, string destination, Func<Control> factory)
    {
        if (registration.Destinations.TryGetValue(destination, out var existing) && !existing.IsDisposed)
            return existing;
        var created = factory();
        created.Dock = DockStyle.Fill;
        registration.Destinations[destination] = created;
        return created;
    }

    private static void SetActiveDestination(Registration registration, string destination)
    {
        foreach (var pair in registration.NavigationButtons)
            SetNavigationState(pair.Value, string.Equals(pair.Key, destination, StringComparison.OrdinalIgnoreCase));
    }

    private static void SetNavigationState(Button button, bool active)
    {
        button.BackColor = active ? FluentTheme.AccentSubtle : FluentTheme.SurfaceRaised;
        button.ForeColor = active ? FluentTheme.Accent : FluentTheme.MutedStrong;
        button.AccessibleDescription = active ? "Current destination" : "Open destination";
    }

    private static Control BuildRuntimeFooter(Button inspector)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = FluentTheme.SurfaceAlt,
            Padding = new Padding(12, 10, 12, 10),
            Margin = new Padding(0, 8, 0, 0)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = FluentTheme.SurfaceAlt };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = "●  Runtime & Recovery",
            Dock = DockStyle.Fill,
            ForeColor = FluentTheme.Success,
            Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = "Live diagnostics available",
            Dock = DockStyle.Fill,
            ForeColor = FluentTheme.Muted,
            Font = new Font("Segoe UI Variable Text", 8.25F),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);
        var open = CreateNavButton("Open Inspector  ›", "Recovery / Runtime Inspector", () => InvokeCanonicalButton(inspector));
        open.Height = 30;
        open.ForeColor = FluentTheme.Accent;
        layout.Controls.Add(open, 0, 2);
        card.Controls.Add(layout);
        return card;
    }

    private static Button CreateNavButton(string text, string destination, Action action, bool active = false)
    {
        var button = new Button
        {
            Text = text,
            Tag = destination,
            Width = 184,
            Height = 42,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 3, 0, 3),
            Padding = new Padding(12, 0, 8, 0),
            AccessibleName = destination
        };
        FluentTheme.StyleButton(button, primary: false);
        button.TextAlign = ContentAlignment.MiddleLeft;
        button.Padding = new Padding(12, 0, 8, 0);
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = active ? FluentTheme.AccentSubtle : FluentTheme.SurfaceRaised;
        button.ForeColor = active ? FluentTheme.Accent : FluentTheme.MutedStrong;
        button.Click += (_, _) => action();
        return button;
    }

    private static void FocusConversationGrid(MainForm main)
    {
        var grid = Descendants(main).OfType<DataGridView>()
            .FirstOrDefault(candidate => candidate.Columns.Cast<DataGridViewColumn>()
                .Any(column => string.Equals(column.HeaderText, "Tab ID", StringComparison.OrdinalIgnoreCase)));
        FocusControl(grid is null ? main : grid);
    }

    private static void FocusMonitorGrid(MainForm main)
    {
        var grid = Descendants(main).OfType<DataGridView>()
            .FirstOrDefault(candidate =>
            {
                var headers = candidate.Columns.Cast<DataGridViewColumn>().Select(column => column.HeaderText).ToHashSet(StringComparer.OrdinalIgnoreCase);
                return headers.Contains("Runtime") && headers.Contains("Auto reply");
            });
        FocusControl(grid is null ? main : grid);
    }

    private static void FocusControl(Control control)
    {
        if (control.IsDisposed) return;
        (control.Parent as ScrollableControl)?.ScrollControlIntoView(control);
        if (control.CanFocus) control.Focus();
    }

    private static void InvokeCanonicalButton(Button source)
    {
        if (source.IsDisposed || !source.Enabled) return;
        var onClick = source.GetType().GetMethod(
            "OnClick",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(EventArgs) },
            modifiers: null);
        onClick?.Invoke(source, new object[] { EventArgs.Empty });
    }

    private static bool TextEquals(Button button, string text)
        => string.Equals(button.Text?.Trim(), text, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeDestination(string destination)
    {
        var value = destination.Trim();
        if (string.Equals(value, ProjectsDestination, StringComparison.OrdinalIgnoreCase)) return ProjectsDestination;
        if (string.Equals(value, DevelopmentMessagesDestination, StringComparison.OrdinalIgnoreCase)) return DevelopmentMessagesDestination;
        if (string.Equals(value, GitSettingsDestination, StringComparison.OrdinalIgnoreCase) || string.Equals(value, "Git Settings", StringComparison.OrdinalIgnoreCase)) return GitSettingsDestination;
        if (string.Equals(value, DashboardDestination, StringComparison.OrdinalIgnoreCase)) return DashboardDestination;
        return value;
    }

    private static string GetProductVersion()
    {
        try
        {
            var path = Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location;
            return FileVersionInfo.GetVersionInfo(path).ProductVersion?.Split('+')[0] ?? "2.0.0";
        }
        catch { return "2.0.0"; }
    }

    private static void TryEnableImmersiveDarkTitleBar(Form form)
    {
        try
        {
            var enabled = 1;
            if (DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(form.Handle, 19, ref enabled, sizeof(int));
        }
        catch
        {
            // The premium theme still works on systems where DWM attributes are unavailable.
        }
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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    private sealed class Registration
    {
        public bool ThemeApplied { get; set; }
        public bool NavigationInstalled { get; set; }
        public MainForm? Main { get; set; }
        public Panel? ContentHost { get; set; }
        public Control? DashboardSurface { get; set; }
        public DevelopmentTaskDashboardControl? DevelopmentDashboard { get; set; }
        public string CurrentDestination { get; set; } = DashboardDestination;
        public Dictionary<string, Control> Destinations { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Button> NavigationButtons { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
