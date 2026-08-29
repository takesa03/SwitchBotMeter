using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Devices.Bluetooth.Advertisement;
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

    private readonly BluetoothLEAdvertisementWatcher watcher;

    // ManufacturerData と ServiceData は別々のBLEパケット（別イベント）で届くため、
    // アドレスごとに直近のManufacturerDataを保持して突き合わせる
    private readonly Dictionary<ulong, byte[]> lastManufacturerData = new();

    public event EventHandler<DeviceAdvertisementEventArgs>? AdvertisementReceived;
    public event EventHandler<string>? LogMessage;

    public bool IsScanning => watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started;

    // 設定すると、このアドレス以外のデバイスのログ出力を抑制する（デバッグ用の一時的な絞り込み）
    public ulong? LogAddressFilter { get; set; }

    public BluetoothLEManager()
    {
        watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };
        watcher.Received += Watcher_Received;
        watcher.Stopped += Watcher_Stopped;
    }

    public void Start()
    {
        Log($"Start() 呼び出し (現在の Status={watcher.Status})");
        try
        {
            watcher.Start();
            Log($"Start() 完了 (Status={watcher.Status})");
        }
        catch (Exception ex)
        {
            Log($"Start() で例外発生: {ex.GetType().Name} - {ex.Message} (HResult=0x{ex.HResult:X8})");
            throw;
        }
    }

    public void Stop()
    {
        watcher.Stop();
        Log($"Stop() 呼び出し (Status={watcher.Status})");
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
