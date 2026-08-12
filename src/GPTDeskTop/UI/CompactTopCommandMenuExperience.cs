using System.Reflection;
using System.Runtime.CompilerServices;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

/// <summary>
/// Reclaims vertical space in the operator window by presenting the existing commands through
/// one compact top menu. The existing Buttons remain the single command/event source; the menu
/// only proxies their Click events so runtime behavior is not duplicated.
/// </summary>
internal static class CompactTopCommandMenuExperience
{
    private static readonly ConditionalWeakTable<Form, Installation> Installations = new();

    [ModuleInitializer]
    internal static void Initialize()
        => Application.Idle += InstallOnOpenMainForms;

    private static void InstallOnOpenMainForms(object? sender, EventArgs e)
    {
        foreach (Form form in Application.OpenForms)
        {
            if (form is not MainForm || form.IsDisposed || form.Disposing)
                continue;

            TryInstall(form);
        }
    }

    internal static bool TryInstall(Form form)
    {
        if (Installations.TryGetValue(form, out _))
            return true;

        var development = Descendants(form).OfType<DevelopmentTaskDashboardControl>().FirstOrDefault();
        var health = Descendants(form).OfType<RuntimeHealthControl>().FirstOrDefault();
        if (development is null || health is null)
            return false; // Program adds these after MainForm construction; retry on the next Idle pass.

        var sources = ResolveSources(form, development, health);
        if (!sources.IsComplete)
            return false;

        var menu = BuildMenu(form, development, health, sources);
        var installation = new Installation(menu, development, health);
        Installations.Add(form, installation);

        CollapseLegacyMainToolbar(form);
        CompactDevelopmentPanel(development, sources);
        CompactHealthPanel(health, sources);

        // The compact view is the operator-first default. Details remain available from Commands.
        // ExpandableWorkspaceLayout is the single physical-height owner, including future DPI/size events.
        development.IsExpanded = false;
        health.IsExpanded = false;
        ExpandableWorkspaceLayout.EnableCompactOperatorLayout(development);
        ExpandableWorkspaceLayout.EnableCompactOperatorLayout(health);

        form.MainMenuStrip = menu;
        form.Controls.Add(menu);
        form.Controls.SetChildIndex(menu, 0);
        return true;
    }

    private static MenuStrip BuildMenu(
        Form form,
        DevelopmentTaskDashboardControl development,
        RuntimeHealthControl health,
        CommandSources sources)
    {
        var strip = new MenuStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            AutoSize = true,
            BackColor = FluentTheme.Surface,
            ForeColor = FluentTheme.Text,
            Padding = new Padding(8, 2, 0, 2),
            AccessibleName = "GPTDeskTop command menu",
            AccessibleDescription = "Compact access to development, runtime health, browser, monitor, runtime and application commands."
        };

        var root = new ToolStripMenuItem("☰ Commands")
        {
            AccessibleName = "Commands",
            ToolTipText = "All operator actions in one compact menu."
        };

        var developmentMenu = CreateGroup("Development Plan");
        AddButtonCommand(developmentMenu, "Start", sources.DevelopmentStart);
        AddButtonCommand(developmentMenu, "Pause", sources.DevelopmentPause);
        AddButtonCommand(developmentMenu, "Resume", sources.DevelopmentResume);
        AddButtonCommand(developmentMenu, "Stop", sources.DevelopmentStop);
        developmentMenu.DropDownItems.Add(new ToolStripSeparator());
        AddButtonCommand(developmentMenu, "Messages", sources.DevelopmentMessages);
        AddButtonCommand(developmentMenu, "Schedule", sources.DevelopmentSchedule);
        developmentMenu.DropDownItems.Add(new ToolStripSeparator());
        var developmentDetails = new ToolStripMenuItem();
        developmentDetails.Click += (_, _) => development.IsExpanded = !development.IsExpanded;
        developmentMenu.DropDownItems.Add(developmentDetails);
        developmentMenu.DropDownOpening += (_, _) =>
        {
            SyncButtonItems(developmentMenu);
            developmentDetails.Text = development.IsExpanded ? "Hide Details" : "Show Details";
        };

