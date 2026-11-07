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

    // ================= CR-1 SP-01：探测注入路径必须释放它自己创建的通道 =================
    // 缺陷：OpenOverChannelAsync 用**公开构造函数**包门面（_ownsChannel=false），
    // 而 Sk150PowerSupply.DisposeAsync 在 !_ownsChannel 时直接 return（什么都不释放）。
    // 于是探测成功后串口句柄永久持有 —— 正是 M83 修过的同一类坑（COM in use，等 GC 终结器）。
    // 修复：私有构造 + ownsChannel: true（探测语义下门面本就该释放工厂造出的通道）。

    /// <summary>可观测的通道：记录 Open/Close/Dispose 次数，行为同 MockRegisterChannel。</summary>
    private sealed class ObservingChannel(MockRegisterChannel inner) : IRegisterChannel
    {
        public int OpenCount { get; private set; }

        public int CloseCount { get; private set; }

        public int DisposeCount { get; private set; }

        public bool IsOpen => inner.IsOpen;

        /// <summary>转发到内层的超时注入（模拟线路静默）。</summary>
        public void QueueTimeouts(int count) => inner.QueueTimeouts(count);

        public Task OpenAsync(CancellationToken cancellationToken = default)
        {
            OpenCount++;
            return inner.OpenAsync(cancellationToken);
        }

        public Task CloseAsync(CancellationToken cancellationToken = default)
        {
            CloseCount++;
            return inner.CloseAsync(cancellationToken);
        }

        public Task<ushort[]> ReadHoldingRegistersAsync(
            ushort startAddress,
            ushort count,
            CancellationToken cancellationToken = default)
            => inner.ReadHoldingRegistersAsync(startAddress, count, cancellationToken);

        public Task WriteSingleRegisterAsync(
            ushort address,
            ushort value,
            CancellationToken cancellationToken = default)
            => inner.WriteSingleRegisterAsync(address, value, cancellationToken);

        public Task WriteMultipleRegistersAsync(
            ushort startAddress,
            IReadOnlyList<ushort> values,
            CancellationToken cancellationToken = default)
            => inner.WriteMultipleRegistersAsync(startAddress, values, cancellationToken);

        public void Dispose() => DisposeCount++;

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return inner.DisposeAsync();
        }
    }

    [Fact]
    public async Task ProbeAsync_WithChannelFactory_DisposesTheChannelItCreated()   // SP-01 AC-1
    {
        var channel = new ObservingChannel(MockRegisterChannel.CreateSk150());

        Sk150DeviceInfo? info = await Sk150Discovery.ProbeAsync(
            "COM7",
            115200,
            1,
            responseTimeout: TimeSpan.FromMilliseconds(50),
            channelFactory: _ => channel);

        Assert.NotNull(info);                 // 探测成功
        Assert.Equal(1, channel.OpenCount);
        Assert.Equal(1, channel.DisposeCount);   // ← 修复前是 0（句柄泄漏）
    }

    [Fact]
    public async Task FindAsync_WithChannelFactory_DisposesTheChannelItHit()   // SP-01 AC-2
    {
        var channel = new ObservingChannel(MockRegisterChannel.CreateSk150());

        Sk150ProbeResult? hit = await Sk150Discovery.FindAsync(
            ports: ["COM1"],
            baudRates: [115200],
            slaveAddresses: [1],
            channelFactory: _ => channel);

        Assert.NotNull(hit);
        Assert.Equal(1, channel.DisposeCount);   // ← 修复前是 0
    }

    [Fact]
    public async Task ProbeAsync_WithChannelFactory_FailureStillDisposesChannel()   // SP-01 AC-3（不回归）
    {
        // 探测失败路径：现有 catch 里的 channel.DisposeAsync() 必须仍然生效
        var channel = new ObservingChannel(MockRegisterChannel.CreateSk150());
        channel.QueueTimeouts(int.MaxValue);

        Sk150DeviceInfo? info = await Sk150Discovery.ProbeAsync(
            "COM7",
            115200,
            1,
            responseTimeout: TimeSpan.FromMilliseconds(20),
            channelFactory: _ => channel);

        Assert.Null(info);
        Assert.Equal(1, channel.DisposeCount);   // 失败路径本就该释放
    }

    [Fact]
    public async Task ProbeAsync_WithoutChannelFactory_StillWorks()   // SP-01 AC-4 不回归
    {
        // 不注入工厂时走 OpenAsync（既有路径），行为不变——用判决性弱的断言锁"不抛算术异常"
        // 真实串口不可用，故此处只验证"没有工厂时不因构造参数变化而崩溃"。
        var ex = await Record.ExceptionAsync(async () =>
            await Sk150Discovery.ProbeAsync(
                "COM_DOES_NOT_EXIST", 115200, 1, TimeSpan.FromMilliseconds(20)));

        // 端口不存在 → 内部失败 → 返回 null（而不是把异常抛给调用方）
        Assert.Null(ex);
    }

    // ================= CR-1 SK-01/SK-04/SK-05：异常覆盖与所有权契约 =================

    [Fact]
    public async Task FindAsync_SingletonFactory_MultipleCandidates_DoesNotThrow()   // SK-01
    {
        // 缺陷：FindAsync 对每个候选调一次 ProbeAsync，每次都释放通道。
        // 若 channelFactory 返回**同一实例**，第二轮拿到已 Disposed 的通道 →
        // ObjectDisposedException 不在 ProbeAsync:77 的过滤器内 → 冒泡出 FindAsync。
        // 修复：① ProbeAsync 捕获该异常视为「该候选失败」；② 文档明确「工厂须每次新建」。
        var channel = new DisposableObservingChannel(MockRegisterChannel.CreateSk150());

        var ex = await Record.ExceptionAsync(async () =>
            await Sk150Discovery.FindAsync(
                ports: ["COM1"],
                baudRates: [115200, 9600],          // 两个候选 → 必然第二轮
                slaveAddresses: [1],
                channelFactory: _ => channel));     // 单例工厂

        Assert.Null(ex);   // 修复前：ObjectDisposedException 冒泡
    }

    [Fact]
    public async Task ProbeAsync_DisposeThrows_ResultStillDeterminedByRead()   // SK-04
    {
        // 缺陷：await using 的 DisposeAsync 异常先于 :77 过滤器发生 →
        // 若 DisposeAsync 抛 IOException，会命中过滤器 → 返回 null，
        // 而 ReadDeviceInfoAsync 其实已成功 → FindAsync 会跳过真实在线设备。
        var channel = new ThrowingOnDisposeChannel(MockRegisterChannel.CreateSk150());

        var info = await Sk150Discovery.ProbeAsync(
            "COM7", 115200, 1, TimeSpan.FromMilliseconds(50), channelFactory: _ => channel);

        // 设备已应答 ⇒ 必须返回非 null，不能因释放失败而判成"探测失败"
        Assert.NotNull(info);
    }

    [Fact]
    public async Task DisposeAsync_NeverThrows_OnOwnedChannel()   // SK-05
    {
        // DisposeAsync 原只 catch Sk150LinkException；注入通道可抛其它类型，
        // 会穿透并替换调用的原异常。释放路径必须永不外抛。
        var channel = new ThrowingOnDisposeChannel(MockRegisterChannel.CreateSk150());

        // 直接构造 ownsChannel=true 的门面并释放（internal 构造，同程序集测试可见）
        var ex = await Record.ExceptionAsync(async () =>
        {
            var psu = new Sk150PowerSupply(channel, new Sk150Options(), ownsChannel: true);
            await psu.DisposeAsync();
        });

        Assert.Null(ex);   // 修复前：ObjectDisposedException 外抛
    }

    /// <summary>
    /// 真实会「Dispose 后不可用」的通道：DisposeAsync 后 OpenAsync 抛 ObjectDisposedException。
    /// 用于复现「单例工厂 + 多候选」时第二轮撞上已释放通道的路径。
    /// </summary>
    private sealed class DisposableObservingChannel(MockRegisterChannel inner) : IRegisterChannel
    {
        private bool _disposed;

        public bool IsOpen => !_disposed && inner.IsOpen;

        public async Task OpenAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await inner.OpenAsync(cancellationToken);
            // 打开成功但读不到数据：使本轮探测失败，同时通道已进入「打开过」状态
            inner.QueueTimeouts(int.MaxValue);
        }

        public Task CloseAsync(CancellationToken cancellationToken = default)
            => inner.CloseAsync(cancellationToken);

        public async Task<ushort[]> ReadHoldingRegistersAsync(
            ushort startAddress, ushort count, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return await inner.ReadHoldingRegistersAsync(startAddress, count, cancellationToken);
        }

        public Task WriteSingleRegisterAsync(
            ushort address, ushort value, CancellationToken cancellationToken = default)
            => inner.WriteSingleRegisterAsync(address, value, cancellationToken);

        public Task WriteMultipleRegistersAsync(
            ushort startAddress, IReadOnlyList<ushort> values, CancellationToken cancellationToken = default)
            => inner.WriteMultipleRegistersAsync(startAddress, values, cancellationToken);

        public void Dispose() => _disposed = true;

        public ValueTask DisposeAsync()
        {
            _disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>DisposeAsync 抛非 Sk150LinkException 的通道（模拟注入实现的异常）。</summary>
    private sealed class ThrowingOnDisposeChannel(MockRegisterChannel inner) : IRegisterChannel
    {
        public bool IsOpen => inner.IsOpen;

        public Task OpenAsync(CancellationToken cancellationToken = default) => inner.OpenAsync(cancellationToken);

        public Task CloseAsync(CancellationToken cancellationToken = default)
            => throw new ObjectDisposedException("simulated close failure");

        public Task<ushort[]> ReadHoldingRegistersAsync(
            ushort startAddress, ushort count, CancellationToken cancellationToken = default)
            => inner.ReadHoldingRegistersAsync(startAddress, count, cancellationToken);

        public Task WriteSingleRegisterAsync(
            ushort address, ushort value, CancellationToken cancellationToken = default)
            => inner.WriteSingleRegisterAsync(address, value, cancellationToken);

        public Task WriteMultipleRegistersAsync(
            ushort startAddress, IReadOnlyList<ushort> values, CancellationToken cancellationToken = default)
            => inner.WriteMultipleRegistersAsync(startAddress, values, cancellationToken);

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => throw new ObjectDisposedException("simulated dispose failure");
    }
}
