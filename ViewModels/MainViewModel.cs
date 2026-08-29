using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using SkiaSharp;
using SwitchBotMeter.Models;
using SwitchBotMeter.Services;

namespace SwitchBotMeter.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly BluetoothLEManager bluetoothManager = new();
    private readonly DeviceAliasStore aliasStore = new();
    private readonly MonitorSettingsStore monitorSettingsStore = new();
    private readonly DeviceHistoryStore historyStore = new();
    private readonly DeviceColorStore colorStore = new();

    // 間引き用: デバイスごとに直近1分以内のデータは記録しない
    private static readonly TimeSpan HistoryMinInterval = TimeSpan.FromMinutes(1);

    [ObservableProperty]
    private ObservableCollection<Device> devices = new();

    [ObservableProperty]
    private Device selectedDevice;

    private Device? aliasTrackedDevice;

    [ObservableProperty]
    private bool isScanning;

    [ObservableProperty]
    private GraphTimeRange selectedTimeRange = GraphTimeRange.ThirtyMinutes;

    [ObservableProperty]
    private ISeries[] series = Array.Empty<ISeries>();

    [ObservableProperty]
    private Axis[] xAxes = Array.Empty<Axis>();

    [ObservableProperty]
    private Axis[] yAxes = Array.Empty<Axis>();

    [ObservableProperty]
    private TimeSpan graphAnimationsSpeed = TimeSpan.FromMilliseconds(300);

    private bool isFirstGraphRender = true;

    // グラフの系列色とデバイス一覧の文字色を一致させるため、SkiaSharp用とWPF用を対で管理する
    private static readonly (SKColor Sk, Color Wpf)[] DevicePalette =
    {
        (SKColors.DodgerBlue, Color.FromRgb(0x1E, 0x90, 0xFF)),
        (SKColors.OrangeRed, Color.FromRgb(0xFF, 0x45, 0x00)),
        (SKColors.LimeGreen, Color.FromRgb(0x32, 0xCD, 0x32)),
        (SKColors.Gold, Color.FromRgb(0xFF, 0xD7, 0x00)),
        (SKColors.MediumPurple, Color.FromRgb(0x93, 0x70, 0xDB)),
        (SKColors.Cyan, Color.FromRgb(0x00, 0xFF, 0xFF))
    };

    private int nextPaletteIndex;

    // ダークテーマ（App.xaml の TextPrimaryBrush/BorderBrush）に合わせたグラフの配色。
    // LiveChartsCore は同じ SolidColorPaint インスタンスを複数の軸で使い回すと描画が壊れるため、
    // 軸ごとに毎回新しいインスタンスを生成する（下記メソッド経由でのみ使用）
    private static SolidColorPaint NewAxisLabelPaint() => new(new SKColor(0xDC, 0xDC, 0xDC));
    private static SolidColorPaint NewAxisSeparatorPaint() => new(new SKColor(0x3F, 0x3F, 0x46));

    public SolidColorPaint LegendTextPaint { get; } = new(new SKColor(0xDC, 0xDC, 0xDC));

    [ObservableProperty]
    private string statusMessage = "準備完了";

    [ObservableProperty]
    private string debugLog = "";

    [ObservableProperty]
    private string outputFilePath;

    [ObservableProperty]
    private ulong? monitoredDeviceAddress;

    [ObservableProperty]
    private string monitoredDeviceName = "";

    [ObservableProperty]
    private bool isMonitoringActive;

    private double? lastWrittenTemperature;
    private int? lastWrittenHumidity;

    // グラフはデータ受信の都度ではなく一定間隔でまとめて再描画し、頻繁なちらつきを防ぐ
    private readonly DispatcherTimer graphRefreshTimer;
    private bool graphNeedsRefresh;

    [ObservableProperty]
    private bool isGraphPaused;

    public MainViewModel()
    {
        bluetoothManager.AdvertisementReceived += OnAdvertisementReceived;
        bluetoothManager.LogMessage += OnLogMessage;

        var monitorSettings = monitorSettingsStore.Load();
        outputFilePath = monitorSettings.OutputFilePath;
        monitoredDeviceAddress = monitorSettings.MonitoredDeviceAddress;

        // スキャン開始前（起動直後）でも過去データを表示できるよう、
        // 履歴ファイルが残っている既知デバイスを先に復元しておく
        LoadKnownDevicesFromHistory();

        // BLEアドバタイズ受信時のDispatcher.Invoke(Normal優先度)が頻発すると、
        // 既定のBackground優先度のタイマーは後回しにされ続けてしまうため、Normal優先度で動かす
        graphRefreshTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(2) };
        graphRefreshTimer.Tick += (_, _) =>
        {
            if (graphNeedsRefresh && !IsGraphPaused)
            {
                graphNeedsRefresh = false;
                RefreshGraphSeries();
            }
        };
        graphRefreshTimer.Start();
    }

    // 履歴ファイルが存在する既知デバイスをデバイス一覧に復元する（DeviceTypeはまだ不明のため空文字。
    // 実際にBLEで再検出された時点でOnAdvertisementReceived側が正しい値に更新する）
    private void LoadKnownDevicesFromHistory()
    {
        foreach (var address in historyStore.GetKnownAddresses())
        {
            if (Devices.Any(d => d.BluetoothAddress == address)) continue;

            var history = historyStore.Load(address, "");
            if (history.Count == 0) continue;

            var last = history[^1];
            var paletteIndex = colorStore.GetPaletteIndex(address) ?? nextPaletteIndex++;

            var device = new Device
            {
                BluetoothAddress = address,
                DeviceType = "",
                Alias = aliasStore.GetAlias(address) ?? "",
                IsMonitored = MonitoredDeviceAddress == address,
                ShowOnGraph = true,
                PaletteIndex = paletteIndex,
                GraphColorBrush = new SolidColorBrush(DevicePalette[paletteIndex % DevicePalette.Length].Wpf),
                FirstSeen = history[0].Timestamp,
                LastSeen = last.Timestamp,
                LastTemperature = last.Temperature,
                LastHumidity = last.Humidity
            };

            foreach (var record in history)
            {
                device.History.Add(record);
            }

            Devices.Add(device);
        }

        RefreshMonitoredDeviceName();
    }

    partial void OnSelectedTimeRangeChanged(GraphTimeRange value) => RefreshGraphSeries();

    // 一時停止中は表示をそのまま維持し、記録自体は継続する。
    // 再開時に最新状態へ一気に更新する
    partial void OnIsGraphPausedChanged(bool value)
    {
        if (!value)
        {
            graphNeedsRefresh = false;
            RefreshGraphSeries();
        }
    }

    public void RefreshGraphSeries()
    {
        try
        {
            RefreshGraphSeriesCore();
        }
        catch (Exception ex)
        {
            AppendLog($"RefreshGraphSeries 例外: {ex.GetType().Name} - {ex.Message}");
        }
    }

    // 0時0分を起点としたキリの良い時刻でX軸の罫線位置を計算する
    private static List<double> ComputeTimeSeparators(DateTime from, DateTime to, GraphTimeRange range)
    {
        var separators = new List<double>();
        var midnight = to.Date;

        TimeSpan? fixedStep = range switch
        {
            GraphTimeRange.ThirtyMinutes => TimeSpan.FromMinutes(5),
            GraphTimeRange.OneHour => TimeSpan.FromMinutes(10),
            GraphTimeRange.TwoHours => TimeSpan.FromMinutes(20),
            GraphTimeRange.FourHours => TimeSpan.FromMinutes(30),
            GraphTimeRange.SixHours => TimeSpan.FromHours(1),
            GraphTimeRange.TwelveHours => TimeSpan.FromHours(2),
            GraphTimeRange.OneDay => TimeSpan.FromHours(3),
            GraphTimeRange.OneWeek => TimeSpan.FromDays(1),
            GraphTimeRange.OneMonth => TimeSpan.FromDays(5),
            _ => null
        };

        if (fixedStep.HasValue)
        {
            var step = fixedStep.Value;
            var stepIndex = Math.Floor((from - midnight).Ticks / (double)step.Ticks);
            var cursor = midnight + TimeSpan.FromTicks((long)(stepIndex * step.Ticks));
            while (cursor <= to)
            {
                if (cursor >= from) separators.Add(cursor.Ticks);
                cursor += step;
            }
        }
        else
        {
            // 半年・1年は暦月単位（1日 0時0分）を起点にする
            int monthStep = range == GraphTimeRange.SixMonths ? 1 : 2;
            var cursor = new DateTime(to.Year, to.Month, 1);
            while (cursor > from) cursor = cursor.AddMonths(-monthStep);
            while (cursor <= to)
            {
                if (cursor >= from) separators.Add(cursor.Ticks);
                cursor = cursor.AddMonths(monthStep);
            }
        }

        return separators;
    }

    private void RefreshGraphSeriesCore()
    {
        var now = DateTime.Now;
        var from = SelectedTimeRange switch
        {
            GraphTimeRange.ThirtyMinutes => now.AddMinutes(-30),
            GraphTimeRange.OneHour => now.AddHours(-1),
            GraphTimeRange.TwoHours => now.AddHours(-2),
            GraphTimeRange.FourHours => now.AddHours(-4),
            GraphTimeRange.SixHours => now.AddHours(-6),
            GraphTimeRange.TwelveHours => now.AddHours(-12),
            GraphTimeRange.OneDay => now.AddDays(-1),
            GraphTimeRange.OneWeek => now.AddDays(-7),
            GraphTimeRange.OneMonth => now.AddMonths(-1),
            GraphTimeRange.SixMonths => now.AddMonths(-6),
            GraphTimeRange.OneYear => now.AddYears(-1),
            _ => now.AddHours(-1)
        };

        var newSeries = new List<ISeries>();

        foreach (var device in Devices.Where(d => d.ShowOnGraph))
        {
            var inRange = device.History.Where(r => r.Timestamp >= from).OrderBy(r => r.Timestamp).ToList();
            if (inRange.Count == 0) continue;

            // 表示範囲開始直前の最後の値を「引き継ぎ点」として先頭に加える。
            // これが無いと、範囲内に最初のデータが来るまで線が描かれず、
            // データ自体は存在するのに空白に見えてしまう
            var priorPoint = device.History
                .Where(r => r.Timestamp < from)
                .OrderByDescending(r => r.Timestamp)
                .FirstOrDefault();
            var points = priorPoint != null
                ? new List<TemperatureHumidityRecord> { priorPoint }.Concat(inRange).ToList()
                : inRange;

            var color = DevicePalette[device.PaletteIndex % DevicePalette.Length].Sk;

            // マーカーは点が密集すると潰れて見づらいため表示しない（線のみ）
            const double geometrySize = 0;

            newSeries.Add(new LineSeries<DateTimePoint>
            {
                Name = $"{device.DisplayName} 温度",
                Values = points.Select(p => new DateTimePoint(p.Timestamp, p.Temperature)).ToArray(),
                Stroke = new SolidColorPaint(color, 2),
                Fill = null,
                GeometrySize = geometrySize,
                GeometryFill = new SolidColorPaint(color),
                GeometryStroke = null,
                ScalesYAt = 0
            });

            newSeries.Add(new LineSeries<DateTimePoint>
            {
                Name = $"{device.DisplayName} 湿度",
                Values = points.Select(p => new DateTimePoint(p.Timestamp, p.Humidity)).ToArray(),
                Stroke = new SolidColorPaint(color, 2) { PathEffect = new DashEffect(new float[] { 6, 6 }) },
                Fill = null,
                GeometrySize = geometrySize,
                GeometryFill = new SolidColorPaint(color),
                GeometryStroke = null,
                ScalesYAt = 1
            });
        }

        Series = newSeries.ToArray();

        var longRange = SelectedTimeRange is GraphTimeRange.OneDay or GraphTimeRange.OneWeek
            or GraphTimeRange.OneMonth or GraphTimeRange.SixMonths or GraphTimeRange.OneYear;

        // 罫線位置は綺麗な時刻に揃えるが、表示範囲自体は常に選択レンジ通り「現在時刻」まで伸ばす
        // （罫線の位置に合わせて右端を切り詰めると、直近の罫線間隔分だけ最新データが見えなくなるため）
        var separators = ComputeTimeSeparators(from, now, SelectedTimeRange);
        var axisFrom = (double)from.Ticks;
        var axisTo = (double)now.Ticks;

        XAxes = new[]
        {
            new Axis
            {
                Labeler = value => new DateTime((long)value).ToString(longRange ? "yyyy/MM/dd" : "MM/dd HH:mm"),
                LabelsRotation = 15,
                // データの有無に関わらず、選択したレンジの幅で表示範囲を固定する。
                // 罫線は0時0分を起点としたキリの良い時刻に揃える
                MinLimit = axisFrom,
                MaxLimit = axisTo,
                CustomSeparators = separators,
                LabelsPaint = NewAxisLabelPaint(),
                SeparatorsPaint = NewAxisSeparatorPaint()
            }
        };

        if (isFirstGraphRender)
        {
            isFirstGraphRender = false;
        }
        else
        {
            GraphAnimationsSpeed = TimeSpan.Zero;
        }

        YAxes = new[]
        {
            new Axis
            {
                // 0〜40を5刻み = 8分割。湿度側と目盛りの本数を揃えてグリッド線を一致させる
                Name = "温度 (℃)",
                NamePaint = NewAxisLabelPaint(),
                LabelsPaint = NewAxisLabelPaint(),
                SeparatorsPaint = NewAxisSeparatorPaint(),
                MinLimit = 0,
                MaxLimit = 40,
                MinStep = 5,
                ForceStepToMin = true
            },
            new Axis
            {
                // 10〜90を10刻み = 8分割（温度と同じ本数にして横線を揃える）
                Name = "湿度 (%)",
                Position = LiveChartsCore.Measure.AxisPosition.End,
                NamePaint = NewAxisLabelPaint(),
                LabelsPaint = NewAxisLabelPaint(),
                SeparatorsPaint = NewAxisSeparatorPaint(),
                MinLimit = 10,
                MaxLimit = 90,
                MinStep = 10,
                ForceStepToMin = true
            }
        };
    }

    private void RefreshMonitoredDeviceName()
    {
        var device = MonitoredDeviceAddress.HasValue
            ? Devices.FirstOrDefault(d => d.BluetoothAddress == MonitoredDeviceAddress.Value)
            : null;
        MonitoredDeviceName = device?.DisplayName ?? "";
    }

    partial void OnOutputFilePathChanged(string value)
    {
        SaveMonitorSettings();
    }

    private void SaveMonitorSettings()
    {
        monitorSettingsStore.Save(new MonitorSettings
        {
            OutputFilePath = OutputFilePath,
            MonitoredDeviceAddress = MonitoredDeviceAddress
        });
    }

    public void UpdateMonitoredDevice()
    {
        if (SelectedDevice == null) return;

        if (SelectedDevice.IsMonitored)
        {
            // 監視対象は常に1台のみ。他のデバイスの監視フラグは解除する
            foreach (var d in Devices)
            {
                if (d != SelectedDevice && d.IsMonitored)
                {
                    d.IsMonitored = false;
                }
            }
            MonitoredDeviceAddress = SelectedDevice.BluetoothAddress;
        }
        else if (MonitoredDeviceAddress == SelectedDevice.BluetoothAddress)
        {
            MonitoredDeviceAddress = null;
        }

        RefreshMonitoredDeviceName();
        SaveMonitorSettings();
    }

    public void ToggleMonitoring()
    {
        if (IsMonitoringActive)
        {
            StopMonitoring();
        }
        else
        {
            StartMonitoring();
        }
    }

    private void StartMonitoring()
    {
        if (MonitoredDeviceAddress == null)
        {
            StatusMessage = "監視するデバイスが選択されていません";
            return;
        }

        var directory = Path.GetDirectoryName(OutputFilePath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            MessageBox.Show($"出力先フォルダが存在しません:\n{directory}", "SwitchBotMeter",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = "出力先フォルダが存在しないため監視を開始できません";
            return;
        }

        if (!IsScanning)
        {
            StartScanning();
        }

        lastWrittenTemperature = null;
        lastWrittenHumidity = null;
        IsMonitoringActive = true;
        StatusMessage = $"バックグラウンド監視開始: {MonitoredDeviceName}";
    }

    private void StopMonitoring()
    {
        IsMonitoringActive = false;
        StatusMessage = "バックグラウンド監視を停止しました";
    }

    private void WriteMonitorFile(double temperature, int humidity)
    {
        try
        {
            using var outputFile = new StreamWriter(OutputFilePath, append: false);
            outputFile.WriteLine($"{String.Format("{0,4:F1}", temperature)} ℃");
            outputFile.Write($"{String.Format("{0,4}", humidity)} ％");
        }
        catch (Exception ex)
        {
            AppendLog($"監視ファイル書き込みエラー: {ex.Message}");
        }
    }

    public void StartScanning()
    {
        try
        {
            bluetoothManager.Start();
            IsScanning = true;
            StatusMessage = "スキャン中...";
        }
        catch (Exception ex)
        {
            StatusMessage = $"スキャン開始に失敗しました: {ex.Message}";
            AppendLog($"StartScanning 例外: {ex.GetType().Name} - {ex.Message} (HResult=0x{ex.HResult:X8})");
        }
    }

    public void StopScanning()
    {
        bluetoothManager.Stop();
        IsScanning = false;

        if (IsMonitoringActive)
        {
            IsMonitoringActive = false;
            StatusMessage = "スキャン停止（監視も停止しました）";
        }
        else
        {
            StatusMessage = "スキャン停止";
        }
    }

    private void OnLogMessage(object? sender, string message)
    {
        Application.Current.Dispatcher.Invoke(() => AppendLog(message));
    }

    private void AppendLog(string message)
    {
        DebugLog += $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";
        var lines = DebugLog.Split('\n');
        if (lines.Length > 300)
        {
            DebugLog = string.Join("\n", lines[^300..]);
        }
    }


    private void OnAdvertisementReceived(object? sender, DeviceAdvertisementEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var device = Devices.FirstOrDefault(d => d.BluetoothAddress == e.BluetoothAddress);
            if (device == null)
            {
                var paletteIndex = colorStore.GetPaletteIndex(e.BluetoothAddress) ?? nextPaletteIndex++;
                device = new Device
                {
                    BluetoothAddress = e.BluetoothAddress,
                    DeviceType = BluetoothLEManager.DeviceTypeName(e.DeviceTypeByte),
                    Alias = aliasStore.GetAlias(e.BluetoothAddress) ?? "",
                    IsMonitored = MonitoredDeviceAddress == e.BluetoothAddress,
                    ShowOnGraph = true,
                    PaletteIndex = paletteIndex,
                    GraphColorBrush = new SolidColorBrush(DevicePalette[paletteIndex % DevicePalette.Length].Wpf),
                    FirstSeen = e.Timestamp,
                    LastSeen = e.Timestamp
                };

                // 過去の間引き済み履歴をファイルから復元する
                foreach (var record in historyStore.Load(e.BluetoothAddress, device.DeviceType))
                {
                    device.History.Add(record);
                }

                Devices.Add(device);
            }
            else
            {
                device.LastSeen = e.Timestamp;

                // 起動時に履歴ファイルから復元した直後はDeviceTypeが不明(空)なので、
                // 実際にBLEで検出できた時点で正しい種別に補完する
                if (string.IsNullOrEmpty(device.DeviceType))
                {
                    device.DeviceType = BluetoothLEManager.DeviceTypeName(e.DeviceTypeByte);
                }
            }

            device.LastTemperature = e.Temperature;
            device.LastHumidity = e.Humidity;
            device.LastBattery = e.Battery >= 0 ? e.Battery : null;

            // 間引き: 直前に記録した点から1分未満しか経っていなければ記録しない
            var lastKept = device.History.Count > 0 ? device.History[^1] : null;
            if (lastKept == null || e.Timestamp - lastKept.Timestamp >= HistoryMinInterval)
            {
                var record = new TemperatureHumidityRecord
                {
                    BluetoothAddress = e.BluetoothAddress,
                    Timestamp = e.Timestamp,
                    Temperature = e.Temperature,
                    Humidity = e.Humidity,
                    Battery = e.Battery,
                    DeviceName = device.DeviceType
                };

                device.History.Add(record);
                historyStore.Append(e.BluetoothAddress, record);

                if (device.ShowOnGraph)
                {
                    graphNeedsRefresh = true;
                }
            }

            if (MonitoredDeviceAddress == e.BluetoothAddress)
            {
                RefreshMonitoredDeviceName();

                if (IsMonitoringActive &&
                    (lastWrittenTemperature != e.Temperature || lastWrittenHumidity != e.Humidity))
                {
                    WriteMonitorFile(e.Temperature, e.Humidity);
                    lastWrittenTemperature = e.Temperature;
                    lastWrittenHumidity = e.Humidity;
                }
            }
        });
    }

    public void SaveAliasForSelectedDevice()
    {
        if (SelectedDevice == null) return;
        aliasStore.SetAlias(SelectedDevice.BluetoothAddress, SelectedDevice.Alias);
    }

    public void SetDeviceColor(Device device, int paletteIndex)
    {
        device.PaletteIndex = paletteIndex;
        device.GraphColorBrush = new SolidColorBrush(DevicePalette[paletteIndex % DevicePalette.Length].Wpf);
        colorStore.SetPaletteIndex(device.BluetoothAddress, paletteIndex);
        RefreshGraphSeries();
    }

    // フォーカスを外さないままアプリを終了しても別名が失われないよう、
    // 入力のたびに即座に保存する（LostFocusでの保存は保険として残す）
    partial void OnSelectedDeviceChanged(Device value)
    {
        if (aliasTrackedDevice != null)
        {
            aliasTrackedDevice.PropertyChanged -= AliasTrackedDevice_PropertyChanged;
        }

        if (value != null)
        {
            value.PropertyChanged += AliasTrackedDevice_PropertyChanged;
        }

        aliasTrackedDevice = value;
    }

    private void AliasTrackedDevice_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Device.Alias) && sender is Device device)
        {
            aliasStore.SetAlias(device.BluetoothAddress, device.Alias);
            if (MonitoredDeviceAddress == device.BluetoothAddress)
            {
                RefreshMonitoredDeviceName();
            }
        }
    }
}
