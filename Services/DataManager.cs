using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SwitchBotMeter.Models;

namespace SwitchBotMeter.Services;

public class DataManager
{
    private string dataDirectory;
    private string csvPath;
    private string jsonPath;

    public DataManager(string basePath = "./data")
    {
        dataDirectory = basePath;
        csvPath = Path.Combine(dataDirectory, "temperature_humidity.csv");
        jsonPath = Path.Combine(dataDirectory, "temperature_humidity.json");

        if (!Directory.Exists(dataDirectory))
        {
            Directory.CreateDirectory(dataDirectory);
        }
    }

    public void SaveRecord(TemperatureHumidityRecord record)
    {
        SaveToCSV(record);
        SaveToJSON(record);
    }

    private void SaveToCSV(TemperatureHumidityRecord record)
    {
        bool fileExists = File.Exists(csvPath);

        using (var writer = new StreamWriter(csvPath, append: true))
        {
            if (!fileExists)
            {
                writer.WriteLine("Timestamp,BluetoothAddress,Temperature,Humidity,Battery,DeviceName");
            }

            writer.WriteLine($"{record.Timestamp:yyyy-MM-dd HH:mm:ss}," +
                $"{record.BluetoothAddress:X}," +
                $"{record.Temperature}," +
                $"{record.Humidity}," +
                $"{record.Battery}," +
                $"{record.DeviceName}");
        }
    }

    private void SaveToJSON(TemperatureHumidityRecord record)
    {
        List<TemperatureHumidityRecord> records = new();

        if (File.Exists(jsonPath))
        {
            try
            {
                string json = File.ReadAllText(jsonPath);
                records = JsonSerializer.Deserialize<List<TemperatureHumidityRecord>>(json) ?? new();
            }
            catch
            {
                records = new();
            }
        }

        records.Add(record);

        var options = new JsonSerializerOptions { WriteIndented = true };
        string jsonString = JsonSerializer.Serialize(records, options);
        File.WriteAllText(jsonPath, jsonString);
    }

    public List<TemperatureHumidityRecord> LoadRecords(ulong? bluetoothAddress = null)
    {
        if (!File.Exists(jsonPath))
            return new();

        try
        {
            string json = File.ReadAllText(jsonPath);
            var records = JsonSerializer.Deserialize<List<TemperatureHumidityRecord>>(json) ?? new();

            if (bluetoothAddress.HasValue)
            {
                records = records.Where(r => r.BluetoothAddress == bluetoothAddress.Value).ToList();
            }

            return records;
        }
        catch
        {
            return new();
        }
    }

    public void ExportToFile(string filePath, List<TemperatureHumidityRecord> records)
    {
        if (filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(records, options);
            File.WriteAllText(filePath, json);
        }
        else
        {
            using (var writer = new StreamWriter(filePath))
            {
                writer.WriteLine("Timestamp,BluetoothAddress,Temperature,Humidity,Battery,DeviceName");
                foreach (var record in records)
                {
                    writer.WriteLine($"{record.Timestamp:yyyy-MM-dd HH:mm:ss}," +
                        $"{record.BluetoothAddress:X}," +
                        $"{record.Temperature}," +
                        $"{record.Humidity}," +
                        $"{record.Battery}," +
                        $"{record.DeviceName}");
                }
            }
        }
    }
}
