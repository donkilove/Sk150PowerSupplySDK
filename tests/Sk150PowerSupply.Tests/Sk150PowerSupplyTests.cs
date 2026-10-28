using Sk150PowerSupply;
using Psu = Sk150PowerSupply.Sk150PowerSupply;

namespace Sk150PowerSupply.Tests;

/// <summary>Facade behaviour against the in-memory device: scaling, guards, and register traffic.</summary>
public sealed class Sk150PowerSupplyTests
{
    private static (Psu Device, MockRegisterChannel Mock) Create(Sk150Options? options = null)
    {
        MockRegisterChannel mock = MockRegisterChannel.CreateSk150();
        return (new Psu(mock, options), mock);
    }

    [Fact]
    public async Task ReadStatusAsync_DecodesTheDeviceSnapshot()
    {
        (Psu psu, _) = Create();

        Sk150Status status = await psu.ReadStatusAsync();

        Assert.Equal(3.30m, status.VoltageSet);
        Assert.Equal(0.100m, status.CurrentSet);
        Assert.Equal(2461 / 100m, status.VoltageIn);
        Assert.True(status.OutputEnabled);
        Assert.Equal(Sk150Registers.ModelIdSk150, status.ModelId);
        Assert.Null(status.TemperatureEx);
    }

    [Fact]
    public async Task ReadDeviceInfoAsync_ReportsTheIdentityAndProbeState()
    {
        (Psu psu, _) = Create();

        Sk150DeviceInfo info = await psu.ReadDeviceInfoAsync();

        Assert.Equal("SK150", info.ModelName);
        Assert.Equal(142, info.FirmwareVersion);
        Assert.Equal(1, info.SlaveAddress);
        Assert.Equal(115200, info.BaudRate);
        Assert.False(info.HasExternalProbe);
    }

    [Fact]
    public async Task SetVoltageAsync_WritesTheScaledSetpoint()
    {
        (Psu psu, MockRegisterChannel mock) = Create();

        await psu.SetVoltageAsync(12.5m);

        Assert.Equal(1250, mock[Sk150Registers.VoltageSet]);
    }

    [Fact]
    public async Task SetCurrentAsync_WritesMilliampResolution()
    {
        (Psu psu, MockRegisterChannel mock) = Create();

        await psu.SetCurrentAsync(1.234m);

        Assert.Equal(1234, mock[Sk150Registers.CurrentSet]);
    }

    [Fact]
    public async Task SetVoltageAsync_RejectsValuesAboveTheNameplateGuardWithoutTouchingTheDevice()
    {
        (Psu psu, MockRegisterChannel mock) = Create();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => psu.SetVoltageAsync(40m));

