using System;
using System.IO;
using System.Text.Json;

namespace SwitchBotMeter.Services;

public class WindowSettings
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool IsMaximized { get; set; }
}

public class WindowSettingsStore
{
    private readonly string filePath;

    public WindowSettingsStore()
    {
        filePath = Path.Combine(AppPaths.SettingsDirectory, "window_settings.json");
    }

    public WindowSettings? Load()
    {
        if (!File.Exists(filePath)) return null;

        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<WindowSettings>(json);
        }
        catch
        {
            return null;
        }
    }

    public void Save(WindowSettings settings)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(filePath, JsonSerializer.Serialize(settings, options));
    }
}
