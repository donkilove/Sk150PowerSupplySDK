using System.IO.Ports;

namespace Sk150PowerSupply;

/// <summary>
/// <see cref="ISerialPort"/> 的默认实现，包装 <see cref="SerialPort"/> 并固定 8N1、
/// 无握手、DTR/RTS 关闭——即 SK 系列模块的接线约定。
/// </summary>
/// <remarks>
/// 这是整个库里唯一直接实例化 <see cref="SerialPort"/> 的地方；
/// 收帧逻辑不在此处，而在 <see cref="SerialRegisterChannel"/>，因此可被替换和测试。
/// </remarks>
public sealed class SystemSerialPort : ISerialPort
{
    private readonly SerialPort _port;

    /// <summary>按给定的串口名与速率创建实例（8N1，无握手）。</summary>
    /// <param name="portName">串口名，例如 "COM4"。</param>
    /// <param name="baudRate">链路速率。</param>
    public SystemSerialPort(string portName, int baudRate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            DtrEnable = false,
            RtsEnable = false,
        };
    }

    /// <inheritdoc />
    public string PortName => _port.PortName;

    /// <inheritdoc />
    public int BaudRate => _port.BaudRate;

    /// <inheritdoc />
    public bool IsOpen => _port.IsOpen;

    /// <inheritdoc />
    public int BytesToRead => _port.BytesToRead;

    /// <inheritdoc />
    public int ReadTimeout
    {
        get => _port.ReadTimeout;
        set => _port.ReadTimeout = value;
    }

    /// <inheritdoc />
    public int WriteTimeout
    {
        get => _port.WriteTimeout;
        set => _port.WriteTimeout = value;
    }

    /// <inheritdoc />
    public void Open() => _port.Open();

    /// <inheritdoc />
    public void Close() => _port.Close();

    /// <inheritdoc />
    public int Read(byte[] buffer, int offset, int count) => _port.Read(buffer, offset, count);

    /// <inheritdoc />
    public void DiscardInBuffer() => _port.DiscardInBuffer();

    /// <inheritdoc />
    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        => _port.BaseStream.WriteAsync(data, cancellationToken).AsTask();

    /// <inheritdoc />
    public Task FlushAsync(CancellationToken cancellationToken = default)
        => _port.BaseStream.FlushAsync(cancellationToken);

    /// <inheritdoc />
    public void Dispose() => _port.Dispose();
}
