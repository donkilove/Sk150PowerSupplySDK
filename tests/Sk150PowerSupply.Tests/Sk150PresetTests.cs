using Sk150PowerSupply;

namespace Sk150PowerSupply.Tests;

/// <summary>Preset group encoding and decoding, and the device facade against the mock channel.</summary>
public sealed class Sk150PresetTests
{
    [Fact]
    public void ToRegisters_AppliesTheDocumentedScales()
    {
        var preset = new Sk150Preset
        {
            Index = 3,
            Voltage = 12.50m,
            Current = 2.000m,
            LowVoltageProtection = 8.00m,
            OverVoltageProtection = 15.00m,
            OverCurrentProtection = 2.500m,
            OverPowerProtection = 30.0m,
            MaxRuntimeHours = 1,
            MaxRuntimeMinutes = 30,
            MaxCapacityMah = 0x0001_0002,
            MaxEnergy10Mwh = 0x0000_0010,
            OverTemperatureProtection = 80m,
            PowerOnOutputEnabled = true,
        };

        ushort[] registers = preset.ToRegisters();

        Assert.Equal(Sk150Registers.PresetRegisterCount, registers.Length);
        Assert.Equal(1250, registers[0x00]);
        Assert.Equal(2000, registers[0x01]);
        Assert.Equal(800, registers[0x02]);
        Assert.Equal(1500, registers[0x03]);
        Assert.Equal(2500, registers[0x04]);
        Assert.Equal(300, registers[0x05]);   // OPP is scaled by 10
        Assert.Equal(1, registers[0x06]);
        Assert.Equal(30, registers[0x07]);
        Assert.Equal(0x0002, registers[0x08]);
        Assert.Equal(0x0001, registers[0x09]);
        Assert.Equal(0x0010, registers[0x0A]);
        Assert.Equal(0x0000, registers[0x0B]);
        Assert.Equal(80, registers[0x0C]);
        Assert.Equal(1, registers[0x0D]);
    }

    [Fact]
    public void RoundTrip_PreservesEveryField()
    {
        var preset = new Sk150Preset
        {
            Index = 7,
            Voltage = 5.00m,
            Current = 1.500m,
            LowVoltageProtection = 4.00m,
            OverVoltageProtection = 6.00m,
            OverCurrentProtection = 2.000m,
            OverPowerProtection = 12.5m,
            MaxRuntimeHours = 2,
            MaxRuntimeMinutes = 15,
            MaxCapacityMah = 5000,
            MaxEnergy10Mwh = 2000,
            OverTemperatureProtection = 65m,
            PowerOnOutputEnabled = false,
        };

        Sk150Preset decoded = Sk150Preset.FromRegisters(7, preset.ToRegisters());

        Assert.Equal(preset, decoded);
        Assert.Equal(new TimeSpan(2, 15, 0), decoded.MaxRuntime);
    }

    [Fact]
    public void FromRegisters_RejectsAnIndexOutsideTheGroups()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Sk150Preset.FromRegisters(10, new ushort[Sk150Registers.PresetRegisterCount]));
    }

    [Fact]
    public void FromRegisters_RejectsAShortBlock()
    {
        Assert.Throws<ArgumentException>(
            () => Sk150Preset.FromRegisters(0, new ushort[Sk150Registers.PresetRegisterCount - 1]));
    }

    [Fact]
    public void PresetAddress_StepsByOneStridePerGroup()
    {
        Assert.Equal(0x0050, Sk150Registers.PresetAddress(0));
        Assert.Equal(0x0060, Sk150Registers.PresetAddress(1));
        Assert.Equal(0x0071, (ushort)(Sk150Registers.PresetAddress(2) + 1));
        Assert.Equal(0x00E0, Sk150Registers.PresetAddress(9));
        Assert.Throws<ArgumentOutOfRangeException>(() => Sk150Registers.PresetAddress(-1));
    }
}
