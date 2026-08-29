using System;
using System.IO;
using System.Text.Json;

namespace SwitchBotMeter.Services;

public class MonitorSettings
{
    public string OutputFilePath { get; set; } = "C:/Users/takes/Videos/obs/tempture.txt";
    public ulong? MonitoredDeviceAddress { get; set; }
}

public class MonitorSettingsStore
{
    private readonly string filePath;

    public MonitorSettingsStore()
    {
        filePath = Path.Combine(AppPaths.SettingsDirectory, "monitor_settings.json");
    }

    public MonitorSettings Load()
    {
        if (!File.Exists(filePath)) return new MonitorSettings();

        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<MonitorSettings>(json) ?? new MonitorSettings();
        }
        catch
        {
            return new MonitorSettings();
        }
    }

    public void Save(MonitorSettings settings)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(filePath, JsonSerializer.Serialize(settings, options));
    }
}
