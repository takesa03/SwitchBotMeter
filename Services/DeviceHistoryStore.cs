using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SwitchBotMeter.Models;

namespace SwitchBotMeter.Services;

// 間引き後の温湿度履歴をデバイスごとのCSVファイルに永続化する。
// グラフはこのファイルの内容（起動時ロード＋以後の追記）から生成される。
public class DeviceHistoryStore
{
    private readonly string directory;

    public DeviceHistoryStore()
    {
        directory = Path.Combine(AppPaths.SettingsDirectory, "history");
        Directory.CreateDirectory(directory);
    }

    private string GetFilePath(ulong address) => Path.Combine(directory, $"{address:X}.csv");

    public List<TemperatureHumidityRecord> Load(ulong address, string deviceType)
    {
        var list = new List<TemperatureHumidityRecord>();
        var path = GetFilePath(address);
        if (!File.Exists(path)) return list;

        foreach (var line in File.ReadLines(path))
        {
            var parts = line.Split(',');
            if (parts.Length < 3) continue;
            if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp)) continue;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var temperature)) continue;
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var humidity)) continue;

            list.Add(new TemperatureHumidityRecord
            {
                BluetoothAddress = address,
                Timestamp = timestamp,
                Temperature = temperature,
                Humidity = humidity,
                DeviceName = deviceType
            });
        }

        return list;
    }

    public void Append(ulong address, TemperatureHumidityRecord record)
    {
        var line = $"{record.Timestamp:O},{record.Temperature.ToString(CultureInfo.InvariantCulture)},{record.Humidity}";
        File.AppendAllText(GetFilePath(address), line + Environment.NewLine);
    }
}
