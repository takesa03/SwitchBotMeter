using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Radios;
using Windows.Storage.Streams;
using SwitchBotUtil;

namespace SwitchBotMeter.Services;

public class DeviceAdvertisementEventArgs : EventArgs
{
    public ulong BluetoothAddress { get; set; }
    public byte DeviceTypeByte { get; set; }
    public double Temperature { get; set; }
    public int Humidity { get; set; }
    public int Battery { get; set; }
    public DateTime Timestamp { get; set; }
}

public class BluetoothLEManager
{
    // SwitchBot Meter 系の Service Data は 16bit UUID 0xFD3D で Scan Response 内に載る
    // (128bit の cba20d00-... は GATT 接続用のサービスUUIDで、アドバタイズのService Dataとは別物)
    private const ushort MeterServiceUuid16 = 0xFD3D;

    // Windowsの内部状態が壊れると、Watcherを作り直さない限り復旧しないことがあるため差し替え可能にする
    private BluetoothLEAdvertisementWatcher watcher;

    // ManufacturerData と ServiceData は別々のBLEパケット（別イベント）で届くため、
    // アドレスごとに直近のManufacturerDataを保持して突き合わせる
    private readonly Dictionary<ulong, byte[]> lastManufacturerData = new();

    // watcher.Status は異常時に正しく更新されないことがあるため、
    // 「アプリとしてスキャンを要求しているか」は別フラグで管理する
    private bool isScanningRequested;
    private DateTime lastAdvertisementAt = DateTime.MinValue;

    // 長時間稼働しているとWindows側のBLEスタックが応答しなくなることがあるため、
    // 一定時間まったく受信がない場合はWatcherを自動的に作り直す（ウォッチドッグ 第1段階）。
    // それでも復旧しない場合は、Bluetooth無線そのものをOFF/ONする（第2段階、より強力だが
    // ペアリング済みの他のBluetooth機器も一時的に切断されるため、長い閾値でのみ実行する）
    private static readonly TimeSpan WatchdogCheckInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan WatcherRecreateThreshold = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan RadioRestartThreshold = TimeSpan.FromMinutes(15);
    private readonly Timer watchdogTimer;
    private DateTime? lastWatcherRecreateAt;
    private DateTime? lastRadioRestartAt;
    private bool radioRestartInProgress;

    // 間欠スキャン: 常時スキャンし続けるとCPU負荷が高いため、短時間だけスキャンして
    // 残りは休止するサイクルを繰り返す。SwitchBot Meter系は1〜2秒に1回広告を出すため、
    // 数秒のスキャン窓でも十分捕捉できる（履歴の記録自体も1分に1回への間引きのため、
    // 常時スキャンする必要性が薄い）
    private static readonly TimeSpan ScanOnDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ScanOffDuration = TimeSpan.FromSeconds(50);
    private readonly Timer dutyCycleTimer;
    private bool dutyCycleScanIsOn;

    public event EventHandler<DeviceAdvertisementEventArgs>? AdvertisementReceived;
    public event EventHandler<string>? LogMessage;

    public bool IsScanning => isScanningRequested;

    // 設定すると、このアドレス以外のデバイスのログ出力を抑制する（デバッグ用の一時的な絞り込み）
    public ulong? LogAddressFilter { get; set; }

