using System;
using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SwitchBotMeter.Models;

public partial class Device : ObservableObject
{
    // グラフ表示用の温湿度履歴（スキャン中に取得したタイミングでのみ記録、補完なし）
    public ObservableCollection<TemperatureHumidityRecord> History { get; } = new();

    [ObservableProperty]
    private bool showOnGraph;

    // グラフの系列色と一覧の文字色を一致させるため、デバイスごとに固定色を割り当てる
    [ObservableProperty]
    private Brush graphColorBrush = Brushes.White;

    public int PaletteIndex { get; set; }

    [ObservableProperty]
    private ulong bluetoothAddress;

    [ObservableProperty]
    private string name = "";

    [ObservableProperty]
    private string alias = "";

    [ObservableProperty]
    private string deviceType = "";

    [ObservableProperty]
    private DateTime firstSeen;

    [ObservableProperty]
    private DateTime lastSeen;

    [ObservableProperty]
    private bool isMonitored;

    [ObservableProperty]
    private double? lastTemperature;

    [ObservableProperty]
    private int? lastHumidity;

    [ObservableProperty]
    private int? lastBattery;

    public string DisplayName =>
        string.IsNullOrWhiteSpace(Alias) ? $"{BluetoothAddress:X} ({DeviceType})" : $"{Alias} ({BluetoothAddress:X})";

    public override string ToString() => DisplayName;

    partial void OnAliasChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    partial void OnDeviceTypeChanged(string value) => OnPropertyChanged(nameof(DisplayName));
}
