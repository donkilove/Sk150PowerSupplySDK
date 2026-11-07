using System.IO.Ports;

namespace Sk150PowerSupply;

/// <summary>A device that answered during a scan.</summary>
/// <param name="PortName">Serial port it answered on.</param>
/// <param name="BaudRate">Link speed that worked.</param>
/// <param name="SlaveAddress">MODBUS slave address that answered.</param>
/// <param name="Device">Identity read from the device.</param>
public sealed record Sk150ProbeResult(
    string PortName,
    int BaudRate,
    byte SlaveAddress,
    Sk150DeviceInfo Device)
{
    /// <summary>Short human-readable summary of the hit.</summary>
    public override string ToString()
        => $"{PortName} @ {BaudRate} 8N1 slave {SlaveAddress}: {Device}";
}

/// <summary>
/// Finds an SK150 on the machine's serial ports. Scanning opens each candidate port and sends one
/// read; ports that are busy, missing, or silent are skipped.
/// </summary>
public static class Sk150Discovery
{
    /// <summary>Baud rates tried by default, in the order they are attempted.</summary>
    public static IReadOnlyList<int> DefaultBaudRates { get; } = [115200, 9600, 38400, 19200, 57600];

    /// <summary>Slave addresses tried by default.</summary>
    public static IReadOnlyList<byte> DefaultSlaveAddresses { get; } = [1, 2, 3];

    /// <summary>Returns the machine's serial port names, sorted.</summary>
    /// <param name="portNameProvider">
    /// Where the names come from; defaults to <see cref="SerialPort.GetPortNames"/>. Supplying a
    /// provider makes discovery testable without touching real hardware.
    /// </param>
    public static IReadOnlyList<string> GetPortNames(Func<IEnumerable<string>>? portNameProvider = null)
        => (portNameProvider?.Invoke() ?? SerialPort.GetPortNames())
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>Checks one port/baud/slave combination and returns the identity, or null when silent.</summary>
    /// <param name="portName">Serial port to probe.</param>
    /// <param name="baudRate">Link speed to try.</param>
    /// <param name="slaveAddress">Slave address to try.</param>
    /// <param name="responseTimeout">How long to wait for the answer; 300 ms by default.</param>
    /// <param name="channelFactory">
    /// Builds the transport to probe through; defaults to a real serial channel. Supplying a
    /// factory lets a host application reuse its own port implementation.
    ///
    /// <para><b>CR-1 SK-02 — 所有权契约（重要）</b>：工厂返回的通道**所有权转移给本方法**。
    /// 探测结束（无论成功或失败）都会 <c>CloseAsync + DisposeAsync</c> 该通道。
    /// 因此工厂**必须每次调用都返回新实例**——返回同一个单例会让第二次探测撞上已释放的通道
    /// （虽然已被 SK-01 兜住为「该候选失败」，但那是容错而非正确用法）。</para>
    ///
    /// <para>这是相对 0.2.0 的**行为变更**：0.2.0 探测成功后不释放（串口句柄泄漏，SP-01），
    /// 那时宿主可继续复用同一通道；0.2.1 起探测会接管并销毁，宿主须自行重开。</para>
    /// </param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    public static async Task<Sk150DeviceInfo?> ProbeAsync(
        string portName,
        int baudRate = 115200,
        byte slaveAddress = 1,
        TimeSpan? responseTimeout = null,
        Func<Sk150Options, IRegisterChannel>? channelFactory = null,
        CancellationToken cancellationToken = default)
    {
        var options = new Sk150Options
        {
            PortName = portName,
            BaudRate = baudRate,
            SlaveAddress = slaveAddress,
            ResponseTimeout = responseTimeout ?? TimeSpan.FromMilliseconds(300),
            Retries = 1,
        };

        try
        {
            await using Sk150PowerSupply psu = channelFactory is null
                ? await Sk150PowerSupply.OpenAsync(options, cancellationToken).ConfigureAwait(false)
                : await OpenOverChannelAsync(options, channelFactory, cancellationToken).ConfigureAwait(false);
            return await psu.ReadDeviceInfoAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is Sk150LinkException or ModbusException or IOException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            // CR-1 SK-01：单例工厂（错误用法）在多候选场景下，第二轮会拿到已释放的通道。
            // 该异常不在上面的过滤器内，原本会冒泡出 FindAsync 让整个扫描崩掉。
            // 视为「该候选失败、继续扫下一个」，扫描语义不变（busy/missing/silent 一律 skip）。
            return null;
        }
    }

    /// <summary>Opens a facade over a caller-supplied transport, probing once like the serial path.</summary>
    /// <remarks>
    /// CR-1 SP-01：门面**必须拥有**它自己创建的通道。原实现用公开构造函数
    /// <c>new Sk150PowerSupply(channel, options)</c>（该重载有意置 <c>_ownsChannel=false</c>），
    /// 于是 <see cref="Sk150PowerSupply.DisposeAsync"/> 在 <c>!_ownsChannel</c> 时直接返回、
    /// 什么都不释放 —— 探测成功后串口句柄被永久持有（现场表现为 <c>COMx in use</c>，要等 GC
    /// 终结器回收才恢复，与 M83 修过的是同一类坑）。探测语义下门面本就该释放工厂造出的通道，
    /// 故改用私有构造并显式 <c>ownsChannel: true</c>。
    /// </remarks>
    private static async Task<Sk150PowerSupply> OpenOverChannelAsync(
        Sk150Options options,
        Func<Sk150Options, IRegisterChannel> channelFactory,
        CancellationToken cancellationToken)
    {
        IRegisterChannel channel = channelFactory(options);
        try
        {
            await channel.OpenAsync(cancellationToken).ConfigureAwait(false);
            var psu = new Sk150PowerSupply(channel, options, ownsChannel: true);
            await psu.ReadStatusAsync(cancellationToken).ConfigureAwait(false);
            return psu;
        }
        catch
        {
            await channel.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Sweeps ports, baud rates and slave addresses and returns the first device found.</summary>
    /// <param name="ports">Ports to try; defaults to every port on the machine.</param>
    /// <param name="baudRates">Rates to try; defaults to <see cref="DefaultBaudRates"/>.</param>
    /// <param name="slaveAddresses">Addresses to try; defaults to <see cref="DefaultSlaveAddresses"/>.</param>
    /// <param name="channelFactory">Builds the transport to probe through; defaults to a real serial channel.</param>
    /// <param name="portNameProvider">Where port names come from when <paramref name="ports"/> is null.</param>
    /// <param name="cancellationToken">Cancels the sweep.</param>
    public static async Task<Sk150ProbeResult?> FindAsync(
        IEnumerable<string>? ports = null,
        IEnumerable<int>? baudRates = null,
        IEnumerable<byte>? slaveAddresses = null,
        Func<Sk150Options, IRegisterChannel>? channelFactory = null,
        Func<IEnumerable<string>>? portNameProvider = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> candidates = ports?.ToArray() ?? GetPortNames(portNameProvider);
        IReadOnlyList<int> rates = baudRates?.ToArray() ?? DefaultBaudRates;
        IReadOnlyList<byte> addresses = slaveAddresses?.ToArray() ?? DefaultSlaveAddresses;

        foreach (string port in candidates)
        {
            foreach (int rate in rates)
            {
                foreach (byte address in addresses)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Sk150DeviceInfo? device = await ProbeAsync(
                        port, rate, address, null, channelFactory, cancellationToken).ConfigureAwait(false);
                    if (device is not null)
                    {
                        return new Sk150ProbeResult(port, rate, address, device);
                    }
                }
            }
        }

        return null;
    }
}