    public BluetoothLEManager()
    {
        watcher = CreateWatcher();
        watchdogTimer = new Timer(_ => WatchdogCheck(), null, WatchdogCheckInterval, WatchdogCheckInterval);
        dutyCycleTimer = new Timer(_ => DutyCycleTick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    private BluetoothLEAdvertisementWatcher CreateWatcher()
    {
        var w = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };
        w.Received += Watcher_Received;
        w.Stopped += Watcher_Stopped;
        return w;
    }

    public void Start()
    {
        isScanningRequested = true;
        lastAdvertisementAt = DateTime.Now;
        dutyCycleScanIsOn = true;
        Log($"Start() 呼び出し（間欠スキャン: ON {ScanOnDuration.TotalSeconds:F0}秒 / OFF {ScanOffDuration.TotalSeconds:F0}秒）");
        StartWatcherInternal();
        dutyCycleTimer.Change(ScanOnDuration, Timeout.InfiniteTimeSpan);
    }

    public void Stop()
    {
        isScanningRequested = false;
        dutyCycleTimer.Change(Timeout.Infinite, Timeout.Infinite);
        watcher.Stop();
        Log($"Stop() 呼び出し (Status={watcher.Status})");
    }

    private void StartWatcherInternal()
    {
        try
        {
            watcher.Start();
            Log($"Watcher Start() 完了 (Status={watcher.Status})");
        }
        catch (Exception ex)
        {
            Log($"Watcher Start() で例外発生: {ex.GetType().Name} - {ex.Message} (HResult=0x{ex.HResult:X8})");
            throw;
        }
    }

    // 間欠スキャンのON/OFFを切り替える
    private void DutyCycleTick()
    {
        if (!isScanningRequested) return;

        if (dutyCycleScanIsOn)
        {
            dutyCycleScanIsOn = false;
            try { watcher.Stop(); } catch { /* 既に停止している場合は無視 */ }
            dutyCycleTimer.Change(ScanOffDuration, Timeout.InfiniteTimeSpan);
        }
        else
        {
            dutyCycleScanIsOn = true;
            try { StartWatcherInternal(); } catch { /* 例外はログ済み。次のサイクルで再試行する */ }
            dutyCycleTimer.Change(ScanOnDuration, Timeout.InfiniteTimeSpan);
        }
    }

    // 段階的な自動復旧:
    // 第1段階(3分無受信): Watcherオブジェクトを作り直す（軽量、他機器への影響なし）
    // 第2段階(15分無受信): それでも直らない場合はBluetooth無線自体をOFF/ONする（重い。他のBluetooth機器も一時切断）
    private void WatchdogCheck()
    {
        if (!isScanningRequested || radioRestartInProgress) return;

        var silence = DateTime.Now - lastAdvertisementAt;
        if (silence < WatcherRecreateThreshold) return;

        if (silence >= RadioRestartThreshold &&
            (lastRadioRestartAt == null || DateTime.Now - lastRadioRestartAt >= RadioRestartThreshold))
        {
            lastRadioRestartAt = DateTime.Now;
            Log($"ウォッチドッグ: {silence.TotalMinutes:F1}分間データ受信がないためBluetooth無線の再起動を試みます");
            _ = RestartBluetoothRadioAsync();
            return;
        }

        // 直近でWatcherを再生成済みなら、無駄な再生成を繰り返さない
        if (lastWatcherRecreateAt != null && DateTime.Now - lastWatcherRecreateAt < WatcherRecreateThreshold) return;

        Log($"ウォッチドッグ: {silence.TotalMinutes:F1}分間データ受信がないためBLE Watcherを再生成します");
        RecreateWatcherAndStart();
    }

    private void RecreateWatcherAndStart()
    {
        lastWatcherRecreateAt = DateTime.Now;

        var old = watcher;
        old.Received -= Watcher_Received;
        old.Stopped -= Watcher_Stopped;
        try { old.Stop(); } catch { /* 既に壊れている可能性があるため無視 */ }

        watcher = CreateWatcher();
        lastAdvertisementAt = DateTime.Now;

        try
        {
            watcher.Start();
            Log($"Watcher再生成後にStart()完了 (Status={watcher.Status})");
        }
        catch (Exception ex)
        {
            Log($"再生成後のStart()で例外発生: {ex.GetType().Name} - {ex.Message}");
        }

        // 間欠スキャンのON/OFF管理と状態がずれないよう、再生成をON区間として扱い直す
        dutyCycleScanIsOn = true;
        dutyCycleTimer.Change(ScanOnDuration, Timeout.InfiniteTimeSpan);
    }

    // Bluetooth無線そのものをOFF/ONする。Watcherの再生成では復旧しない、
    // Windows側のBluetoothスタック／ドライバ自体が無応答になったケース向けの強力な復旧手段。
    // 他のペアリング済みBluetooth機器（マウス・キーボード・スマホ等）も一時的に切断される
    public async Task RestartBluetoothRadioAsync()
    {
        if (radioRestartInProgress)
        {
            Log("Bluetooth無線の再起動は既に実行中です");
            return;
        }

        radioRestartInProgress = true;
        try
        {
            Log("Bluetooth無線の再起動を開始します（他のBluetooth機器も一時的に切断されます）");
            var radios = await Radio.GetRadiosAsync();
            var bluetoothRadio = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            if (bluetoothRadio == null)
            {
                Log("Bluetooth無線が見つかりませんでした");
                return;
            }

            Log($"Bluetooth無線をOFFにします (現在の状態={bluetoothRadio.State})");
            await bluetoothRadio.SetStateAsync(RadioState.Off);
            await Task.Delay(3000);

            Log("Bluetooth無線をONにします");
            var result = await bluetoothRadio.SetStateAsync(RadioState.On);
            Log($"Bluetooth無線の再起動完了 (結果={result}, 状態={bluetoothRadio.State})");

            await Task.Delay(2000);

            if (isScanningRequested)
            {
                RecreateWatcherAndStart();
            }
        }
        catch (Exception ex)
        {
            Log($"Bluetooth無線再起動でエラー: {ex.GetType().Name} - {ex.Message}");
        }
        finally
        {
            radioRestartInProgress = false;
        }
    }

    private void Watcher_Stopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        Log($"Watcher が停止しました。Error={args.Error}");
    }

