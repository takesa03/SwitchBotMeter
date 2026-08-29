using System;
using System.IO;
using System.Text.Json;

namespace SwitchBotMeter.Services;

public class GraphAxisSettings
{
    public double TemperatureMin { get; set; } = 0;
    public double TemperatureMax { get; set; } = 40;
    public double HumidityMin { get; set; } = 10;
    public double HumidityMax { get; set; } = 90;
}

// 温度・湿度グラフの上限/下限設定を保存する
public class GraphAxisSettingsStore
{
    private readonly string filePath;

    public GraphAxisSettingsStore()
    {
        filePath = Path.Combine(AppPaths.SettingsDirectory, "graph_axis_settings.json");
    }

    public GraphAxisSettings Load()
    {
        if (!File.Exists(filePath)) return new GraphAxisSettings();

        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<GraphAxisSettings>(json) ?? new GraphAxisSettings();
        }
        catch
        {
            return new GraphAxisSettings();
        }
    }

    public void Save(GraphAxisSettings settings)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(filePath, JsonSerializer.Serialize(settings, options));
    }
}
