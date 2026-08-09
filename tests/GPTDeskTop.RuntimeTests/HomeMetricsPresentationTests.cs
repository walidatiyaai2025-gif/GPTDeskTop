using System.Drawing;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class HomeMetricsPresentationTests
{
    [Fact]
    public void RunningStatusUsesGreenBoldLamp()
    {
        var presentation = HomeMetricsPresentation.GetStatus("Running");

        Assert.NotNull(presentation);
        Assert.Equal("● Running", presentation!.Text);
        Assert.Equal(Color.SeaGreen, presentation.ForeColor);
        Assert.Equal(FontStyle.Bold, presentation.FontStyle);
    }

    [Fact]
    public void StoppedStatusUsesRedBoldLamp()
    {
        var presentation = HomeMetricsPresentation.GetStatus("Stopped");

        Assert.NotNull(presentation);
        Assert.Equal("● Stopped", presentation!.Text);
        Assert.Equal(Color.Firebrick, presentation.ForeColor);
        Assert.Equal(FontStyle.Bold, presentation.FontStyle);
    }

    [Fact]
    public void UnknownStatusIsNotReformatted()
    {
        Assert.Null(HomeMetricsPresentation.GetStatus("Waiting"));
        Assert.Null(HomeMetricsPresentation.GetStatus(null));
    }

    [Fact]
    public void SnapshotShowsPersistentCrashCountAndLiveOverTotalMonitors()
    {
        var monitors = new List<SavedMonitor>
        {
            new() { Id = 11, Title = "one" },
            new() { Id = 12, Title = "two" },
            new() { Id = 13, Title = "three" }
        };

        var snapshot = HomeMetricsPresentation.BuildSnapshot(
            7,
            monitors,
            id => id is 11 or 13);

        Assert.Equal("Crashes\r\n7", snapshot.CrashCardText);
        Assert.Equal("Monitors\r\n2 / 3", snapshot.MonitorCardText);
    }

    [Fact]
    public void NegativeCrashCountCannotLeakIntoCard()
    {
        var snapshot = HomeMetricsPresentation.BuildSnapshot(
            -5,
            Array.Empty<SavedMonitor>(),
            _ => false);

        Assert.Equal("Crashes\r\n0", snapshot.CrashCardText);
        Assert.Equal("Monitors\r\n0 / 0", snapshot.MonitorCardText);
    }
}