    public static string DeviceTypeName(byte deviceType) => deviceType switch
    {
        SBDeviceTypes.Meter => "Meter",
        SBDeviceTypes.MeterPlus => "MeterPlus",
        SBDeviceTypes.OutdoorMeter => "OutdoorMeter",
        SBDeviceTypes.MeterPro => "MeterPro",
        SBDeviceTypes.Hub2 => "Hub2",
        _ => $"Unknown(0x{deviceType:X2})"
    };

    private void Watcher_Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        // SwitchBot以外のデバイスも含め、何かしら受信できていればWatcherは生きていると判断する
        lastAdvertisementAt = DateTime.Now;

        var mfrEntry = args.Advertisement.ManufacturerData.FirstOrDefault(m => m.CompanyId == SwitchBot.companyId);
        var serviceDataSections = args.Advertisement.DataSections
            .Where(s => s.DataType == BluetoothLEAdvertisementDataTypes.ServiceData16BitUuids)
            .ToList();

        if (mfrEntry == null && serviceDataSections.Count == 0) return;

        bool logEnabled = LogAddressFilter == null || args.BluetoothAddress == LogAddressFilter.Value;
        void LogF(string message) { if (logEnabled) Log(message); }

        LogF($"検出: {args.BluetoothAddress:X} RSSI={args.RawSignalStrengthInDBm} AdvType={args.AdvertisementType}");

        byte[]? mfrBytes = null;
        if (mfrEntry != null)
        {
            mfrBytes = ReadBuffer(mfrEntry.Data);
            lastManufacturerData[args.BluetoothAddress] = mfrBytes;
            LogF($"  ManufacturerData CompanyId=0x{mfrEntry.CompanyId:X4} Data={Convert.ToHexString(mfrBytes)}");
        }
        else if (lastManufacturerData.TryGetValue(args.BluetoothAddress, out var cachedMfrBytes))
        {
            mfrBytes = cachedMfrBytes;
            LogF($"  ManufacturerData(キャッシュ) Data={Convert.ToHexString(mfrBytes)}");
        }