        var healthMenu = CreateGroup("Runtime Health");
        AddButtonCommand(healthMenu, "Refresh Health", sources.HealthRefresh);
        AddButtonCommand(healthMenu, "Repair…", sources.HealthRepair);
        AddButtonCommand(healthMenu, "Retry Recovery", sources.HealthRetry);
        healthMenu.DropDownItems.Add(new ToolStripSeparator());
        var healthDetails = new ToolStripMenuItem();
        healthDetails.Click += (_, _) => health.IsExpanded = !health.IsExpanded;
        healthMenu.DropDownItems.Add(healthDetails);
        healthMenu.DropDownOpening += (_, _) =>
        {
            SyncButtonItems(healthMenu);
            healthDetails.Text = health.IsExpanded ? "Hide Details" : "Show Details";
        };

        var browserMenu = CreateGroup("Browser");
        AddButtonCommand(browserMenu, "Launch Chrome", sources.LaunchChrome);
        AddButtonCommand(browserMenu, "Hide Chrome", sources.HideChrome);
        AddButtonCommand(browserMenu, "Show Chrome", sources.ShowChrome);
        AddButtonCommand(browserMenu, "Refresh Conversations", sources.RefreshTabs);
        browserMenu.DropDownOpening += (_, _) => SyncButtonItems(browserMenu);

        var monitorMenu = CreateGroup("Monitors");
        AddButtonCommand(monitorMenu, "New Chat + Monitor", sources.NewChatMonitor);
        AddButtonCommand(monitorMenu, "Add Monitor", sources.AddMonitor);
        AddButtonCommand(monitorMenu, "Edit Monitor", sources.EditMonitor);
        AddButtonCommand(monitorMenu, "Delete Monitor", sources.DeleteMonitor);
        monitorMenu.DropDownOpening += (_, _) => SyncButtonItems(monitorMenu);

        var runtimeMenu = CreateGroup("Runtime");
        AddButtonCommand(runtimeMenu, "Start Selected", sources.StartSelected);
        AddButtonCommand(runtimeMenu, "Stop Selected", sources.StopSelected);
        AddButtonCommand(runtimeMenu, "Start All", sources.StartAll);
        AddButtonCommand(runtimeMenu, "Stop All", sources.StopAll);
        runtimeMenu.DropDownOpening += (_, _) => SyncButtonItems(runtimeMenu);

        var appMenu = CreateGroup("Application");
        AddButtonCommand(appMenu, "Settings", sources.Settings);
        appMenu.DropDownOpening += (_, _) => SyncButtonItems(appMenu);

        root.DropDownItems.Add(developmentMenu);
        root.DropDownItems.Add(healthMenu);
        root.DropDownItems.Add(browserMenu);
        root.DropDownItems.Add(monitorMenu);
        root.DropDownItems.Add(runtimeMenu);
        root.DropDownItems.Add(appMenu);
        root.DropDownItems.Add(new ToolStripSeparator());

        var focusLog = new ToolStripMenuItem("Focus Live Activity")
        {
            ToolTipText = "Move keyboard focus to the live activity/log output."
        };
        focusLog.Click += (_, _) => FocusLiveActivity(form);
        root.DropDownItems.Add(focusLog);

