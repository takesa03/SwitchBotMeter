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

    public DeviceHistoryStore(string baseDirectory)
    {
        directory = Path.Combine(baseDirectory, "history");
        Directory.CreateDirectory(directory);
    }

    public string HistoryDirectory => directory;

    private string GetFilePath(ulong address) => Path.Combine(directory, $"{address:X}.csv");

    // 起動直後（スキャン開始前）にも過去データを表示できるよう、
    // 履歴ファイルが存在する既知デバイスのアドレス一覧を返す
    public List<ulong> GetKnownAddresses()
    {
        var result = new List<ulong>();
        foreach (var path in Directory.GetFiles(directory, "*.csv"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (ulong.TryParse(name, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address))
            {
                result.Add(address);
            }
        }
        return result;
    }

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
        // 秒未満とタイムゾーンオフセットは記録しない（日本標準時前提）
        var timestamp = record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var line = $"{timestamp},{record.Temperature.ToString(CultureInfo.InvariantCulture)},{record.Humidity}";
        File.AppendAllText(GetFilePath(address), line + Environment.NewLine);
    }
}
