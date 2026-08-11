namespace GPTDeskTop.RuntimeTests;

public sealed class MainDashboardExperienceStabilityTests
{
    [Fact]
    public void DashboardPresentation_IsOneTimePerForm()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MainDashboardExperience.cs");
        Assert.Contains("if (registration.Initialized) return;", source, StringComparison.Ordinal);
        Assert.Contains("form.Resize += (_, _) => ApplyResponsiveLayout(form);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardResponsiveRules_AreScopedToDashboardActionGroups()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MainDashboardExperience.cs");
        Assert.Contains("IsDashboardActionGroup(group)", source, StringComparison.Ordinal);
        Assert.Contains("actionRow.WrapContents = false;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("buttons.Count >= 4", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DevelopmentPlan_PreservesSingleRowRule()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "SecondaryScreenExperience.cs");
        Assert.Contains("actions.WrapContents = false", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(parts)}");
    }
}
