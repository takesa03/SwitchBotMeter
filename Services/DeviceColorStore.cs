using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SwitchBotMeter.Services;

// デバイスごとのグラフ表示色（パレットのインデックス）を固定保存する
public class DeviceColorStore
{
    private readonly string filePath;
    private Dictionary<ulong, int> colors = new();

    public DeviceColorStore(string baseDirectory)
    {
        filePath = Path.Combine(baseDirectory, "device_colors.json");
        Load();
    }

    private void Load()
    {
        if (!File.Exists(filePath)) return;

        try
        {
            var json = File.ReadAllText(filePath);
            colors = JsonSerializer.Deserialize<Dictionary<ulong, int>>(json) ?? new();
        }
        catch
        {
            colors = new();
        }
    }

    public int? GetPaletteIndex(ulong address) => colors.TryGetValue(address, out var index) ? index : null;

    public void SetPaletteIndex(ulong address, int index)
    {
        colors[address] = index;
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(filePath, JsonSerializer.Serialize(colors, options));
    }
}
