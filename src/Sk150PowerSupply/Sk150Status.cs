namespace Sk150PowerSupply;

/// <summary>
/// Decoded contents of the 30-register status block (0x0000-0x001D): setpoints, measurements,
/// accumulators, protection state and the device configuration mirrored in that block.
/// </summary>
/// <remarks>
/// Measured voltages, currents and powers are decoded with the register scales (100, 1000, 100);
/// setpoints are decoded the same way. Temperatures are reported in whichever unit register 0x0013
/// selects — read <see cref="TemperatureUnit"/> before comparing them to a Celsius threshold.
/// </remarks>
public sealed record Sk150Status
{
    /// <summary>Target output voltage, in volts (register 0x0000).</summary>
    public decimal VoltageSet { get; init; }

    /// <summary>Output current limit, in amperes (register 0x0001).</summary>
    public decimal CurrentSet { get; init; }

    /// <summary>Measured output voltage, in volts (register 0x0002).</summary>
    public decimal VoltageOut { get; init; }

    /// <summary>Measured output current, in amperes (register 0x0003).</summary>
    public decimal CurrentOut { get; init; }

    /// <summary>Measured output power, in watts (register 0x0004).</summary>
    public decimal PowerOut { get; init; }

    /// <summary>Measured input voltage, in volts (register 0x0005).</summary>
    public decimal VoltageIn { get; init; }

    /// <summary>Accumulated charge, in milliampere-hours (registers 0x0006-0x0007).</summary>
    public int CapacityMah { get; init; }

    /// <summary>Accumulated energy, in milliwatt-hours (registers 0x0008-0x0009).</summary>
    public int EnergyMwh { get; init; }

    /// <summary>Elapsed runtime (registers 0x000A-0x000C).</summary>
    public TimeSpan Runtime { get; init; }

    /// <summary>Internal temperature (register 0x000D), in the unit given by <see cref="TemperatureUnit"/>.</summary>
    public decimal TemperatureIn { get; init; }

    /// <summary>
    /// External NTC probe temperature (register 0x000E), in the unit given by
    /// <see cref="TemperatureUnit"/>. Null when the register reports
    /// <see cref="Sk150Registers.ExternalTemperatureNotConnected"/>, i.e. no probe is fitted.
    /// </summary>
    public decimal? TemperatureEx { get; init; }

    /// <summary>True when the front-panel keys are locked (register 0x000F).</summary>
    public bool KeyLocked { get; init; }

    /// <summary>Protection state (register 0x0010); <see cref="Sk150ProtectionCode.None"/> when healthy.</summary>
    public Sk150ProtectionCode Protection { get; init; }

    /// <summary>Raw protection code, kept for codes outside the documented table.</summary>
    public ushort ProtectionRaw { get; init; }

    /// <summary>Active regulation mode (register 0x0011).</summary>
    public Sk150ControlMode ControlMode { get; init; }

    /// <summary>True when the output stage is enabled (register 0x0012).</summary>
    public bool OutputEnabled { get; init; }

    /// <summary>Temperature unit (register 0x0013).</summary>
    public Sk150TemperatureUnit TemperatureUnit { get; init; }

    /// <summary>Backlight brightness, 0-5 (register 0x0014).</summary>
    public int Backlight { get; init; }

    /// <summary>Screen sleep time in minutes, 0-9; 0 keeps the screen on (register 0x0015).</summary>
    public int SleepMinutes { get; init; }

    /// <summary>Product model id (register 0x0016).</summary>
    public ushort ModelId { get; init; }

    /// <summary>Firmware version (register 0x0017).</summary>
    public ushort FirmwareVersion { get; init; }

    /// <summary>MODBUS slave address (register 0x0018).</summary>
    public byte SlaveAddress { get; init; }

    /// <summary>Baud rate index, see <see cref="Sk150Registers.BaudRates"/> (register 0x0019).</summary>
    public int BaudRateIndex { get; init; }

    /// <summary>Baud rate decoded from <see cref="BaudRateIndex"/>, or null when the index is unknown.</summary>
    public int? BaudRate { get; init; }

    /// <summary>Internal temperature calibration offset (register 0x001A).</summary>
    public decimal TemperatureInCalibration { get; init; }

    /// <summary>External temperature calibration offset (register 0x001B).</summary>
    public decimal TemperatureExCalibration { get; init; }

    /// <summary>True when the buzzer is enabled (register 0x001C).</summary>
    public bool BuzzerEnabled { get; init; }

    /// <summary>Accumulated energy in watt-hours, derived from <see cref="EnergyMwh"/>.</summary>
    public decimal EnergyWh => EnergyMwh / 1000m;

    /// <summary>Accumulated charge in ampere-hours, derived from <see cref="CapacityMah"/>.</summary>
    public decimal CapacityAh => CapacityMah / 1000m;

