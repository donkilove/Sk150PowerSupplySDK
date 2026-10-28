namespace Sk150PowerSupply;

/// <summary>
/// One stored setup group (M0-M9): output setpoints plus the protection thresholds that are
/// applied when the group is loaded.
/// </summary>
/// <remarks>
/// Groups live at 0x0050 + index * 0x0010 and occupy 14 registers (offsets 0x00-0x0D).
/// M0 is the power-on group; M1 and M2 are the quick-access groups on the front panel.
/// </remarks>
public sealed record Sk150Preset
{
    /// <summary>Group index, 0-9 (M0-M9).</summary>
    public int Index { get; init; }

    /// <summary>Preset output voltage, in volts (offset 0x00, scale 100).</summary>
    public decimal Voltage { get; init; }

    /// <summary>Preset current limit, in amperes (offset 0x01, scale 1000).</summary>
    public decimal Current { get; init; }

    /// <summary>Low input voltage protection threshold, in volts (offset 0x02, scale 100).</summary>
    public decimal LowVoltageProtection { get; init; }

    /// <summary>Over voltage protection threshold, in volts (offset 0x03, scale 100).</summary>
    public decimal OverVoltageProtection { get; init; }

    /// <summary>Over current protection threshold, in amperes (offset 0x04, scale 1000).</summary>
    public decimal OverCurrentProtection { get; init; }

    /// <summary>Over power protection threshold, in watts (offset 0x05, scale 10).</summary>
    public decimal OverPowerProtection { get; init; }

    /// <summary>Maximum runtime hours (offset 0x06).</summary>
    public int MaxRuntimeHours { get; init; }

    /// <summary>Maximum runtime minutes (offset 0x07).</summary>
    public int MaxRuntimeMinutes { get; init; }

    /// <summary>Maximum accumulated charge, in milliampere-hours (offsets 0x08-0x09).</summary>
    public int MaxCapacityMah { get; init; }

    /// <summary>Maximum accumulated energy, in 10 mWh units (offsets 0x0A-0x0B).</summary>
    public int MaxEnergy10Mwh { get; init; }

    /// <summary>Over temperature protection threshold (offset 0x0C), in the configured unit.</summary>
    public decimal OverTemperatureProtection { get; init; }

    /// <summary>Output state applied at power-on for this group (offset 0x0D).</summary>
    public bool PowerOnOutputEnabled { get; init; }

    /// <summary>Maximum runtime as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan MaxRuntime => new(MaxRuntimeHours, MaxRuntimeMinutes, 0);

    /// <summary>Decodes a preset group read from <see cref="Sk150Registers.PresetAddress"/>.</summary>
    /// <param name="index">Group index, 0-9.</param>
    /// <param name="registers">At least <see cref="Sk150Registers.PresetRegisterCount"/> registers.</param>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside 0-9.</exception>
    /// <exception cref="ArgumentException">The block is shorter than one preset group.</exception>
    public static Sk150Preset FromRegisters(int index, ReadOnlySpan<ushort> registers)
    {
        if (index is < 0 or > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Preset groups are M0-M9.");
        }

        if (registers.Length < Sk150Registers.PresetRegisterCount)
        {
            throw new ArgumentException(
                $"A preset group needs {Sk150Registers.PresetRegisterCount} registers, got {registers.Length}.",
                nameof(registers));
        }

        return new Sk150Preset
        {
            Index = index,
            Voltage = registers[0x00] / Sk150Registers.VoltageScale,
            Current = registers[0x01] / Sk150Registers.CurrentScale,
            LowVoltageProtection = registers[0x02] / Sk150Registers.VoltageScale,
            OverVoltageProtection = registers[0x03] / Sk150Registers.VoltageScale,
            OverCurrentProtection = registers[0x04] / Sk150Registers.CurrentScale,
            OverPowerProtection = registers[0x05] / Sk150Registers.PresetPowerScale,
            MaxRuntimeHours = registers[0x06],
            MaxRuntimeMinutes = registers[0x07],
            MaxCapacityMah = registers[0x08] | (registers[0x09] << 16),
            MaxEnergy10Mwh = registers[0x0A] | (registers[0x0B] << 16),
            OverTemperatureProtection = registers[0x0C],
            PowerOnOutputEnabled = registers[0x0D] != 0,
        };
    }

    /// <summary>Encodes this group into the 14 registers it occupies on the device.</summary>
    public ushort[] ToRegisters()
    {
        var registers = new ushort[Sk150Registers.PresetRegisterCount];
        registers[0x00] = Sk150Registers.ToRaw(Voltage, Sk150Registers.VoltageScale);
        registers[0x01] = Sk150Registers.ToRaw(Current, Sk150Registers.CurrentScale);
        registers[0x02] = Sk150Registers.ToRaw(LowVoltageProtection, Sk150Registers.VoltageScale);
        registers[0x03] = Sk150Registers.ToRaw(OverVoltageProtection, Sk150Registers.VoltageScale);
        registers[0x04] = Sk150Registers.ToRaw(OverCurrentProtection, Sk150Registers.CurrentScale);
        registers[0x05] = Sk150Registers.ToRaw(OverPowerProtection, Sk150Registers.PresetPowerScale);
        registers[0x06] = (ushort)MaxRuntimeHours;
        registers[0x07] = (ushort)MaxRuntimeMinutes;
        registers[0x08] = (ushort)(MaxCapacityMah & 0xFFFF);
        registers[0x09] = (ushort)((MaxCapacityMah >> 16) & 0xFFFF);
        registers[0x0A] = (ushort)(MaxEnergy10Mwh & 0xFFFF);
        registers[0x0B] = (ushort)((MaxEnergy10Mwh >> 16) & 0xFFFF);
        registers[0x0C] = Sk150Registers.ToRaw(OverTemperatureProtection, 1m);
        registers[0x0D] = PowerOnOutputEnabled ? (ushort)1 : (ushort)0;
        return registers;
    }

    /// <summary>Short human-readable summary of the group.</summary>
    public override string ToString()
        => $"M{Index}: {Voltage:0.00} V / {Current:0.000} A, OVP {OverVoltageProtection:0.00} V, " +
           $"OCP {OverCurrentProtection:0.000} A, OPP {OverPowerProtection:0.0} W.";
}
