using System;

namespace SwitchBotUtil;

public class SwitchBot
{
    public const ushort companyId = 0x0969;
}

public class SBDeviceTypes
{
    public const byte Bot = 0x48;
    public const byte Meter = 0x54;
    public const byte Humidifier = 0x65;
    public const byte Curtain = 0x63;
    public const byte MotionSensor = 0x73;
    public const byte ContactSensor = 0x64;
    public const byte ColorBulb = 0x75;
    public const byte LEDStripLight = 0x72;
    public const byte SmartLock = 0x6F;
    public const byte PlugMini = 0x67;
    public const byte MeterPlus = 0x69;
    public const byte OutdoorMeter = 0x77;
    public const byte MeterPro = 0x34; // 温度湿度計Pro
    public const byte HubPlus = 0x70; //SwitchBot Hub Plus (WoLink Plus)
    public const byte Hub = 0x6C; //SwitchBot Hub (WoLink)
    public const byte HubMini = 0x6D;//SwitchBot Mini (HubMini)
    public const byte Hub2 = 0x76; //SwitchBot Hub 2 (温湿度計内蔵)
}

public class Uuids
{
    public static readonly Guid meter = new Guid("cba20d00-224d-11e6-9fb8-0002a5d5c51b");
}

