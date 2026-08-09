using System.Text.Json;

namespace GPTDeskTop.Services.DevelopmentTaskEngine;

public sealed class DevelopmentTaskScheduleSettings
{
    public int WorkMinutes { get; set; } = 10;
    public int CoolingMinutes { get; set; } = 5;

    public void Validate()
    {
        if (WorkMinutes < 1 || WorkMinutes > 120)
            throw new InvalidOperationException("Work window must be between 1 and 120 minutes.");
        if (CoolingMinutes < 1 || CoolingMinutes > 120)
            throw new InvalidOperationException("Cooling window must be between 1 and 120 minutes.");
    }
}

public sealed class DevelopmentTaskScheduleSettingsStore
{
    private readonly string _path;

    public DevelopmentTaskScheduleSettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppContext.BaseDirectory, "data", "development-task-settings.json");
    }

    public DevelopmentTaskScheduleSettings Load()
    {
        if (!File.Exists(_path)) return new DevelopmentTaskScheduleSettings();
        var settings = JsonSerializer.Deserialize<DevelopmentTaskScheduleSettings>(File.ReadAllText(_path))
            ?? new DevelopmentTaskScheduleSettings();
        settings.Validate();
        return settings;
    }

    public void Save(DevelopmentTaskScheduleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _path, true);
    }
}
