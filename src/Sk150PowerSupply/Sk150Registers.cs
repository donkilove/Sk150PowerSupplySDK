namespace Sk150PowerSupply;

/// <summary>
/// Register map and scaling factors for XY-SK / ZK-SK series buck-boost modules (SK60, SK120,
/// SK150). Addresses and scaling are shared across the family; the register map was verified
/// against an SK150 running firmware 142.
/// </summary>
/// <remarks>
/// All values are unsigned 16-bit integers in big-endian byte order. Physical values are the raw
/// register divided by the scale factor for that register.
/// </remarks>
public static class Sk150Registers
{
    // Live settings and measurements (0x0000-0x0005).

    /// <summary>0x0000 — target output voltage, scale 100 (raw 330 = 3.30 V).</summary>
    public const ushort VoltageSet = 0x0000;

    /// <summary>0x0001 — output current limit, scale 1000 (raw 100 = 0.100 A).</summary>
    public const ushort CurrentSet = 0x0001;

    /// <summary>0x0002 — measured output voltage, scale 100.</summary>
    public const ushort VoltageOut = 0x0002;

    /// <summary>0x0003 — measured output current, scale 1000.</summary>
    public const ushort CurrentOut = 0x0003;

    /// <summary>0x0004 — measured output power, scale 100.</summary>
    public const ushort PowerOut = 0x0004;

    /// <summary>0x0005 — measured input voltage, scale 100.</summary>
    public const ushort VoltageIn = 0x0005;

    // Accumulators and runtime (0x0006-0x000C).

    /// <summary>0x0006 — accumulated capacity, low 16 bits, unit mAh.</summary>
    public const ushort CapacityLow = 0x0006;

    /// <summary>0x0007 — accumulated capacity, high 16 bits, unit mAh.</summary>
    public const ushort CapacityHigh = 0x0007;

    /// <summary>0x0008 — accumulated energy, low 16 bits, unit mWh.</summary>
    public const ushort EnergyLow = 0x0008;

    /// <summary>0x0009 — accumulated energy, high 16 bits, unit mWh.</summary>
    public const ushort EnergyHigh = 0x0009;

    /// <summary>0x000A — runtime hours.</summary>
    public const ushort RuntimeHours = 0x000A;

    /// <summary>0x000B — runtime minutes.</summary>
    public const ushort RuntimeMinutes = 0x000B;

    /// <summary>0x000C — runtime seconds.</summary>
    public const ushort RuntimeSeconds = 0x000C;

    // Temperatures, protection, and output control (0x000D-0x001D).

    /// <summary>0x000D — internal temperature, scale 10.</summary>
    public const ushort TemperatureIn = 0x000D;

    /// <summary>0x000E — external NTC probe temperature, scale 10; reports 8888 when no probe is fitted.</summary>
    public const ushort TemperatureEx = 0x000E;

    /// <summary>0x000F — key lock (0 = unlocked, 1 = locked).</summary>
    public const ushort KeyLock = 0x000F;

    /// <summary>0x0010 — protection status code, see <see cref="Sk150ProtectionCode"/>.</summary>
    public const ushort Protection = 0x0010;

    /// <summary>0x0011 — control mode (0 = CV, 1 = CC), see <see cref="Sk150ControlMode"/>.</summary>
    public const ushort ControlMode = 0x0011;

    /// <summary>0x0012 — output enable (0 = off, 1 = on).</summary>
    public const ushort OutputEnable = 0x0012;

    /// <summary>0x0013 — temperature unit (0 = Celsius, 1 = Fahrenheit).</summary>
    public const ushort TemperatureUnit = 0x0013;

    /// <summary>0x0014 — backlight brightness, 0-5.</summary>
    public const ushort Backlight = 0x0014;

    /// <summary>0x0015 — screen sleep time in minutes, 0-9; 0 keeps the screen on.</summary>
    public const ushort SleepMinutes = 0x0015;