        foreach (var section in serviceDataSections)
        {
            var raw = ReadBuffer(section.Data);
            LogF($"  ServiceData16 Raw={Convert.ToHexString(raw)}");

            if (raw.Length < 3)
            {
                LogF($"  ServiceDataが短すぎます: {raw.Length} bytes");
                continue;
            }

            ushort uuid16 = (ushort)(raw[0] | (raw[1] << 8));
            if (uuid16 != MeterServiceUuid16)
            {
                LogF($"  UUID不一致: 0x{uuid16:X4} (期待値: 0x{MeterServiceUuid16:X4})");
                continue;
            }

            byte deviceType = raw[2];
            var data = raw.AsSpan(2);

            // Hub2 は Service Data にタイプ+ステータスのみを載せ、実際の温湿度は
            // Manufacturer Data 側（MACアドレス6バイトに続く状態バイト列）に格納される
            if (deviceType == SBDeviceTypes.Hub2)
            {
                if (mfrBytes == null)
                {
                    LogF("  Hub2を検出しましたがManufacturerDataがありません");
                    continue;
                }
                ParseHub2(args, mfrBytes, logEnabled);
                continue;
            }

            // OutdoorMeter(防水温湿度計) も Service Data にはタイプのみを載せ、
            // 実際の温湿度は Manufacturer Data 側に格納される
            if (deviceType == SBDeviceTypes.OutdoorMeter && data.Length < 6)
            {
                if (mfrBytes == null)
                {
                    LogF("  OutdoorMeterを検出しましたがManufacturerDataがありません");
                    continue;
                }
                ParseOutdoorMeter(args, mfrBytes, logEnabled);
                continue;
            }

            if (data.Length < 6)
            {
                // Meter以外のSwitchBotデバイス（Hub等）は温湿度データを持たないため、
                // Service Dataがタイプ+ステータスの2バイト程度で終わる場合がある。
                // ただしMeter系(OutdoorMeter等)でも短い場合は、Hub2同様ManufacturerData側に
                // 実データがある可能性があるため、参照用にキャッシュ内容も出力する。
                var mfrHex = mfrBytes != null ? Convert.ToHexString(mfrBytes) : "(なし)";
                LogF($"  温湿度データなし: Type=0x{deviceType:X2} ServiceDataPayload={data.Length}bytes ManufacturerData={mfrHex}");
                continue;
            }

            int battery = data[2] & 0b01111111;
            int tempSign = (data[4] & 0b10000000) != 0 ? 1 : -1;
            double temperature = tempSign * ((data[4] & 0b01111111) + (data[3] & 0b00001111) / 10.0);
            int humidity = data[5] & 0b01111111;

            LogF($"  温湿度データ取得: {args.BluetoothAddress:X} Type={DeviceTypeName(deviceType)} Temp={temperature:F1} Hum={humidity} Batt={battery}");

            AdvertisementReceived?.Invoke(this, new DeviceAdvertisementEventArgs
            {
                BluetoothAddress = args.BluetoothAddress,
                DeviceTypeByte = deviceType,
                Temperature = temperature,
                Humidity = humidity,
                Battery = battery,
                Timestamp = DateTime.Now
            });
        }
    }

    // Hub2 の Manufacturer Data 構造 (MACアドレス6バイトの後、状態バイト列が続く):
    // index12=ステータス(照度等), index13=温度小数部, index14=温度整数部+符号, index15=湿度
    // (pySwitchbot の adv_parsers/hub2.py を参照)
    private void ParseHub2(BluetoothLEAdvertisementReceivedEventArgs args, byte[] mfrBytes, bool logEnabled)
    {
        if (mfrBytes.Length < 16)
        {
            if (logEnabled) Log($"  Hub2データ長不足: {mfrBytes.Length} bytes (16バイト以上必要)");
            return;
        }

        byte tempDecimalByte = mfrBytes[13];
        byte tempIntByte = mfrBytes[14];
        byte humidityByte = mfrBytes[15];

        int tempSign = (tempIntByte & 0b10000000) != 0 ? 1 : -1;
        double temperature = tempSign * ((tempIntByte & 0b01111111) + (tempDecimalByte & 0b00001111) / 10.0);
        int humidity = humidityByte & 0b01111111;

        if (logEnabled) Log($"  Hub2 温湿度データ取得: {args.BluetoothAddress:X} Temp={temperature:F1} Hum={humidity}");

        AdvertisementReceived?.Invoke(this, new DeviceAdvertisementEventArgs
        {
            BluetoothAddress = args.BluetoothAddress,
            DeviceTypeByte = SBDeviceTypes.Hub2,
            Temperature = temperature,
            Humidity = humidity,
            Battery = -1, // Hub2 はAC電源のためバッテリー情報なし
            Timestamp = DateTime.Now
        });
    }

    // OutdoorMeter(防水温湿度計) の Manufacturer Data 構造 (MACアドレス6バイトの後、状態バイト列が続く):
    // index7=バッテリー, index8=温度小数部, index9=温度整数部+符号, index10=湿度
    // (pySwitchbot の adv_parsers/weather_station.py を参照)
    private void ParseOutdoorMeter(BluetoothLEAdvertisementReceivedEventArgs args, byte[] mfrBytes, bool logEnabled)
    {
        if (mfrBytes.Length < 11)
        {
            if (logEnabled) Log($"  OutdoorMeterデータ長不足: {mfrBytes.Length} bytes (11バイト以上必要)");
            return;
        }

        int battery = mfrBytes[7] & 0b01111111;
        byte tempDecimalByte = mfrBytes[8];
        byte tempIntByte = mfrBytes[9];
        byte humidityByte = mfrBytes[10];

        int tempSign = (tempIntByte & 0b10000000) != 0 ? 1 : -1;
        double temperature = tempSign * ((tempIntByte & 0b01111111) + (tempDecimalByte & 0b00001111) / 10.0);
        int humidity = humidityByte & 0b01111111;

        if (logEnabled) Log($"  OutdoorMeter 温湿度データ取得: {args.BluetoothAddress:X} Temp={temperature:F1} Hum={humidity} Batt={battery}");

        AdvertisementReceived?.Invoke(this, new DeviceAdvertisementEventArgs
        {
            BluetoothAddress = args.BluetoothAddress,
            DeviceTypeByte = SBDeviceTypes.OutdoorMeter,
            Temperature = temperature,
            Humidity = humidity,
            Battery = battery,
            Timestamp = DateTime.Now
        });
    }

    private void Log(string message) => LogMessage?.Invoke(this, message);

    private static byte[] ReadBuffer(IBuffer buffer)
    {
        var data = new byte[buffer.Length];
        using var reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(data);
        return data;
    }
}
