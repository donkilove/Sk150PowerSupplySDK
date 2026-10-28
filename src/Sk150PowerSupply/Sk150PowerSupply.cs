using System.Runtime.CompilerServices;

namespace Sk150PowerSupply;

/// <summary>
/// High-level SK150 facade: decoded status, setpoints, protection thresholds, output control and
/// preset handling on top of an <see cref="IRegisterChannel"/>.
/// </summary>
/// <remarks>
/// <para>
/// Instances created with <see cref="OpenAsync(Sk150Options, CancellationToken)"/> own their serial
/// channel and close it on dispose. Instances created with the public constructor do not own the
/// channel — the caller stays responsible for it.
/// </para>
/// <para>
/// Every voltage and current write is checked against <see cref="Sk150Options.SafetyLimits"/>; values
/// above the configured nameplate are rejected rather than clamped, so a unit mix-up fails loudly.
/// </para>
/// </remarks>
public sealed class Sk150PowerSupply : IDisposable, IAsyncDisposable
{
    private readonly IRegisterChannel _channel;
    private readonly Sk150Options _options;
    private readonly bool _ownsChannel;
    private bool _disposed;

    /// <summary>Wraps an existing channel. The channel is not disposed with this instance.</summary>
    /// <param name="channel">Transport to talk through.</param>
    /// <param name="options">Options used for safety limits and timeouts. Defaults are used when null.</param>
    public Sk150PowerSupply(IRegisterChannel channel, Sk150Options? options = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _options = options?.Clone() ?? new Sk150Options();
        _ownsChannel = false;
    }

    private Sk150PowerSupply(IRegisterChannel channel, Sk150Options options, bool ownsChannel)
    {
        _channel = channel;
        _options = options;
        _ownsChannel = ownsChannel;
    }

    /// <summary>Options in effect for this instance (a private copy).</summary>
    public Sk150Options Options => _options;

    /// <summary>The transport this instance talks through.</summary>
    public IRegisterChannel Channel => _channel;

    /// <summary>True when the underlying link reports itself open.</summary>
    public bool IsOpen => !_disposed && _channel.IsOpen;

    /// <summary>Opens a serial link, probes the device, and returns a facade that owns the port.</summary>
    /// <param name="options">Must carry a <see cref="Sk150Options.PortName"/>.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <exception cref="Sk150LinkException">The port could not be opened or the device did not answer.</exception>
    public static async Task<Sk150PowerSupply> OpenAsync(
        Sk150Options options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var channel = new SerialRegisterChannel(options);
        try
        {
            await channel.OpenAsync(cancellationToken).ConfigureAwait(false);
            var psu = new Sk150PowerSupply(channel, options.Clone(), ownsChannel: true);

            // A single status read confirms something answers at this address before handing the
            // facade to the caller; a wrong port or slave fails here instead of at first use.
            await psu.ReadStatusAsync(cancellationToken).ConfigureAwait(false);
            return psu;
        }
        catch
        {
            await channel.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Opens a serial link with explicit link parameters and probes the device.</summary>
    /// <param name="portName">Serial port, for example "COM4".</param>
    /// <param name="baudRate">Link speed; the SK family defaults to 115200.</param>
    /// <param name="slaveAddress">MODBUS slave address; the factory default is 1.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    public static Task<Sk150PowerSupply> OpenAsync(
        string portName,
        int baudRate = 115200,
        byte slaveAddress = 1,
        CancellationToken cancellationToken = default)
        => OpenAsync(
            new Sk150Options { PortName = portName, BaudRate = baudRate, SlaveAddress = slaveAddress },
            cancellationToken);

    /// <summary>Creates a facade backed by an in-memory device, for tests, samples and the CLI mock mode.</summary>
    public static Sk150PowerSupply CreateMock()
        => new(MockRegisterChannel.CreateSk150(), new Sk150Options(), ownsChannel: true);

    /// <summary>Opens the underlying link if it is closed.</summary>
    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _channel.OpenAsync(cancellationToken);
    }

