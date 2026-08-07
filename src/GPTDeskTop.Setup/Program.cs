using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace GPTDeskTop.Setup;

internal static class Program
{
    private const string AppName = "GPTDeskTop";
    private const string Version = "1.7.0";

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            if (args.Any(a => string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase))) { Uninstall(); return; }
            if (MessageBox.Show($"Install {AppName} v{Version}?\n\nThe application will be installed for the current Windows user.", $"{AppName} Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Install();
            MessageBox.Show($"{AppName} v{Version} was installed successfully.\n\nA desktop and Start Menu shortcut were created.", $"{AppName} Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(ex.ToString(), $"{AppName} Setup Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static void Install()
    {
        var installDir = GetInstallDirectory(); Directory.CreateDirectory(installDir); StopRunningApplication();
        ExtractRequiredResource("Payload.GPTDeskTop.exe", Path.Combine(installDir, "GPTDeskTop.exe"), true);
        ExtractOptionalResource("Payload.appsettings.json", Path.Combine(installDir, "appsettings.json"), false);
        ExtractOptionalResource("Payload.Version.txt", Path.Combine(installDir, "Version.txt"), true);
        ExtractOptionalResource("Payload.ReleaseNotes.txt", Path.Combine(installDir, "ReleaseNotes.txt"), true);
        var setupCopy = Path.Combine(installDir, "GPTDeskTop-Setup.exe"); File.Copy(Environment.ProcessPath ?? Application.ExecutablePath, setupCopy, true);
        CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "GPTDeskTop.lnk"), Path.Combine(installDir, "GPTDeskTop.exe"), installDir);
        var startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "GPTDeskTop"); Directory.CreateDirectory(startMenuDir);
        CreateShortcut(Path.Combine(startMenuDir, "GPTDeskTop.lnk"), Path.Combine(installDir, "GPTDeskTop.exe"), installDir);
        CreateShortcut(Path.Combine(startMenuDir, "Uninstall GPTDeskTop.lnk"), setupCopy, installDir, "/uninstall");
        RegisterUninstall(setupCopy, installDir);
    }

    private static void Uninstall()
    {
        if (MessageBox.Show("Remove GPTDeskTop from this computer?\n\nSaved database files will be preserved unless you delete them manually.", "Uninstall GPTDeskTop", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        StopRunningApplication();
        var installDir = GetInstallDirectory();
        TryDelete(Path.Combine(installDir, "GPTDeskTop.exe")); TryDelete(Path.Combine(installDir, "appsettings.json")); TryDelete(Path.Combine(installDir, "Version.txt")); TryDelete(Path.Combine(installDir, "ReleaseNotes.txt"));
        TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "GPTDeskTop.lnk"));
        var startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "GPTDeskTop");
        if (Directory.Exists(startMenuDir)) { foreach (var file in Directory.GetFiles(startMenuDir)) TryDelete(file); TryDeleteDirectory(startMenuDir); }
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", writable: true)) key?.DeleteSubKeyTree("GPTDeskTop", false);
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
            try { process.CloseMainWindow(); if (!process.WaitForExit(2500)) process.Kill(entireProcessTree: true); } catch { }
        }
    }

    private static void RegisterUninstall(string setupCopy, string installDir)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\GPTDeskTop");
        key.SetValue("DisplayName", "GPTDeskTop"); key.SetValue("DisplayVersion", Version); key.SetValue("Publisher", "GPTDeskTop"); key.SetValue("InstallLocation", installDir);
        key.SetValue("DisplayIcon", Path.Combine(installDir, "GPTDeskTop.exe")); key.SetValue("UninstallString", $"\"{setupCopy}\" /uninstall"); key.SetValue("NoModify", 1, RegistryValueKind.DWord); key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static void ExtractRequiredResource(string resourceName, string destination, bool overwrite)
    { if (!ExtractResource(resourceName, destination, overwrite)) throw new InvalidOperationException($"Installer payload resource is missing: {resourceName}"); }
    private static void ExtractOptionalResource(string resourceName, string destination, bool overwrite) => ExtractResource(resourceName, destination, overwrite);

    private static bool ExtractResource(string resourceName, string destination, bool overwrite)
    {
        if (!overwrite && File.Exists(destination)) return true;
        var assembly = Assembly.GetExecutingAssembly(); using var stream = assembly.GetManifestResourceStream(resourceName); if (stream is null) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!); using var file = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None); stream.CopyTo(file); return true;
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory, string arguments = "")
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host is not available; shortcut creation failed.");
        dynamic shell = Activator.CreateInstance(shellType)!; dynamic shortcut = shell.CreateShortcut(shortcutPath); shortcut.TargetPath = targetPath; shortcut.WorkingDirectory = workingDirectory; shortcut.Arguments = arguments; shortcut.IconLocation = targetPath + ",0"; shortcut.Save();
        Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell);
    }

    private static string GetInstallDirectory() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "GPTDeskTop");
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, false); } catch { } }
}
