using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace GPTDeskTop.Setup;

internal static class Program
{
    internal const string AppName = "GPTDeskTop";
    internal const string Version = "2.0.3";
    private static readonly string[] RequiredPayloadResources =
    [
        "Payload.GPTDeskTop.exe",
        "Payload.appsettings.json"
    ];

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--verify-payload", StringComparison.OrdinalIgnoreCase)))
            return VerifyPayloadResources(out _) ? 0 : 23;

        if (args.Any(a => string.Equals(a, "--verify-wizard", StringComparison.OrdinalIgnoreCase)))
            return InstallerWizardForm.VerifyWizardContract() ? 0 : 24;

        ApplicationConfiguration.Initialize();
        try
        {
            if (args.Any(a => string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase)))
            {
                Uninstall();
                return 0;
            }

            if (!VerifyPayloadResources(out var payloadError))
                throw new InvalidOperationException(payloadError);

            using var wizard = new InstallerWizardForm();
            Application.Run(wizard);
            return wizard.InstallationFailed ? 1 : 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), $"{AppName} Setup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static bool VerifyPayloadResources(out string error)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var names = assembly.GetManifestResourceNames().ToHashSet(StringComparer.Ordinal);
        foreach (var resourceName in RequiredPayloadResources)
        {
            if (!names.Contains(resourceName))
            {
                error = $"Installer payload resource is missing: {resourceName}";
                return false;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null || stream.Length <= 0)
            {
                error = $"Installer payload resource is empty or unreadable: {resourceName}";
                return false;
            }
        }

        using var executable = assembly.GetManifestResourceStream("Payload.GPTDeskTop.exe");
        if (executable is null || executable.Length < 1024 * 1024)
        {
            error = "Installer application payload is unexpectedly small; refusing to install.";
            return false;
        }

        if (!VerifyWindowsX64Pe(executable, out error))
            return false;

        error = string.Empty;
        return true;
    }

    private static bool VerifyWindowsX64Pe(Stream executable, out string error)
    {
        try
        {
            if (!executable.CanSeek || executable.Length < 256)
            {
                error = "Installer application payload is not a seekable PE image.";
                return false;
            }

            using var reader = new BinaryReader(executable, System.Text.Encoding.UTF8, leaveOpen: true);
            executable.Position = 0;
            if (reader.ReadUInt16() != 0x5A4D)
            {
                error = "Installer application payload does not begin with the DOS MZ signature.";
                return false;
            }

            executable.Position = 0x3C;
            var peOffset = reader.ReadInt32();
            if (peOffset < 0x40 || peOffset + 26 >= executable.Length)
            {
                error = "Installer application payload contains an invalid PE header offset.";
                return false;
            }

            executable.Position = peOffset;
            if (reader.ReadUInt32() != 0x00004550)
            {
                error = "Installer application payload does not contain a PE signature.";
                return false;
            }

            if (reader.ReadUInt16() != 0x8664)
            {
                error = "Installer application payload is not AMD64/x64.";
                return false;
            }

            executable.Position = peOffset + 24;
            if (reader.ReadUInt16() != 0x020B)
            {
                error = "Installer application payload is not PE32+ / 64-bit.";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or ArgumentOutOfRangeException)
        {
            error = $"Installer application payload PE validation failed: {ex.Message}";
            return false;
        }
    }

    internal static void Install(bool createDesktopShortcut)
    {
        var installDir = GetInstallDirectory();
        Directory.CreateDirectory(installDir);
        StopRunningApplication();

        ExtractRequiredResource("Payload.GPTDeskTop.exe", Path.Combine(installDir, "GPTDeskTop.exe"), true);
        ExtractRequiredResource("Payload.appsettings.json", Path.Combine(installDir, "appsettings.json"), false);
        ExtractOptionalResource("Payload.Version.txt", Path.Combine(installDir, "Version.txt"), true);
        ExtractOptionalResource("Payload.ReleaseNotes.txt", Path.Combine(installDir, "ReleaseNotes.txt"), true);

        var setupCopy = Path.Combine(installDir, "GPTDeskTop-Setup.exe");
        File.Copy(Environment.ProcessPath ?? Application.ExecutablePath, setupCopy, true);

        var desktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "GPTDeskTop.lnk");
        if (createDesktopShortcut)
            CreateShortcut(desktopShortcut, Path.Combine(installDir, "GPTDeskTop.exe"), installDir);
        else
            TryDelete(desktopShortcut);

        var startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "GPTDeskTop");
        Directory.CreateDirectory(startMenuDir);
        CreateShortcut(Path.Combine(startMenuDir, "GPTDeskTop.lnk"), Path.Combine(installDir, "GPTDeskTop.exe"), installDir);
        CreateShortcut(Path.Combine(startMenuDir, "Uninstall GPTDeskTop.lnk"), setupCopy, installDir, "/uninstall");
        RegisterUninstall(setupCopy, installDir);
    }

    internal static void LaunchInstalledApplication()
    {
        try
        {
            var installDir = GetInstallDirectory();
            var executable = Path.Combine(installDir, "GPTDeskTop.exe");
            if (!File.Exists(executable)) return;
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = installDir,
                UseShellExecute = true
            });
        }
        catch
        {
            // Installation succeeded even if the optional post-install launch is blocked.
        }
    }

    private static void Uninstall()
    {
        if (MessageBox.Show("Remove GPTDeskTop from this computer?\n\nSaved database files will be preserved unless you delete them manually.", "Uninstall GPTDeskTop", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        StopRunningApplication();
        var installDir = GetInstallDirectory();
        TryDelete(Path.Combine(installDir, "GPTDeskTop.exe"));
        TryDelete(Path.Combine(installDir, "appsettings.json"));
        TryDelete(Path.Combine(installDir, "Version.txt"));
        TryDelete(Path.Combine(installDir, "ReleaseNotes.txt"));
        TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "GPTDeskTop.lnk"));

        var startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "GPTDeskTop");
        if (Directory.Exists(startMenuDir))
        {
            foreach (var file in Directory.GetFiles(startMenuDir)) TryDelete(file);
            TryDeleteDirectory(startMenuDir);
        }

        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", writable: true))
            key?.DeleteSubKeyTree("GPTDeskTop", false);

        MessageBox.Show("GPTDeskTop was removed. Local database data was preserved.", "Uninstall GPTDeskTop", MessageBoxButtons.OK, MessageBoxIcon.Information);
        var self = Environment.ProcessPath ?? Application.ExecutablePath;
        if (self.StartsWith(installDir, StringComparison.OrdinalIgnoreCase))
        {
            var cmd = $"/c ping 127.0.0.1 -n 3 > nul & del /f /q \"{self}\" & rmdir /q \"{installDir}\"";
            Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false });
        }
    }

    private static void StopRunningApplication()
    {
        foreach (var process in Process.GetProcessesByName("GPTDeskTop"))
        {
            try
            {
                process.CloseMainWindow();
                if (!process.WaitForExit(2500)) process.Kill(entireProcessTree: true);
            }
            catch { }
        }
    }

    private static void RegisterUninstall(string setupCopy, string installDir)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\GPTDeskTop");
        key.SetValue("DisplayName", "GPTDeskTop");
        key.SetValue("DisplayVersion", Version);
        key.SetValue("Publisher", "GPTDeskTop");
        key.SetValue("InstallLocation", installDir);
        key.SetValue("DisplayIcon", Path.Combine(installDir, "GPTDeskTop.exe"));
        key.SetValue("UninstallString", $"\"{setupCopy}\" /uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static void ExtractRequiredResource(string resourceName, string destination, bool overwrite)
    {
        if (!ExtractResource(resourceName, destination, overwrite))
            throw new InvalidOperationException($"Installer payload resource is missing: {resourceName}");
    }

    private static void ExtractOptionalResource(string resourceName, string destination, bool overwrite) => ExtractResource(resourceName, destination, overwrite);

    private static bool ExtractResource(string resourceName, string destination, bool overwrite)
    {
        if (!overwrite && File.Exists(destination)) return true;
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var file = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.CopyTo(file);
        return true;
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory, string arguments = "")
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host is not available; shortcut creation failed.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = workingDirectory;
        shortcut.Arguments = arguments;
        shortcut.IconLocation = targetPath + ",0";
        shortcut.Save();
        Marshal.FinalReleaseComObject(shortcut);
        Marshal.FinalReleaseComObject(shell);
    }

    internal static string GetInstallDirectory() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "GPTDeskTop");

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, false); } catch { }
    }
}
