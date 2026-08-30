using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace GPTDeskTop.Setup;

internal static class QaLifecycleModule
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var qaInstall = args.Any(a => string.Equals(a, "--qa-install", StringComparison.OrdinalIgnoreCase));
        var qaUninstall = args.Any(a => string.Equals(a, "--qa-uninstall", StringComparison.OrdinalIgnoreCase));
        if (!qaInstall && !qaUninstall)
            return;

        try
        {
            if (qaInstall)
            {
                Program.Install(createDesktopShortcut: false);
            }
            else
            {
                QaUninstall();
            }

            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Environment.Exit(91);
        }
    }

    private static void QaUninstall()
    {
        StopRunningApplication();
        var installDir = Program.GetInstallDirectory();

        TryDelete(Path.Combine(installDir, "GPTDeskTop.exe"));
        TryDelete(Path.Combine(installDir, "appsettings.json"));
        TryDelete(Path.Combine(installDir, "Version.txt"));
        TryDelete(Path.Combine(installDir, "ReleaseNotes.txt"));
        TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "GPTDeskTop.lnk"));

        var startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "GPTDeskTop");
        if (Directory.Exists(startMenuDir))
        {
            foreach (var file in Directory.GetFiles(startMenuDir))
                TryDelete(file);
            TryDeleteDirectory(startMenuDir);
        }

        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", writable: true))
            key?.DeleteSubKeyTree("GPTDeskTop", false);

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
                if (!process.WaitForExit(2500))
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, false);
        }
        catch
        {
        }
    }
}
