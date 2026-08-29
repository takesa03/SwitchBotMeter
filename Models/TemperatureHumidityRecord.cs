using System;

namespace SwitchBotMeter.Models;

public class TemperatureHumidityRecord
{
    public ulong BluetoothAddress { get; set; }
    public DateTime Timestamp { get; set; }
    public double Temperature { get; set; }
    public int Humidity { get; set; }
    public int Battery { get; set; }
    public string DeviceName { get; set; } = "";
}
