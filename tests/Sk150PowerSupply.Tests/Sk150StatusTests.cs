using Sk150PowerSupply;

namespace Sk150PowerSupply.Tests;

/// <summary>Status-block decoding: scales, the NTC sentinel, and enum mapping.</summary>
public sealed class Sk150StatusTests
{
    private static ushort[] BuildBlock()
    {
        var block = new ushort[Sk150Registers.StatusBlockCount];
        block[0x00] = 1230;   // 12.30 V setpoint
        block[0x01] = 2000;   // 2.000 A limit
        block[0x02] = 1198;   // 11.98 V measured
        block[0x03] = 1234;   // 1.234 A measured
        block[0x04] = 1478;   // 14.78 W
        block[0x05] = 2461;   // 24.61 V input
        block[0x06] = 1000;   // capacity low
        block[0x07] = 0;
        block[0x08] = 1500;   // energy low (mWh)
        block[0x09] = 0;
        block[0x0A] = 0;
        block[0x0B] = 11;
        block[0x0C] = 49;
        block[0x0D] = 341;    // 34.1 C
        block[0x0E] = Sk150Registers.ExternalTemperatureNotConnected;
        block[0x0F] = 1;      // keys locked
        block[0x10] = 2;      // OCP
        block[0x11] = 1;      // CC
        block[0x12] = 1;      // output on
        block[0x13] = 0;      // Celsius
        block[0x14] = 5;
        block[0x15] = 0;
        block[0x16] = Sk150Registers.ModelIdSk150;
        block[0x17] = 142;
        block[0x18] = 1;
        block[0x19] = 6;      // 115200
        block[0x1A] = 0;
        block[0x1B] = 0;
        block[0x1C] = 1;
        block[0x1D] = 0;
        return block;
    }

    [Fact]
    public void FromRegisters_AppliesTheDocumentedScales()
    {
        Sk150Status status = Sk150Status.FromRegisters(BuildBlock());

        Assert.Equal(12.30m, status.VoltageSet);
        Assert.Equal(2.000m, status.CurrentSet);
        Assert.Equal(11.98m, status.VoltageOut);
        Assert.Equal(1.234m, status.CurrentOut);
        Assert.Equal(14.78m, status.PowerOut);
        Assert.Equal(24.61m, status.VoltageIn);
        Assert.Equal(34.1m, status.TemperatureIn);
    }

    [Fact]
    public void FromRegisters_CombinesTheAccumulatorHalvesAndRuntime()
    {
        ushort[] block = BuildBlock();
        block[0x06] = 0x0001;
        block[0x07] = 0x0002;   // (2 << 16) | 1 = 131073 mAh
        block[0x08] = 0x0010;
        block[0x09] = 0x0000;

        Sk150Status status = Sk150Status.FromRegisters(block);

        Assert.Equal(131073, status.CapacityMah);
        Assert.Equal(131.073m, status.CapacityAh);
        Assert.Equal(16, status.EnergyMwh);
        Assert.Equal(0.016m, status.EnergyWh);
        Assert.Equal(new TimeSpan(0, 11, 49), status.Runtime);
    }

    [Fact]
    public void FromRegisters_ReportsNoProbeWhenTheTemperatureRegisterHoldsTheSentinel()
    {
        Sk150Status status = Sk150Status.FromRegisters(BuildBlock());

        Assert.Null(status.TemperatureEx);
    }

    [Fact]
    public void FromRegisters_DecodesAConnectedProbe()
    {
        ushort[] block = BuildBlock();
        block[0x0E] = 253;   // 25.3 in the configured unit

        Sk150Status status = Sk150Status.FromRegisters(block);

        Assert.Equal(25.3m, status.TemperatureEx);
    }

    [Fact]
    public void FromRegisters_MapsProtectionModeOutputAndIdentity()
    {
        Sk150Status status = Sk150Status.FromRegisters(BuildBlock());

        Assert.Equal(Sk150ProtectionCode.OverCurrent, status.Protection);
        Assert.Equal(2, status.ProtectionRaw);
        Assert.Equal(Sk150ControlMode.ConstantCurrent, status.ControlMode);
        Assert.True(status.OutputEnabled);
        Assert.True(status.KeyLocked);
        Assert.True(status.BuzzerEnabled);
        Assert.Equal(Sk150TemperatureUnit.Celsius, status.TemperatureUnit);
        Assert.Equal(Sk150Registers.ModelIdSk150, status.ModelId);
        Assert.Equal(142, status.FirmwareVersion);
        Assert.Equal(1, status.SlaveAddress);
        Assert.Equal(115200, status.BaudRate);
        Assert.Equal(5, status.Backlight);
    }

    [Fact]
    public void FromRegisters_FlagsAnUndocumentedProtectionCodeInsteadOfGuessing()
    {
        ushort[] block = BuildBlock();
        block[0x10] = 99;

        Sk150Status status = Sk150Status.FromRegisters(block);

        Assert.Equal(Sk150ProtectionCode.Unknown, status.Protection);
        Assert.Equal(99, status.ProtectionRaw);
    }

    [Fact]
    public void FromRegisters_RejectsAShortBlock()
    {
        var shortBlock = new ushort[Sk150Registers.StatusBlockCount - 1];

        Assert.Throws<ArgumentException>(() => Sk150Status.FromRegisters(shortBlock));
    }

    [Fact]
    public void FromRegisters_KeepsTheRawRegistersForDiagnostics()
    {
        Sk150Status status = Sk150Status.FromRegisters(BuildBlock());

        Assert.Equal(Sk150Registers.StatusBlockCount, status.Raw.Count);
        Assert.Equal(1230, status.Raw[0x00]);
    }
}