        strip.Items.Add(root);
        return strip;
    }

    private static ToolStripMenuItem CreateGroup(string text)
        => new(text) { ForeColor = FluentTheme.Text };

    private static void AddButtonCommand(ToolStripMenuItem group, string text, Button source)
    {
        var item = new ToolStripMenuItem(text)
        {
            Tag = source,
            Enabled = source.Enabled,
            ToolTipText = source.AccessibleDescription ?? source.AccessibleName ?? source.Text
        };
        item.Click += (_, _) => InvokeButtonCommand(source);
        group.DropDownItems.Add(item);
    }

    private static void SyncButtonItems(ToolStripMenuItem group)
    {
        foreach (ToolStripItem item in group.DropDownItems)
        {
            if (item is ToolStripMenuItem menuItem && menuItem.Tag is Button source)
                menuItem.Enabled = source.Enabled && !source.IsDisposed;
        }
    }

    private static void InvokeButtonCommand(Button source)
    {
        if (source.IsDisposed || !source.Enabled)
            return;

        try
        {
            // The original controls are intentionally clipped/hidden to reclaim layout space,
            // which makes Button.PerformClick() reject them through CanSelect. Raising the same
            // protected OnClick path keeps the existing event handlers as the single behavior owner.
            var onClick = source.GetType().GetMethod(
                "OnClick",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(EventArgs) },
                modifiers: null);
            if (onClick is null)
                throw new MissingMethodException(source.GetType().FullName, "OnClick");

            onClick.Invoke(source, new object[] { EventArgs.Empty });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionLogService.Log(ex.InnerException, $"CompactTopCommandMenu.{source.Text}");
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, $"CompactTopCommandMenu.{source.Text}");
        }
    }

    private static void CollapseLegacyMainToolbar(Form form)
    {
        var requiredHeaders = new[] { "BROWSER", "MONITOR", "RUNTIME", "APP" };
        var toolbar = Descendants(form)
            .OfType<FlowLayoutPanel>()
            .FirstOrDefault(panel =>
            {
                var labels = Descendants(panel)
                    .OfType<Label>()
                    .Select(label => label.Text.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                return requiredHeaders.All(labels.Contains);
            });

        if (toolbar is null)
            return;

        toolbar.Visible = false;
        toolbar.AutoSize = false;
        toolbar.MinimumSize = Size.Empty;
        toolbar.MaximumSize = Size.Empty;
        toolbar.Height = 0;
        toolbar.Margin = Padding.Empty;
        toolbar.Padding = Padding.Empty;

        if (toolbar.Parent is TableLayoutPanel layout)
            CollapseTableRow(layout, toolbar);
    }

    private static void CompactDevelopmentPanel(
        DevelopmentTaskDashboardControl development,
        CommandSources sources)
    {
        development.Padding = new Padding(0, 2, 0, 2);

        var actionButtons = new[]
        {
            sources.DevelopmentStart,
            sources.DevelopmentPause,
            sources.DevelopmentResume,
            sources.DevelopmentStop,
            sources.DevelopmentMessages,
            sources.DevelopmentSchedule
        };

        var actionRow = Descendants(development)
            .OfType<FlowLayoutPanel>()
            .FirstOrDefault(panel => actionButtons.All(button => IsDescendantOf(button, panel)));
        if (actionRow is not null)
        {
            actionRow.Visible = false;
            if (actionRow.Parent is TableLayoutPanel bodyLayout)
                CollapseTableRow(bodyLayout, actionRow);
        }

        var toggle = FindButton(development, "Collapse", "Details");
        if (toggle is not null)
            HideHeaderButtonAndColumn(toggle);
    }

    private static void CompactHealthPanel(RuntimeHealthControl health, CommandSources sources)
    {
        health.Padding = new Padding(0, 2, 0, 2);

        foreach (var button in new[] { sources.HealthRefresh, sources.HealthRepair, sources.HealthRetry })
            HideHeaderButtonAndColumn(button);

        var toggle = FindButton(health, "Details", "Hide Details");
        if (toggle is not null)
            HideHeaderButtonAndColumn(toggle);
    }

    private static void HideHeaderButtonAndColumn(Button button)
    {
        button.Visible = false;
        button.TabStop = false;

        if (button.Parent is not TableLayoutPanel layout)
            return;

        var position = layout.GetPositionFromControl(button);
        if (position.Column < 0 || position.Column >= layout.ColumnStyles.Count)
            return;

        layout.ColumnStyles[position.Column].SizeType = SizeType.Absolute;
        layout.ColumnStyles[position.Column].Width = 0;
    }

    private static void CollapseTableRow(TableLayoutPanel layout, Control control)
    {
        var position = layout.GetPositionFromControl(control);
        if (position.Row < 0 || position.Row >= layout.RowStyles.Count)
            return;

        layout.RowStyles[position.Row].SizeType = SizeType.Absolute;
        layout.RowStyles[position.Row].Height = 0;
    }

    private static void FocusLiveActivity(Form form)
    {
        var activity = Descendants(form)
            .OfType<RichTextBox>()
            .FirstOrDefault(box => box.ReadOnly);
        if (activity is null || activity.IsDisposed)
            return;

        activity.Focus();
        activity.SelectionStart = activity.TextLength;
        activity.SelectionLength = 0;
        activity.ScrollToCaret();
    }

    private static CommandSources ResolveSources(
        Form form,
        DevelopmentTaskDashboardControl development,
        RuntimeHealthControl health)
    {
        var developmentButtons = Descendants(development).OfType<Button>().ToArray();
        var healthButtons = Descendants(health).OfType<Button>().ToArray();
        var mainButtons = Descendants(form)
            .OfType<Button>()
            .Where(button => !IsDescendantOf(button, development) && !IsDescendantOf(button, health))
            .ToArray();

        return new CommandSources(
            RequiredButton(developmentButtons, "Start"),
            RequiredButton(developmentButtons, "Pause"),
            RequiredButton(developmentButtons, "Resume"),
            RequiredButton(developmentButtons, "Stop"),
            RequiredButton(developmentButtons, "Messages"),
            RequiredButton(developmentButtons, "Schedule"),
            RequiredButton(healthButtons, "Refresh"),
            RequiredButton(healthButtons, "Repair…"),
            RequiredButton(healthButtons, "Retry"),
            RequiredButton(mainButtons, "Launch Chrome"),
            RequiredButton(mainButtons, "Hide Chrome"),
            RequiredButton(mainButtons, "Show Chrome"),
            RequiredButton(mainButtons, "Refresh"),
            RequiredButton(mainButtons, "New Chat + Monitor"),
            RequiredButton(mainButtons, "Add Monitor"),
            RequiredButton(mainButtons, "Edit Monitor"),
            RequiredButton(mainButtons, "Delete"),
            RequiredButton(mainButtons, "Start Selected"),
            RequiredButton(mainButtons, "Stop Selected"),
            RequiredButton(mainButtons, "Start All"),
            RequiredButton(mainButtons, "Stop All"),
            RequiredButton(mainButtons, "Settings"));
    }

    private static Button RequiredButton(IEnumerable<Button> buttons, string text)
        => buttons.FirstOrDefault(button => string.Equals(button.Text, text, StringComparison.Ordinal))
           ?? MissingButton.Instance;

    private static Button? FindButton(Control root, params string[] texts)
        => Descendants(root)
            .OfType<Button>()
            .FirstOrDefault(button => texts.Contains(button.Text, StringComparer.Ordinal));

    private static bool IsDescendantOf(Control control, Control ancestor)
    {
        for (Control? current = control; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
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

    private sealed record CommandSources(
        Button DevelopmentStart,
        Button DevelopmentPause,
        Button DevelopmentResume,
        Button DevelopmentStop,
        Button DevelopmentMessages,
        Button DevelopmentSchedule,
        Button HealthRefresh,
        Button HealthRepair,
        Button HealthRetry,
        Button LaunchChrome,
        Button HideChrome,
        Button ShowChrome,
        Button RefreshTabs,
        Button NewChatMonitor,
        Button AddMonitor,
        Button EditMonitor,
        Button DeleteMonitor,
        Button StartSelected,
        Button StopSelected,
        Button StartAll,
        Button StopAll,
        Button Settings)
    {
        public bool IsComplete => new[]
        {
            DevelopmentStart, DevelopmentPause, DevelopmentResume, DevelopmentStop,
            DevelopmentMessages, DevelopmentSchedule,
            HealthRefresh, HealthRepair, HealthRetry,
            LaunchChrome, HideChrome, ShowChrome, RefreshTabs,
            NewChatMonitor, AddMonitor, EditMonitor, DeleteMonitor,
            StartSelected, StopSelected, StartAll, StopAll, Settings
        }.All(button => !ReferenceEquals(button, MissingButton.Instance));
    }

    private sealed class Installation(
        MenuStrip menu,
        DevelopmentTaskDashboardControl development,
        RuntimeHealthControl health)
    {
        public MenuStrip Menu { get; } = menu;
        public DevelopmentTaskDashboardControl Development { get; } = development;
        public RuntimeHealthControl Health { get; } = health;
    }

    /// <summary>
    /// Sentinel used only during discovery so installation can wait until Program has populated
    /// the complete visual tree. It is never parented or displayed.
    /// </summary>
    private sealed class MissingButton : Button
    {
        public static MissingButton Instance { get; } = new();
        private MissingButton() { }
    }
}
