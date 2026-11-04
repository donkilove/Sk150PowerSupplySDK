using Sk150PowerSupply.Tests.Fakes;

namespace Sk150PowerSupply.Tests;

/// <summary>
/// 设备发现的可注入路径：端口枚举与通道工厂均可替换，因此整个扫描过程无需真实硬件。
/// </summary>
public sealed class Sk150DiscoveryTests
{
    [Fact]
    public void GetPortNames_UsesTheInjectedProviderWhenSupplied()
    {
        IReadOnlyList<string> ports = Sk150Discovery.GetPortNames(() => ["COM9", "COM3"]);

        // 既有语义：端口名一律按名称不区分大小写排序后返回（COM3 在 COM9 之前）。
        Assert.Equal(["COM3", "COM9"], ports);
    }

    [Fact]
    public void GetPortNames_SortsTheProviderResultCaseInsensitively()
    {
        IReadOnlyList<string> ports = Sk150Discovery.GetPortNames(() => ["com9", "COM3"]);

        Assert.Equal(["COM3", "com9"], ports);
    }

    [Fact]
    public async Task FindAsync_ReturnsTheFirstProbeThatAnswers()
    {
        MockRegisterChannel device = MockRegisterChannel.CreateSk150();

        Sk150ProbeResult? hit = await Sk150Discovery.FindAsync(
            ports: ["COM1", "COM2"],
            baudRates: [115200],
            slaveAddresses: [1],
            channelFactory: _ => device);

        Assert.NotNull(hit);
        Assert.Equal("COM1", hit.PortName);
        Assert.Equal(115200, hit.BaudRate);
        Assert.Equal("SK150", hit.Device.ModelName);
    }

    [Fact]
    public async Task FindAsync_ReturnsNullWhenEveryProbeIsSilent()
    {
        // 每个端口的通道都注入超时（模拟线路静默），探针必须逐个跳过并最终返回 null。
        Sk150ProbeResult? hit = await Sk150Discovery.FindAsync(
            ports: ["COM1", "COM2"],
            baudRates: [115200],
            slaveAddresses: [1],
            channelFactory: _ =>
            {
                MockRegisterChannel silent = MockRegisterChannel.CreateSk150();
                silent.QueueTimeouts(int.MaxValue);
                return silent;
            });

        Assert.Null(hit);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsTheDeviceIdentityForAHealthyChannel()
    {
        MockRegisterChannel device = MockRegisterChannel.CreateSk150();

        Sk150DeviceInfo? info = await Sk150Discovery.ProbeAsync(
            "COM7",
            115200,
            1,
            responseTimeout: TimeSpan.FromMilliseconds(50),
            channelFactory: _ => device);

        Assert.NotNull(info);
        Assert.Equal(142, info.FirmwareVersion);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsNullWhenTheChannelThrows()
    {
        Sk150DeviceInfo? info = await Sk150Discovery.ProbeAsync(
            "COM7",
            115200,
            1,
            responseTimeout: TimeSpan.FromMilliseconds(50),
            channelFactory: _ => new ThrowingChannel());

        Assert.Null(info);
    }

    private sealed class ThrowingChannel : IRegisterChannel
    {
        public bool IsOpen => true;

        public Task OpenAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<ushort[]> ReadHoldingRegistersAsync(
            ushort startAddress,
            ushort count,
            CancellationToken cancellationToken = default)
            => throw new Sk150LinkException("端口不可用");

        public Task WriteSingleRegisterAsync(
            ushort address,
            ushort value,
            CancellationToken cancellationToken = default)
            => throw new Sk150LinkException("端口不可用");

        public Task WriteMultipleRegistersAsync(
            ushort startAddress,
            IReadOnlyList<ushort> values,
            CancellationToken cancellationToken = default)
            => throw new Sk150LinkException("端口不可用");

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
