namespace Sk150PowerSupply;

/// <summary>One register write recorded by <see cref="MockRegisterChannel"/>.</summary>
/// <param name="Address">Register address written.</param>
/// <param name="Value">Raw value written.</param>
/// <param name="IsBlock">True when the write arrived as a multi-register (FC10) transaction.</param>
public readonly record struct RegisterWrite(ushort Address, ushort Value, bool IsBlock);

/// <summary>
/// In-memory stand-in for the device: a 64K register file, a write log, and injectable failures.
/// Used for offline development and unit tests, and usable from the CLI with <c>--mock</c>.
/// </summary>
public sealed class MockRegisterChannel : IRegisterChannel
{
    private readonly ushort[] _registers = new ushort[ushort.MaxValue + 1];
    private readonly List<RegisterWrite> _writes = new();
    private int _queuedTimeouts;

    /// <summary>Creates an empty register file that starts open.</summary>
    public MockRegisterChannel()
    {
    }

    /// <summary>Creates a register file preloaded with a plausible SK150 snapshot.</summary>
    public static MockRegisterChannel CreateSk150()
    {
        var mock = new MockRegisterChannel();
        mock[Sk150Registers.VoltageSet] = 330;
        mock[Sk150Registers.CurrentSet] = 100;
        mock[Sk150Registers.VoltageOut] = 330;
        mock[Sk150Registers.CurrentOut] = 4;
        mock[Sk150Registers.PowerOut] = 1;
        mock[Sk150Registers.VoltageIn] = 2461;
        mock[Sk150Registers.RuntimeMinutes] = 11;
        mock[Sk150Registers.RuntimeSeconds] = 49;
        mock[Sk150Registers.TemperatureIn] = 341;
        mock[Sk150Registers.TemperatureEx] = Sk150Registers.ExternalTemperatureNotConnected;
        mock[Sk150Registers.OutputEnable] = 1;
        mock[Sk150Registers.Backlight] = 5;
        mock[Sk150Registers.ModelId] = Sk150Registers.ModelIdSk150;
        mock[Sk150Registers.FirmwareVersion] = 142;
        mock[Sk150Registers.SlaveAddress] = 1;
        mock[Sk150Registers.BaudRateIndex] = 6;
        return mock;
    }

    /// <inheritdoc />
    public bool IsOpen { get; private set; } = true;

    /// <summary>Every register write in arrival order.</summary>
    public IReadOnlyList<RegisterWrite> Writes => _writes;

    /// <summary>Reads or writes one register directly, for test setup and assertions.</summary>
    public ushort this[ushort address]
    {
        get => _registers[address];
        set => _registers[address] = value;
    }

    /// <summary>Makes the next <paramref name="count"/> requests fail with a timeout.</summary>
    public void QueueTimeouts(int count) => _queuedTimeouts = count;

    /// <summary>Clears the write log.</summary>
    public void ClearWrites() => _writes.Clear();

    /// <summary>Returns a copy of a register window.</summary>
    public ushort[] Snapshot(ushort startAddress, ushort count)
    {
        var copy = new ushort[count];
        Array.Copy(_registers, startAddress, copy, 0, count);
        return copy;
    }

    /// <inheritdoc />
    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        IsOpen = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        IsOpen = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ushort[]> ReadHoldingRegistersAsync(
        ushort startAddress,
        ushort count,
        CancellationToken cancellationToken = default)
    {
        ThrowIfQueuedTimeout();
        cancellationToken.ThrowIfCancellationRequested();
        if (count == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Read at least one register.");
        }

        int end = startAddress + count;
        if (end > _registers.Length)
        {
            throw new ModbusException(1, ModbusRtu.ReadHoldingRegisters, (byte)ModbusExceptionCode.IllegalDataAddress);
        }

        return Task.FromResult(Snapshot(startAddress, count));
    }

    /// <inheritdoc />
    public Task WriteSingleRegisterAsync(
        ushort address,
        ushort value,
        CancellationToken cancellationToken = default)
    {
        ThrowIfQueuedTimeout();
        cancellationToken.ThrowIfCancellationRequested();
        _registers[address] = value;
        _writes.Add(new RegisterWrite(address, value, IsBlock: false));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task WriteMultipleRegistersAsync(
        ushort startAddress,
        IReadOnlyList<ushort> values,
        CancellationToken cancellationToken = default)
    {
        ThrowIfQueuedTimeout();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(values);

        for (int i = 0; i < values.Count; i++)
        {
            ushort address = (ushort)(startAddress + i);
            _registers[address] = values[i];
            _writes.Add(new RegisterWrite(address, values[i], IsBlock: true));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        IsOpen = false;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void ThrowIfQueuedTimeout()
    {
        if (_queuedTimeouts <= 0)
        {
            return;
        }

        _queuedTimeouts--;
        throw new Sk150TimeoutException("Injected timeout from MockRegisterChannel.");
    }
}