    /// <summary>0x0016 — product model id (read only).</summary>
    public const ushort ModelId = 0x0016;

    /// <summary>0x0017 — firmware version (read only).</summary>
    public const ushort FirmwareVersion = 0x0017;

    /// <summary>0x0018 — MODBUS slave address, 1-247.</summary>
    public const ushort SlaveAddress = 0x0018;

    /// <summary>0x0019 — baud rate index, see <see cref="Sk150Registers.BaudRates"/>.</summary>
    public const ushort BaudRateIndex = 0x0019;

    /// <summary>0x001A — internal temperature calibration offset, scale 10.</summary>
    public const ushort TemperatureInCalibration = 0x001A;

    /// <summary>0x001B — external temperature calibration offset, scale 10.</summary>
    public const ushort TemperatureExCalibration = 0x001B;

    /// <summary>0x001C — buzzer enable (0 = off, 1 = on).</summary>
    public const ushort Buzzer = 0x001C;

    /// <summary>0x001D — write 0-9 to load preset group M0-M9.</summary>
    public const ushort LoadPreset = 0x001D;

    /// <summary>First register of the status block (0x0000).</summary>
    public const ushort StatusBlockStart = 0x0000;

    /// <summary>Number of registers in the status block (0x0000-0x001D).</summary>
    public const ushort StatusBlockCount = 30;

    /// <summary>Base address of preset group M0.</summary>
    public const ushort PresetBase = 0x0050;

    /// <summary>Address distance between two preset groups.</summary>
    public const ushort PresetStride = 0x0010;

    /// <summary>Registers per preset group (offsets 0x00-0x0D).</summary>
    public const ushort PresetRegisterCount = 14;

    /// <summary>Scale factor for voltage registers.</summary>
    public const decimal VoltageScale = 100m;

    /// <summary>Scale factor for current registers.</summary>
    public const decimal CurrentScale = 1000m;

    /// <summary>Scale factor for power registers.</summary>
    public const decimal PowerScale = 100m;

    /// <summary>Scale factor for temperature registers.</summary>
    public const decimal TemperatureScale = 10m;

    /// <summary>Scale factor for the preset over-power register (unit 0.1 W).</summary>
    public const decimal PresetPowerScale = 10m;

    /// <summary>
    /// Raw value the external temperature register reports when no NTC probe is connected
    /// (8888 = 888.8 in the configured unit). Observed on an SK150 with firmware 142 and not
    /// documented by the vendor.
    /// </summary>
    public const ushort ExternalTemperatureNotConnected = 8888;

    /// <summary>Model id reported by the SK150 (0x5960).</summary>
    public const ushort ModelIdSk150 = 22880;

    /// <summary>Baud rate index to baud rate mapping for register 0x0019.</summary>
    public static IReadOnlyDictionary<int, int> BaudRates { get; } = new Dictionary<int, int>
    {
        [0] = 9600,
        [1] = 14400,
        [2] = 19200,
        [3] = 38400,
        [4] = 56000,
        [5] = 57600,
        [6] = 115200,
    };

    /// <summary>Returns the address of the first register of preset group <paramref name="index"/>.</summary>
    public static ushort PresetAddress(int index)
    {
        if (index is < 0 or > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Preset groups are M0-M9.");
        }

        return (ushort)(PresetBase + (index * PresetStride));
    }

    /// <summary>Converts a physical value to the raw register value at the given scale.</summary>
    public static ushort ToRaw(decimal value, decimal scale)
    {
        decimal scaled = decimal.Round(value * scale, 0, MidpointRounding.AwayFromZero);
        if (scaled < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Registers cannot hold negative values.");
        }

        if (scaled > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), value, $"Value {value} overflows a 16-bit register at scale {scale}.");
        }

        return (ushort)scaled;
    }

    /// <summary>Converts a raw register value to a physical value at the given scale.</summary>
    public static decimal FromRaw(ushort raw, decimal scale) => raw / scale;
}
