namespace GPTDeskTop.RuntimeTests;

public sealed class SetupWizardRegressionTests
{
    private static string ReadSetupSource(string fileName)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GPTDeskTop.Setup", fileName));
        return File.ReadAllText(path);
    }

    [Fact]
    public void StandaloneSetupUsesInteractiveWizardInsteadOfSingleConfirmationMessage()
    {
        var program = ReadSetupSource("Program.cs");
        var wizard = ReadSetupSource("InstallerWizardForm.cs");

        Assert.Contains("using var wizard = new InstallerWizardForm();", program, StringComparison.Ordinal);
        Assert.Contains("Application.Run(wizard);", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Install {AppName} v{Version}?", program, StringComparison.Ordinal);
        Assert.Contains("internal sealed class InstallerWizardForm : Form", wizard, StringComparison.Ordinal);
        Assert.Contains("Welcome to the GPTDeskTop Setup Wizard", wizard, StringComparison.Ordinal);
        Assert.Contains("Installation options", wizard, StringComparison.Ordinal);
        Assert.Contains("Ready to install", wizard, StringComparison.Ordinal);
        Assert.Contains("Installing GPTDeskTop", wizard, StringComparison.Ordinal);
        Assert.Contains("Setup complete", wizard, StringComparison.Ordinal);
        Assert.Contains("_next.Text = \"Install\";", wizard, StringComparison.Ordinal);
        Assert.Contains("_next.Text = \"Finish\";", wizard, StringComparison.Ordinal);
    }

    [Fact]
    public void SetupWizardContractIsMachineVerifiableBeforeRelease()
    {
        var program = ReadSetupSource("Program.cs");
        var wizard = ReadSetupSource("InstallerWizardForm.cs");

        Assert.Contains("--verify-wizard", program, StringComparison.Ordinal);
        Assert.Contains("InstallerWizardForm.VerifyWizardContract()", program, StringComparison.Ordinal);
        Assert.Contains("WizardContractVersion = 1", wizard, StringComparison.Ordinal);
        Assert.Contains("RequiredWizardStages", wizard, StringComparison.Ordinal);
        Assert.Contains("[\"Welcome\", \"Options\", \"Ready\", \"Installing\", \"Complete\"]", wizard, StringComparison.Ordinal);
    }

    [Fact]
    public void WizardRunsRealInstallerAndCanLaunchInstalledApplication()
    {
        var program = ReadSetupSource("Program.cs");
        var wizard = ReadSetupSource("InstallerWizardForm.cs");

        Assert.Contains("Program.Install(createDesktopShortcut);", wizard, StringComparison.Ordinal);
        Assert.Contains("Program.LaunchInstalledApplication();", wizard, StringComparison.Ordinal);
        Assert.Contains("CreateShortcut", program, StringComparison.Ordinal);
        Assert.Contains("RegisterUninstall", program, StringComparison.Ordinal);
        Assert.Contains("GPTDeskTop-Setup.exe", program, StringComparison.Ordinal);
        Assert.Contains("/uninstall", program, StringComparison.Ordinal);
    }
}
