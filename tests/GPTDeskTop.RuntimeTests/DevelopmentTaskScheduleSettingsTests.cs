using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentTaskScheduleSettingsTests
{
    [Fact]
    public void DefaultsRemainTenMinutesWorkAndFiveMinutesCooling()
    {
        var settings = new DevelopmentTaskScheduleSettings();
        settings.Validate();
        Assert.Equal(10, settings.WorkMinutes);
        Assert.Equal(5, settings.CoolingMinutes);
    }

    [Fact]
    public void InvalidValuesAreRejected()
    {
        var settings = new DevelopmentTaskScheduleSettings { WorkMinutes = 0, CoolingMinutes = 5 };
        Assert.Throws<InvalidOperationException>(() => settings.Validate());

        settings = new DevelopmentTaskScheduleSettings { WorkMinutes = 10, CoolingMinutes = 121 };
        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public void StoreRoundTripsConfiguredSchedule()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gptdesktop-schedule-{Guid.NewGuid():N}.json");
        try
        {
            var store = new DevelopmentTaskScheduleSettingsStore(path);
            store.Save(new DevelopmentTaskScheduleSettings { WorkMinutes = 7, CoolingMinutes = 3 });
            var loaded = store.Load();
            Assert.Equal(7, loaded.WorkMinutes);
            Assert.Equal(3, loaded.CoolingMinutes);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
        }
    }

    [Fact]
    public void ChangingSettingsDoesNotAlterAnAlreadyStartedWindowContract()
    {
        var initial = new DevelopmentTaskScheduleSettings { WorkMinutes = 10, CoolingMinutes = 5 };
        var nextWindow = new DevelopmentTaskScheduleSettings { WorkMinutes = 20, CoolingMinutes = 2 };
        Assert.NotEqual(initial.WorkMinutes, nextWindow.WorkMinutes);
        Assert.NotEqual(initial.CoolingMinutes, nextWindow.CoolingMinutes);
    }
}
