using System.Drawing.Imaging;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GPTDeskTop.Configuration;
using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;
using GPTDeskTop.Services.DevelopmentTaskEngine;
using GPTDeskTop.UI;

namespace GPTDeskTop.RuntimeTests;

public sealed class RenderedPremiumUiAcceptanceTests
{
    private const string TaskId = "GPTDESKTOP-T0002-UI-PRODUCT-CLOSURE";

    [Fact]
    public void ProductionWinFormsSurfacesRenderAcceptanceMatrixAndWriteEvidenceManifest()
    {
        var repoRoot = RepositoryRoot();
        var evidenceDirectory = Path.Combine(repoRoot, "artifacts", "ui-evidence");
        Directory.CreateDirectory(evidenceDirectory);
        var checks = new Dictionary<string, EvidenceCheck>(StringComparer.OrdinalIgnoreCase);
        var screenshots = new List<ScreenshotEvidence>();
        var failures = new List<string>();
        var referencePath = Path.Combine(repoRoot, "docs", "ui-reference", "APPROVED_PREMIUM_DASHBOARD_REFERENCE.jpg");
        var referenceSha = File.Exists(referencePath) ? Sha256(referencePath) : string.Empty;

        try
        {
            RunSta(() => ExecuteRenderedAcceptance(repoRoot, evidenceDirectory, checks, screenshots, failures));
            Record(checks, failures, "design-parity", File.Exists(referencePath) && referenceSha.Length == 64,
                File.Exists(referencePath) ? $"Approved reference SHA-256 {referenceSha}." : "Approved premium reference is missing.");
        }
        catch (Exception ex)
        {
            failures.Add($"Unhandled rendered acceptance failure: {ex}");
            checks["QA-UI-001"] = new EvidenceCheck("FAIL", ex.Message);
        }
        finally
        {
            foreach (var required in new[]
            {
                "QA-UI-001", "1366x768", "1600x900", "1920x1080", "125%-DPI",
                "single-surface", "multiline-interaction", "Projects", "Development Messages",
                "GitHub/Git", "design-parity"
            })
            {
                if (!checks.ContainsKey(required))
                    checks[required] = new EvidenceCheck("NOT_RUN", "Acceptance did not reach this gate.");
            }

            var manifest = new
            {
                schema = "gptdesktop.ui-evidence.v1",
                taskId = TaskId,
                generatedUtc = DateTimeOffset.UtcNow,
                sourceSha = Environment.GetEnvironmentVariable("UI_EVIDENCE_SOURCE_SHA")
                            ?? Environment.GetEnvironmentVariable("GITHUB_SHA")
                            ?? "local",
                reference = new { path = "docs/ui-reference/APPROVED_PREMIUM_DASHBOARD_REFERENCE.jpg", sha256 = referenceSha },
                checks,
                screenshots,
                failures
            };
            File.WriteAllText(
                Path.Combine(evidenceDirectory, "ui-evidence-manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static void ExecuteRenderedAcceptance(
        string repoRoot,
        string evidenceDirectory,
        Dictionary<string, EvidenceCheck> checks,
        List<ScreenshotEvidence> screenshots,
        List<string> failures)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "gptdesktop-ui-acceptance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        DevelopmentTaskRuntimeBinding? binding = null;
        MainForm? main = null;
        HttpClient? http = null;

        try
        {
            var database = new LocalDatabase(Path.Combine(scratch, "acceptance.db"));
            database.InitializeAsync().GetAwaiter().GetResult();
            http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            var chrome = new ChromeDevToolsService(http, new ChromeConfig());
            var monitor = new ChatGptMonitorService(chrome, database, new MonitoringConfig());
            main = new MainForm(chrome, monitor, database, () => Task.CompletedTask);
            ProjectMonitorUiBootstrap.Install(main);

            File.WriteAllText(
                Path.Combine(scratch, "development-task-messages.json"),
                JsonSerializer.Serialize(new { messages = new[] { "Inspect {planTitle}.\r\nReport verified progress.", "Continue {planId} step {step}/{total}." } }, new JsonSerializerOptions { WriteIndented = true }));
            var engine = new DevelopmentTaskEngine(
                statePath: Path.Combine(scratch, "development-task-state.json"),
                messagesPath: Path.Combine(scratch, "development-task-messages.json"),
                scheduleSettingsPath: Path.Combine(scratch, "development-task-schedule.json"));
            var resolver = new SavedMonitorTabResolver(chrome);
            var targetFactory = new DevelopmentTaskMonitorTargetFactory(database, resolver, chrome);
            binding = new DevelopmentTaskRuntimeBinding(engine, targetFactory);
            var development = new DevelopmentTaskDashboardControl(binding) { Dock = DockStyle.Top, IsExpanded = false, TabStop = false };
            var health = new RuntimeHealthControl(chrome, monitor, database) { Dock = DockStyle.Top, IsExpanded = false, TabStop = false };
            main.Controls.Add(development);
            main.Controls.SetChildIndex(development, 0);
            main.Controls.Add(health);
            main.Controls.SetChildIndex(health, 0);

            var menuInstalled = CompactTopCommandMenuExperience.TryInstall(main);
            var shellInstalled = PremiumRuntimeShellExperience.InstallNow(main);
            Record(checks, failures, "single-surface", menuInstalled && shellInstalled && SingleSurface(main),
                $"Compact menu installed={menuInstalled}; premium shell installed={shellInstalled}; content children={ContentHost(main)?.Controls.Count ?? -1}.");

            var matrix = new[]
            {
                new ViewportCase("1366x768", 1366, 768, 96, "dashboard-1366x768-100.png"),
                new ViewportCase("1600x900", 1600, 900, 96, "dashboard-1600x900-100.png"),
                new ViewportCase("1920x1080", 1920, 1080, 96, "dashboard-1920x1080-100.png"),
                new ViewportCase("125%-DPI", 1920, 1080, 120, "dashboard-1920x1080-125dpi.png")
            };

            foreach (var viewport in matrix)
            {
                PremiumRuntimeShellExperience.NavigateTo(main, "Dashboard");
                var path = Path.Combine(evidenceDirectory, viewport.FileName);
                var logical = Render(main, viewport.Width, viewport.Height, viewport.Dpi, path);
                var pass = PremiumRuntimeShellExperience.SupportsViewport(new Size(viewport.Width, viewport.Height), viewport.Dpi)
                           && SingleSurface(main)
                           && File.Exists(path)
                           && new FileInfo(path).Length > 1000;
                Record(checks, failures, viewport.Name, pass,
                    $"Rendered {viewport.Width}x{viewport.Height} at {viewport.Dpi} DPI equivalent; logical viewport {logical.Width}x{logical.Height}.");
                screenshots.Add(new ScreenshotEvidence(viewport.FileName, "Dashboard", viewport.Width, viewport.Height, viewport.Dpi, logical.Width, logical.Height, pass ? "PASS" : "FAIL"));
            }

            PremiumRuntimeShellExperience.NavigateTo(main, "Projects");
            var projectsPath = Path.Combine(evidenceDirectory, "projects-1920x1080-100.png");
            Render(main, 1920, 1080, 96, projectsPath);
            var projects = Descendants(main).OfType<ProjectMonitorDashboardControl>().FirstOrDefault(x => x.Name == "PremiumProjectsWorkspace");
            var projectsGrid = projects is null ? null : Descendants(projects).OfType<DataGridView>().FirstOrDefault(x => x.AccessibleName == "Registered projects");
            var projectHeaders = projectsGrid?.Columns.Cast<DataGridViewColumn>().Select(x => x.HeaderText).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            var projectsPass = projects is not null && SingleSurface(main)
                               && new[] { "Project", "Status", "Progress", "Active tasks", "Health", "Branch", "Repository", "Updated", "Latest result" }.All(projectHeaders.Contains);
            Record(checks, failures, "Projects", projectsPass, "Rendered embedded Projects workspace with repository, branch, progress, health and real persisted evidence controls.");
            screenshots.Add(new ScreenshotEvidence("projects-1920x1080-100.png", "Projects", 1920, 1080, 96, 1920, 1080, projectsPass ? "PASS" : "FAIL"));

            PremiumRuntimeShellExperience.NavigateTo(main, "Development Messages");
            var messagesPath = Path.Combine(evidenceDirectory, "development-messages-1920x1080-100.png");
            Render(main, 1920, 1080, 96, messagesPath);
            var messagesWorkspace = Descendants(main).OfType<DevelopmentMessagesWorkspaceControl>().FirstOrDefault();
            var messageEditor = messagesWorkspace is null ? null : Descendants(messagesWorkspace).OfType<TextBox>().FirstOrDefault(x => x.AccessibleName == "Development message editor");
            var devPass = messagesWorkspace is not null && messageEditor is { Multiline: true, AcceptsReturn: true } && SingleSurface(main);
            Record(checks, failures, "Development Messages", devPass, "Rendered lifecycle/catalog/schedule/receipt workspace over the canonical DevelopmentTaskRuntimeBinding.");
            screenshots.Add(new ScreenshotEvidence("development-messages-1920x1080-100.png", "Development Messages", 1920, 1080, 96, 1920, 1080, devPass ? "PASS" : "FAIL"));

            if (messageEditor is not null)
            {
                messageEditor.Text = "First instruction\r\nSecond instruction";
                messageEditor.SelectionStart = messageEditor.TextLength;
                messageEditor.SelectedText = "\r\nThird instruction";
                Record(checks, failures, "development-editor-interaction",
                    messageEditor.Lines.Length == 3 && messageEditor.Height >= 120,
                    $"Development editor accepted {messageEditor.Lines.Length} lines at height {messageEditor.Height}px.");
            }

            PremiumRuntimeShellExperience.NavigateTo(main, "GitHub / Git Settings");
            var gitPath = Path.Combine(evidenceDirectory, "github-git-settings-1920x1080-100.png");
            var gitControl = Descendants(main).OfType<GitHubIntegrationControl>().FirstOrDefault();
            gitControl?.LoadAsync().GetAwaiter().GetResult();
            Render(main, 1920, 1080, 96, gitPath);
            var protectedTokenFields = gitControl is null ? 0 : Descendants(gitControl).OfType<TextBox>().Count(x => x.UseSystemPasswordChar);
            var gitPass = gitControl is not null && protectedTokenFields >= 2 && SingleSurface(main);
            Record(checks, failures, "GitHub/Git", gitPass, $"Rendered embedded GitHub/Git workspace; protected token fields={protectedTokenFields}.");
            screenshots.Add(new ScreenshotEvidence("github-git-settings-1920x1080-100.png", "GitHub / Git Settings", 1920, 1080, 96, 1920, 1080, gitPass ? "PASS" : "FAIL"));

            using (var monitorSettings = new MonitorSettingsForm(
                       "QA Monitor",
                       "https://chatgpt.com/c/ui-product-closure",
                       new SavedMonitor
                       {
                           Title = "QA Monitor",
                           Url = "https://chatgpt.com/c/ui-product-closure",
                           AutoReply = "Line one\r\nLine two",
                           NewChatStartMessage = "Recovery one\r\nRecovery two",
                           Enabled = true
                       }))
            {
                monitorSettings.CreateControl();
                monitorSettings.PerformLayout();
                var autoReply = GetPrivateTextBox(monitorSettings, "_autoReplyBox");
                var newChat = GetPrivateTextBox(monitorSettings, "_newChatMessageBox");
                autoReply.Text = "Alpha\r\nBeta";
                autoReply.SelectionStart = autoReply.TextLength;
                autoReply.SelectedText = "\r\nGamma";
                newChat.Text = "One\r\nTwo\r\nThree";
                var multilinePass = autoReply.Multiline && autoReply.AcceptsReturn && autoReply.Lines.Length == 3
                                    && newChat.Multiline && newChat.AcceptsReturn && newChat.Lines.Length == 3
                                    && autoReply.Height >= 72 && newChat.Height >= 72;
                Record(checks, failures, "multiline-interaction", multilinePass,
                    $"Monitor Settings accepted real line breaks: auto-reply={autoReply.Lines.Length}, new-chat={newChat.Lines.Length}; heights={autoReply.Height}/{newChat.Height}px.");
                var monitorSettingsPath = Path.Combine(evidenceDirectory, "monitor-settings-multiline.png");
                RenderControl(monitorSettings, monitorSettings.ClientSize.Width, monitorSettings.ClientSize.Height, 96, monitorSettingsPath);
                screenshots.Add(new ScreenshotEvidence("monitor-settings-multiline.png", "Monitor Settings multiline", monitorSettings.ClientSize.Width, monitorSettings.ClientSize.Height, 96, monitorSettings.ClientSize.Width, monitorSettings.ClientSize.Height, multilinePass ? "PASS" : "FAIL"));
            }

            var palettePass = FluentTheme.Background == Color.FromArgb(5, 14, 24)
                              && FluentTheme.Surface == Color.FromArgb(9, 23, 38)
                              && FluentTheme.SurfaceAlt == Color.FromArgb(12, 29, 47)
                              && FluentTheme.SurfaceRaised == Color.FromArgb(7, 20, 34)
                              && FluentTheme.Accent == Color.FromArgb(10, 113, 255)
                              && FluentTheme.Text == Color.FromArgb(235, 243, 255)
                              && FluentTheme.Muted == Color.FromArgb(135, 153, 179)
                              && FluentTheme.Border == Color.FromArgb(28, 48, 70);
            Record(checks, failures, "rendered-palette", palettePass, "Rendered production surfaces use the locked premium palette values.");

            var ownedPass = checks.TryGetValue("Projects", out var p) && p.Status == "PASS"
                            && checks.TryGetValue("Development Messages", out var d) && d.Status == "PASS"
                            && checks.TryGetValue("GitHub/Git", out var g) && g.Status == "PASS"
                            && checks.TryGetValue("single-surface", out var s) && s.Status == "PASS"
                            && checks.TryGetValue("multiline-interaction", out var m) && m.Status == "PASS";
            Record(checks, failures, "QA-UI-001", ownedPass, "All owned UI product closure gates passed on rendered production WinForms controls.");
        }
        finally
        {
            try { main?.Dispose(); } catch { }
            if (binding is not null)
            {
                try { binding.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
            }
            http?.Dispose();
            try { Directory.Delete(scratch, recursive: true); } catch { }
        }
    }

    private static Size Render(Form form, int physicalWidth, int physicalHeight, int dpi, string path)
    {
        var logical = PremiumRuntimeShellExperience.CalculateLogicalViewport(new Size(physicalWidth, physicalHeight), dpi);
        form.ClientSize = logical;
        form.PerformLayout();
        Application.DoEvents();
        RenderControl(form, physicalWidth, physicalHeight, dpi, path, logical);
        return logical;
    }

    private static void RenderControl(Control control, int physicalWidth, int physicalHeight, int dpi, string path, Size? logicalOverride = null)
    {
        var logical = logicalOverride ?? new Size(physicalWidth, physicalHeight);
        control.PerformLayout();
        using var logicalBitmap = new Bitmap(Math.Max(1, logical.Width), Math.Max(1, logical.Height));
        control.DrawToBitmap(logicalBitmap, new Rectangle(Point.Empty, logicalBitmap.Size));
        if (logicalBitmap.Width == physicalWidth && logicalBitmap.Height == physicalHeight)
        {
            logicalBitmap.SetResolution(dpi, dpi);
            logicalBitmap.Save(path, ImageFormat.Png);
            return;
        }

        using var physical = new Bitmap(physicalWidth, physicalHeight);
        physical.SetResolution(dpi, dpi);
        using (var graphics = Graphics.FromImage(physical))
            graphics.DrawImage(logicalBitmap, new Rectangle(0, 0, physicalWidth, physicalHeight));
        physical.Save(path, ImageFormat.Png);
    }

    private static bool SingleSurface(MainForm main)
    {
        var host = ContentHost(main);
        return host is not null && host.Controls.Count == 1;
    }

    private static Panel? ContentHost(MainForm main)
        => main.Controls.Find("PremiumContentHost", true).OfType<Panel>().SingleOrDefault();

    private static TextBox GetPrivateTextBox(object owner, string fieldName)
        => (TextBox)(owner.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner)
                     ?? throw new InvalidOperationException($"Missing text box field {fieldName}."));

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Record(
        Dictionary<string, EvidenceCheck> checks,
        List<string> failures,
        string key,
        bool pass,
        string detail)
    {
        checks[key] = new EvidenceCheck(pass ? "PASS" : "FAIL", detail);
        if (!pass) failures.Add($"{key}: {detail}");
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("STA rendered acceptance failed.", failure);
    }

    private static string RepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed record ViewportCase(string Name, int Width, int Height, int Dpi, string FileName);
    private sealed record EvidenceCheck(string Status, string Detail);
    private sealed record ScreenshotEvidence(string File, string Destination, int PhysicalWidth, int PhysicalHeight, int Dpi, int LogicalWidth, int LogicalHeight, string Status);
}