    /// <summary>The 30 raw registers this snapshot was decoded from.</summary>
    public IReadOnlyList<ushort> Raw { get; init; } = Array.Empty<ushort>();

    /// <summary>Decodes a status block read from <see cref="Sk150Registers.StatusBlockStart"/>.</summary>
    /// <param name="block">At least <see cref="Sk150Registers.StatusBlockCount"/> registers.</param>
    /// <exception cref="ArgumentException">The block is shorter than the status block.</exception>
    public static Sk150Status FromRegisters(ReadOnlySpan<ushort> block)
    {
        if (block.Length < Sk150Registers.StatusBlockCount)
        {
            throw new ArgumentException(
                $"A status block needs {Sk150Registers.StatusBlockCount} registers, got {block.Length}.",
                nameof(block));
        }

        int baudIndex = block[Sk150Registers.BaudRateIndex];
        ushort protectionRaw = block[Sk150Registers.Protection];
        ushort externalRaw = block[Sk150Registers.TemperatureEx];

        return new Sk150Status
        {
            VoltageSet = Sk150Registers.FromRaw(block[Sk150Registers.VoltageSet], Sk150Registers.VoltageScale),
            CurrentSet = Sk150Registers.FromRaw(block[Sk150Registers.CurrentSet], Sk150Registers.CurrentScale),
            VoltageOut = Sk150Registers.FromRaw(block[Sk150Registers.VoltageOut], Sk150Registers.VoltageScale),
            CurrentOut = Sk150Registers.FromRaw(block[Sk150Registers.CurrentOut], Sk150Registers.CurrentScale),
            PowerOut = Sk150Registers.FromRaw(block[Sk150Registers.PowerOut], Sk150Registers.PowerScale),
            VoltageIn = Sk150Registers.FromRaw(block[Sk150Registers.VoltageIn], Sk150Registers.VoltageScale),
            CapacityMah = block[Sk150Registers.CapacityLow] | (block[Sk150Registers.CapacityHigh] << 16),
            EnergyMwh = block[Sk150Registers.EnergyLow] | (block[Sk150Registers.EnergyHigh] << 16),
            Runtime = new TimeSpan(
                block[Sk150Registers.RuntimeHours],
                block[Sk150Registers.RuntimeMinutes],
                block[Sk150Registers.RuntimeSeconds]),
            TemperatureIn = Sk150Registers.FromRaw(
                block[Sk150Registers.TemperatureIn], Sk150Registers.TemperatureScale),
            TemperatureEx = externalRaw == Sk150Registers.ExternalTemperatureNotConnected
                ? null
                : Sk150Registers.FromRaw(externalRaw, Sk150Registers.TemperatureScale),
            KeyLocked = block[Sk150Registers.KeyLock] != 0,
            ProtectionRaw = protectionRaw,
            Protection = Enum.IsDefined(typeof(Sk150ProtectionCode), protectionRaw)
                ? (Sk150ProtectionCode)protectionRaw
                : Sk150ProtectionCode.Unknown,
            ControlMode = block[Sk150Registers.ControlMode] == 1
                ? Sk150ControlMode.ConstantCurrent
                : Sk150ControlMode.ConstantVoltage,
            OutputEnabled = block[Sk150Registers.OutputEnable] != 0,
            TemperatureUnit = block[Sk150Registers.TemperatureUnit] == 1
                ? Sk150TemperatureUnit.Fahrenheit
                : Sk150TemperatureUnit.Celsius,
            Backlight = block[Sk150Registers.Backlight],
            SleepMinutes = block[Sk150Registers.SleepMinutes],
            ModelId = block[Sk150Registers.ModelId],
            FirmwareVersion = block[Sk150Registers.FirmwareVersion],
            SlaveAddress = (byte)block[Sk150Registers.SlaveAddress],
            BaudRateIndex = baudIndex,
            BaudRate = Sk150Registers.BaudRates.TryGetValue(baudIndex, out int baud) ? baud : null,
            TemperatureInCalibration = Sk150Registers.FromRaw(
                block[Sk150Registers.TemperatureInCalibration], Sk150Registers.TemperatureScale),
            TemperatureExCalibration = Sk150Registers.FromRaw(
                block[Sk150Registers.TemperatureExCalibration], Sk150Registers.TemperatureScale),
            BuzzerEnabled = block[Sk150Registers.Buzzer] != 0,
            Raw = block[..Sk150Registers.StatusBlockCount].ToArray(),
        };
    }

    /// <summary>Short human-readable summary of the live state.</summary>
    public override string ToString()
        => $"output {(OutputEnabled ? "ON" : "OFF")} {ControlMode} {VoltageOut:0.00} V {CurrentOut:0.000} A " +
           $"{PowerOut:0.00} W (set {VoltageSet:0.00} V / {CurrentSet:0.000} A), protection {Protection}.";
}