        Assert.Empty(mock.Writes);
    }

    [Fact]
    public async Task SetCurrentAsync_RejectsValuesAboveTheNameplateGuard()
    {
        (Psu psu, _) = Create();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => psu.SetCurrentAsync(6m));
    }

    [Fact]
    public async Task SetVoltageAsync_AcceptsAnyRegisterValueWhenLimitsAreDisabled()
    {
        (Psu psu, MockRegisterChannel mock) = Create(new Sk150Options { SafetyLimits = Sk150SafetyLimits.None });

        await psu.SetVoltageAsync(40m);

        Assert.Equal(4000, mock[Sk150Registers.VoltageSet]);
    }

    [Fact]
    public async Task SetVoltageAndCurrentAsync_WritesBothSetpointsInOneTransaction()
    {
        (Psu psu, MockRegisterChannel mock) = Create();

        await psu.SetVoltageAndCurrentAsync(5m, 1.5m);

        Assert.Equal(2, mock.Writes.Count);
        Assert.All(mock.Writes, write => Assert.True(write.IsBlock));
        Assert.Equal((ushort)500, mock[Sk150Registers.VoltageSet]);
        Assert.Equal((ushort)1500, mock[Sk150Registers.CurrentSet]);
    }

    [Fact]
    public async Task TurnOnAndOff_WriteTheOutputRegister()
    {
        (Psu psu, MockRegisterChannel mock) = Create();

        await psu.TurnOffAsync();
        Assert.Equal(0, mock[Sk150Registers.OutputEnable]);

        await psu.TurnOnAsync();
        Assert.Equal(1, mock[Sk150Registers.OutputEnable]);
    }

    [Fact]
    public async Task LoadPresetAsync_WritesTheGroupIndex()
    {
        (Psu psu, MockRegisterChannel mock) = Create();

        await psu.LoadPresetAsync(4);

        Assert.Equal(4, mock[Sk150Registers.LoadPreset]);
    }

    [Fact]
    public async Task LoadPresetAsync_RejectsAnIndexOutsideTheGroups()
    {
        (Psu psu, MockRegisterChannel mock) = Create();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => psu.LoadPresetAsync(10));

        Assert.Empty(mock.Writes);
    }

    [Fact]
    public async Task WritePresetAsync_StoresAllFourteenRegistersAtTheGroupAddress()
    {
        (Psu psu, MockRegisterChannel mock) = Create();
        var preset = new Sk150Preset { Index = 2, Voltage = 9.00m, Current = 1.000m, OverVoltageProtection = 12m };

        await psu.WritePresetAsync(2, preset);

        Assert.Equal(Sk150Registers.PresetRegisterCount, mock.Writes.Count);
        Assert.All(mock.Writes, write => Assert.True(write.IsBlock));
        Assert.Equal((ushort)900, mock[Sk150Registers.PresetAddress(2)]);
        Assert.Equal((ushort)1000, mock[(ushort)(Sk150Registers.PresetAddress(2) + 1)]);
        Assert.Equal((ushort)1200, mock[(ushort)(Sk150Registers.PresetAddress(2) + 3)]);
    }

    [Fact]
    public async Task WritePresetAsync_RejectsAPresetWhoseIndexDoesNotMatchTheGroup()
    {
        (Psu psu, MockRegisterChannel mock) = Create();
        var preset = new Sk150Preset { Index = 3, Voltage = 9m };

        await Assert.ThrowsAsync<ArgumentException>(() => psu.WritePresetAsync(2, preset));

        Assert.Empty(mock.Writes);
    }

    [Fact]
    public async Task ReadPresetAsync_ReadsTheRequestedGroup()
    {
        (Psu psu, MockRegisterChannel mock) = Create();
        mock[Sk150Registers.PresetAddress(5)] = 2400;
        mock[(ushort)(Sk150Registers.PresetAddress(5) + 1)] = 750;
        mock[(ushort)(Sk150Registers.PresetAddress(5) + 5)] = 155;

        Sk150Preset preset = await psu.ReadPresetAsync(5);

        Assert.Equal(24.00m, preset.Voltage);
        Assert.Equal(0.750m, preset.Current);
        Assert.Equal(15.5m, preset.OverPowerProtection);
    }

    [Fact]
    public async Task SetBacklightAsync_RejectsLevelsOutsideThePanelRange()
    {
        (Psu psu, MockRegisterChannel mock) = Create();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => psu.SetBacklightAsync(6));
        Assert.Empty(mock.Writes);

        await psu.SetBacklightAsync(3);
        Assert.Equal(3, mock[Sk150Registers.Backlight]);
    }

    [Fact]
    public async Task SetSlaveAddressAsync_RejectsZero()
    {
        (Psu psu, _) = Create();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => psu.SetSlaveAddressAsync(0));
    }

    [Fact]
    public async Task SetBaudRateAsync_RejectsRatesTheDeviceCannotStore()
    {
        (Psu psu, _) = Create();

        await Assert.ThrowsAsync<ArgumentException>(() => psu.SetBaudRateAsync(12345));
    }

    [Fact]
    public async Task SetBaudRateAsync_WritesTheDocumentedIndex()
    {
        (Psu psu, MockRegisterChannel mock) = Create();

        await psu.SetBaudRateAsync(9600);

        Assert.Equal(0, mock[Sk150Registers.BaudRateIndex]);
    }

    [Fact]
    public async Task ReadStatusAsync_SurfacesInjectedTimeouts()
    {
        (Psu psu, MockRegisterChannel mock) = Create();
        mock.QueueTimeouts(1);

        await Assert.ThrowsAsync<Sk150TimeoutException>(() => psu.ReadStatusAsync());
    }

    [Fact]
    public async Task MonitorAsync_YieldsSnapshotsUntilTheConsumerStops()
    {
        (Psu psu, _) = Create();
        int seen = 0;

        await foreach (Sk150Status status in psu.MonitorAsync(TimeSpan.Zero))
        {
            Assert.Equal(3.30m, status.VoltageOut);
            if (++seen == 3)
            {
                break;
            }
        }

        Assert.Equal(3, seen);
    }

    [Fact]
    public async Task Dispose_ClosesTheEndpointForAnOwnedChannelOnly()
    {
        (Psu psu, MockRegisterChannel mock) = Create();

        await psu.DisposeAsync();

        Assert.True(mock.IsOpen);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => psu.ReadStatusAsync());
    }

    [Fact]
    public void CreateMock_ReturnsAUsableDevice()
    {
        using Psu psu = Psu.CreateMock();

        Assert.True(psu.IsOpen);
    }
}