    /// <summary>Closes the underlying link, keeping the instance usable for a later open.</summary>
    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _channel.CloseAsync(cancellationToken);
    }

    /// <summary>Reads the whole status block (0x0000-0x001D) and decodes it.</summary>
    public async Task<Sk150Status> ReadStatusAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ushort[] block = await _channel
            .ReadHoldingRegistersAsync(
                Sk150Registers.StatusBlockStart, Sk150Registers.StatusBlockCount, cancellationToken)
            .ConfigureAwait(false);
        return Sk150Status.FromRegisters(block);
    }

    /// <summary>Reads the device identity: model, firmware, slave address and baud rate.</summary>
    public async Task<Sk150DeviceInfo> ReadDeviceInfoAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ushort[] probe = await _channel
            .ReadHoldingRegistersAsync(Sk150Registers.TemperatureEx, 1, cancellationToken)
            .ConfigureAwait(false);
        ushort[] identity = await _channel
            .ReadHoldingRegistersAsync(Sk150Registers.ModelId, 4, cancellationToken)
            .ConfigureAwait(false);

        int baudIndex = identity[Sk150Registers.BaudRateIndex - Sk150Registers.ModelId];
        return new Sk150DeviceInfo(
            identity[0],
            Sk150DeviceInfo.DescribeModel(identity[0]),
            identity[1],
            (byte)identity[2],
            baudIndex,
            Sk150Registers.BaudRates.TryGetValue(baudIndex, out int baud) ? baud : null,
            probe[0] != Sk150Registers.ExternalTemperatureNotConnected);
    }

    /// <summary>Sets the output voltage setpoint (register 0x0000, 10 mV resolution).</summary>
    /// <param name="volts">Target voltage in volts.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or above the safety limit.</exception>
    public Task SetVoltageAsync(decimal volts, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        _options.SafetyLimits.EnsureVoltageAllowed(volts);
        ushort raw = Sk150Registers.ToRaw(volts, Sk150Registers.VoltageScale);
        return _channel.WriteSingleRegisterAsync(Sk150Registers.VoltageSet, raw, cancellationToken);
    }

    /// <summary>Sets the current limit (register 0x0001, 1 mA resolution).</summary>
    /// <param name="amps">Target current limit in amperes.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or above the safety limit.</exception>
    public Task SetCurrentAsync(decimal amps, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        _options.SafetyLimits.EnsureCurrentAllowed(amps);
        ushort raw = Sk150Registers.ToRaw(amps, Sk150Registers.CurrentScale);
        return _channel.WriteSingleRegisterAsync(Sk150Registers.CurrentSet, raw, cancellationToken);
    }

    /// <summary>
    /// Writes the voltage setpoint and current limit in one MODBUS transaction (FC10 at 0x0000), so
    /// the device never sees one new setpoint paired with the other's stale value.
    /// </summary>
    public Task SetVoltageAndCurrentAsync(
        decimal volts,
        decimal amps,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        _options.SafetyLimits.EnsureVoltageAllowed(volts);
        _options.SafetyLimits.EnsureCurrentAllowed(amps);
        ushort[] values =
        [
            Sk150Registers.ToRaw(volts, Sk150Registers.VoltageScale),
            Sk150Registers.ToRaw(amps, Sk150Registers.CurrentScale),
        ];
        return _channel.WriteMultipleRegistersAsync(Sk150Registers.VoltageSet, values, cancellationToken);
    }

    /// <summary>Enables or disables the output stage (register 0x0012).</summary>
    public Task SetOutputAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _channel.WriteSingleRegisterAsync(
            Sk150Registers.OutputEnable, enabled ? (ushort)1 : (ushort)0, cancellationToken);
    }

    /// <summary>Enables the output stage.</summary>
    public Task TurnOnAsync(CancellationToken cancellationToken = default)
        => SetOutputAsync(true, cancellationToken);

    /// <summary>Disables the output stage.</summary>
    public Task TurnOffAsync(CancellationToken cancellationToken = default)
        => SetOutputAsync(false, cancellationToken);

    /// <summary>Locks or unlocks the front-panel keys (register 0x000F).</summary>
    public Task SetKeyLockAsync(bool locked, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _channel.WriteSingleRegisterAsync(
            Sk150Registers.KeyLock, locked ? (ushort)1 : (ushort)0, cancellationToken);
    }

    /// <summary>Sets the screen backlight brightness (register 0x0014, 0-5).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The level is outside 0-5.</exception>
    public Task SetBacklightAsync(int level, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (level is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Backlight brightness is 0-5.");
        }

        return _channel.WriteSingleRegisterAsync(Sk150Registers.Backlight, (ushort)level, cancellationToken);
    }

    /// <summary>Sets the screen sleep time in minutes (register 0x0015, 0-9; 0 keeps it on).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside 0-9.</exception>
    public Task SetSleepMinutesAsync(int minutes, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (minutes is < 0 or > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes), minutes, "Screen sleep time is 0-9 minutes.");
        }

        return _channel.WriteSingleRegisterAsync(Sk150Registers.SleepMinutes, (ushort)minutes, cancellationToken);
    }

    /// <summary>Enables or disables the buzzer (register 0x001C).</summary>
    public Task SetBuzzerAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _channel.WriteSingleRegisterAsync(
            Sk150Registers.Buzzer, enabled ? (ushort)1 : (ushort)0, cancellationToken);
    }

    /// <summary>Sets the temperature unit (register 0x0013).</summary>
    public Task SetTemperatureUnitAsync(Sk150TemperatureUnit unit, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _channel.WriteSingleRegisterAsync(
            Sk150Registers.TemperatureUnit, (ushort)unit, cancellationToken);
    }

    /// <summary>
    /// Changes the stored baud rate (register 0x0019). The device switches speed immediately, so the
    /// current link stops working: reopen the channel at the new rate afterwards.
    /// </summary>
    /// <exception cref="ArgumentException">The rate is not one of the documented indexes.</exception>
    public Task SetBaudRateAsync(int baudRate, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        int index = Sk150Registers.BaudRates
            .Where(pair => pair.Value == baudRate)
            .Select(pair => pair.Key)
            .DefaultIfEmpty(-1)
            .First();
        if (index < 0)
        {
            throw new ArgumentException(
                $"Baud rate {baudRate} is not one of: {string.Join(", ", Sk150Registers.BaudRates.Values)}.",
                nameof(baudRate));
        }

        return _channel.WriteSingleRegisterAsync(Sk150Registers.BaudRateIndex, (ushort)index, cancellationToken);
    }

    /// <summary>
    /// Changes the MODBUS slave address (register 0x0018). The device answers on the new address
    /// immediately; update <see cref="Sk150Options.SlaveAddress"/> and reopen the channel afterwards.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The address is outside 1-247.</exception>
    public Task SetSlaveAddressAsync(byte slaveAddress, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (slaveAddress is < 1 or > 247)
        {
            throw new ArgumentOutOfRangeException(nameof(slaveAddress), slaveAddress, "Slave address is 1-247.");
        }

        return _channel.WriteSingleRegisterAsync(Sk150Registers.SlaveAddress, slaveAddress, cancellationToken);
    }

    /// <summary>Loads a stored setup group M0-M9 onto the live setpoints (register 0x001D).</summary>
    /// <param name="index">Group index, 0-9.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside 0-9.</exception>
    public Task LoadPresetAsync(int index, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (index is < 0 or > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Preset groups are M0-M9.");
        }

        return _channel.WriteSingleRegisterAsync(Sk150Registers.LoadPreset, (ushort)index, cancellationToken);
    }

    /// <summary>Reads one stored setup group.</summary>
    /// <param name="index">Group index, 0-9.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside 0-9.</exception>
    public async Task<Sk150Preset> ReadPresetAsync(int index, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ushort start = Sk150Registers.PresetAddress(index);
        ushort[] registers = await _channel
            .ReadHoldingRegistersAsync(start, Sk150Registers.PresetRegisterCount, cancellationToken)
            .ConfigureAwait(false);
        return Sk150Preset.FromRegisters(index, registers);
    }

    /// <summary>
    /// Writes one stored setup group. The preset's <see cref="Sk150Preset.Index"/> must match
    /// <paramref name="index"/>. Thresholds are stored as given; the safety limits only guard the
    /// live setpoint writes.
    /// </summary>
    /// <param name="index">Group index, 0-9.</param>
    /// <param name="preset">Group contents to store.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="ArgumentException">The preset index does not match <paramref name="index"/>.</exception>
    public Task WritePresetAsync(int index, Sk150Preset preset, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(preset);
        if (preset.Index != index)
        {
            throw new ArgumentException(
                $"Preset index {preset.Index} does not match the target group {index}.", nameof(preset));
        }

        ushort start = Sk150Registers.PresetAddress(index);
        return _channel.WriteMultipleRegistersAsync(start, preset.ToRegisters(), cancellationToken);
    }

    /// <summary>Reads raw holding registers, for registers the facade does not model.</summary>
    public Task<ushort[]> ReadRawAsync(ushort startAddress, ushort count, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _channel.ReadHoldingRegistersAsync(startAddress, count, cancellationToken);
    }

    /// <summary>Writes one raw holding register, for registers the facade does not model.</summary>
    public Task WriteRawAsync(ushort address, ushort value, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _channel.WriteSingleRegisterAsync(address, value, cancellationToken);
    }

    /// <summary>
    /// Streams status snapshots at <paramref name="interval"/> until cancelled. Enumeration stops on
    /// cancellation; link errors are thrown to the consumer.
    /// </summary>
    /// <param name="interval">Delay between snapshots. Use zero for back-to-back reads.</param>
    /// <param name="cancellationToken">Stops the stream.</param>
    public async IAsyncEnumerable<Sk150Status> MonitorAsync(
        TimeSpan interval,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return await ReadStatusAsync(cancellationToken).ConfigureAwait(false);

            if (interval > TimeSpan.Zero)
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsChannel)
        {
            _channel.Dispose();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_ownsChannel)
        {
            return;
        }

        try
        {
            await _channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Sk150LinkException)
        {
            // A port that disappeared cannot be closed gracefully; disposal still has to finish.
        }

        await _channel.DisposeAsync().ConfigureAwait(false);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
