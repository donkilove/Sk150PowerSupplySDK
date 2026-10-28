using System.IO.Ports;

namespace Sk150PowerSupply;

/// <summary>
/// MODBUS RTU channel over a serial port (USB-to-TTL adapter or a module's built-in bridge).
/// </summary>
/// <remarks>
/// Requests are serialised by an internal gate, each request is preceded by the configured
/// inter-frame silence, and responses are read until the expected length arrives or the line goes
/// idle. Framing and timeout failures are retried <see cref="Sk150Options.Retries"/> times;
/// MODBUS exception responses are never retried.
/// </remarks>
public sealed class SerialRegisterChannel : IRegisterChannel
{
    private readonly Sk150Options _options;
    private readonly SerialPort _port;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    /// <summary>Creates a channel from a fully populated options object.</summary>
    /// <exception cref="ArgumentException">No port name was supplied.</exception>
    public SerialRegisterChannel(Sk150Options options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.PortName))
        {
            throw new ArgumentException("Sk150Options.PortName must be set to open a serial link.", nameof(options));
        }

        options.Validate();
        _options = options.Clone();
        _port = new SerialPort(_options.PortName!, _options.BaudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            DtrEnable = false,
            RtsEnable = false,
            ReadTimeout = (int)Math.Max(1, _options.ResponseTimeout.TotalMilliseconds),
            WriteTimeout = (int)Math.Max(1, _options.ResponseTimeout.TotalMilliseconds),
        };
    }

    /// <summary>Creates a channel for one port, using the family defaults (115200 8N1, slave 1).</summary>
    public SerialRegisterChannel(string portName, int baudRate = 115200, byte slaveAddress = 1)
        : this(new Sk150Options { PortName = portName, BaudRate = baudRate, SlaveAddress = slaveAddress })
    {
    }

    /// <summary>Serial port name the channel is bound to.</summary>
    public string PortName => _port.PortName;

    /// <summary>MODBUS slave address used for outgoing frames.</summary>
    public byte SlaveAddress => _options.SlaveAddress;

    /// <inheritdoc />
    public bool IsOpen => !_disposed && _port.IsOpen;

    /// <inheritdoc />
    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_port.IsOpen)
        {
            return Task.CompletedTask;
        }

        try
        {
            _port.Open();
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new Sk150LinkException(
                $"Cannot open {_port.PortName}: the port is in use by another process.", ex);
        }
        catch (IOException ex)
        {
            throw new Sk150LinkException($"Cannot open {_port.PortName}: {ex.Message}", ex);
        }
        catch (ArgumentException ex)
        {
            throw new Sk150LinkException($"{_port.PortName} is not a valid serial port name.", ex);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (!_disposed && _port.IsOpen)
        {
            _port.Close();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<ushort[]> ReadHoldingRegistersAsync(
        ushort startAddress,
        ushort count,
        CancellationToken cancellationToken = default)
    {
        byte[] request = ModbusRtu.BuildReadHoldingRegisters(_options.SlaveAddress, startAddress, count);
        int expectedLength = 5 + (count * 2);

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                byte[] frame = await ExchangeAsync(request, expectedLength, cancellationToken).ConfigureAwait(false);
                return ModbusRtu.ParseReadHoldingRegistersResponse(frame, _options.SlaveAddress, count);
            }
            catch (ModbusException)
            {
                throw;
            }
            catch (Sk150LinkException) when (attempt < _options.Retries)
            {
                await Task.Delay(_options.RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async Task WriteSingleRegisterAsync(
        ushort address,
        ushort value,
        CancellationToken cancellationToken = default)
    {
        byte[] request = ModbusRtu.BuildWriteSingleRegister(_options.SlaveAddress, address, value);

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                byte[] frame = await ExchangeAsync(request, 8, cancellationToken).ConfigureAwait(false);
                ModbusRtu.ParseWriteResponse(frame, _options.SlaveAddress, ModbusRtu.WriteSingleRegister, address, value);
                return;
            }
            catch (ModbusException)
            {
                throw;
            }
            catch (Sk150LinkException) when (attempt < _options.Retries)
            {
                await Task.Delay(_options.RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async Task WriteMultipleRegistersAsync(
        ushort startAddress,
        IReadOnlyList<ushort> values,
        CancellationToken cancellationToken = default)
    {
        byte[] request = ModbusRtu.BuildWriteMultipleRegisters(_options.SlaveAddress, startAddress, values);

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                byte[] frame = await ExchangeAsync(request, 8, cancellationToken).ConfigureAwait(false);
                ModbusRtu.ParseWriteResponse(
                    frame, _options.SlaveAddress, ModbusRtu.WriteMultipleRegisters, startAddress, (ushort)values.Count);
                return;
            }
            catch (ModbusException)
            {
                throw;
            }
            catch (Sk150LinkException) when (attempt < _options.Retries)
            {
                await Task.Delay(_options.RetryDelay, cancellationToken).ConfigureAwait(false);
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
        try
        {
            if (_port.IsOpen)
            {
                _port.Close();
            }
        }
        catch (IOException)
        {
            // Closing a yanked USB adapter can fail; nothing useful to do here.
        }

        _port.Dispose();
        _gate.Dispose();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<byte[]> ExchangeAsync(byte[] request, int expectedLength, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_port.IsOpen)
            {
                throw new Sk150LinkException("The serial port is not open.");
            }

            if (_options.InterFrameDelay > TimeSpan.Zero)
            {
                await Task.Delay(_options.InterFrameDelay, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                _port.DiscardInBuffer();
                await _port.BaseStream.WriteAsync(request, cancellationToken).ConfigureAwait(false);
                await _port.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                throw new Sk150LinkException($"Write to {_port.PortName} failed: {ex.Message}", ex);
            }

            return await ReadFrameAsync(expectedLength, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <remarks>
    /// Deliberately synchronous and poll-driven: <see cref="Stream.ReadAsync(byte[], int, int, CancellationToken)"/>
    /// on <see cref="SerialPort.BaseStream"/> is unusable on Windows, because cancelling the pending
    /// read surfaces as <c>IOException</c> "The I/O operation has been aborted ..." (winerror 995)
    /// instead of <see cref="OperationCanceledException"/> — which both breaks cancellation and
    /// leaves the input buffer in an unknown state, so the next parse sees a corrupt frame. Sync
    /// reads honour <see cref="SerialPort.ReadTimeout"/> and always leave the port consistent.
    /// </remarks>
    private async Task<byte[]> ReadFrameAsync(int expectedLength, CancellationToken cancellationToken)
    {
        var buffer = new byte[ModbusRtu.MaxFrameLength];
        int total = 0;
        DateTime deadline = DateTime.UtcNow + _options.ResponseTimeout;
        int limit = Math.Min(expectedLength, buffer.Length);

        while (total < limit)
        {
            if (DateTime.UtcNow >= deadline)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();

            int available;
            try
            {
                available = _port.BytesToRead;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
            {
                throw new Sk150LinkException($"Read from {_port.PortName} failed: {ex.Message}", ex);
            }

            if (available <= 0)
            {
                // Idle line: a frame that already has a header is complete here (exception or short reply).
                if (total >= ModbusRtu.MinimumResponseLength)
                {
                    break;
                }

                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                continue;
            }

            int want = Math.Min(available, limit - total);
            int read;
            try
            {
                read = _port.Read(buffer, total, want);
            }
            catch (TimeoutException)
            {
                continue;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
            {
                throw new Sk150LinkException($"Read from {_port.PortName} failed: {ex.Message}", ex);
            }

            total += read;
        }

        if (total == 0)
        {
            throw new Sk150TimeoutException(
                $"No response from slave {_options.SlaveAddress} on {_port.PortName} " +
                $"within {_options.ResponseTimeout.TotalMilliseconds:0} ms.");
        }

        return buffer[..total];
    }
}
