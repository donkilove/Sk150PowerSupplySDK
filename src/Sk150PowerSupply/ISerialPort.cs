namespace Sk150PowerSupply;

/// <summary>
/// 串口抽象：<see cref="SerialRegisterChannel"/> 只依赖本接口，因此同一套组帧、收帧与重试逻辑
/// 既能跑在真实串口（<see cref="SystemSerialPort"/>）上，也能跑在测试替身或上位机自己的
/// 串口实现上。
/// </summary>
/// <remarks>
/// <para>
/// 成员刻意与 <c>System.IO.Ports.SerialPort</c> 的对应子集保持同构（8N1 由实现固定），
/// 这样默认实现只是薄委托，而既有收帧逻辑无需改写。
/// </para>
/// <para>
/// <see cref="ReadTimeout"/> 必须暴露：收帧循环依赖它在无数据时抛
/// <see cref="TimeoutException"/>（实现侧），再靠轮询与截止时间判定整帧超时。
/// </para>
/// </remarks>
public interface ISerialPort : IDisposable
{
    /// <summary>本通道绑定的串口名，例如 "COM4"。</summary>
    string PortName { get; }

    /// <summary>链路速率。</summary>
    int BaudRate { get; }

    /// <summary>串口是否已打开。</summary>
    bool IsOpen { get; }

    /// <summary>接收缓冲区中当前可读的字节数。</summary>
    int BytesToRead { get; }

    /// <summary>单次读取的阻塞上限（毫秒）；超时由实现抛 <see cref="TimeoutException"/>。</summary>
    int ReadTimeout { get; set; }

    /// <summary>单次写入的阻塞上限（毫秒）。</summary>
    int WriteTimeout { get; set; }

    /// <summary>打开串口；重复调用应幂等。</summary>
    void Open();

    /// <summary>关闭串口；重复调用应幂等。</summary>
    void Close();

    /// <summary>读取最多 <paramref name="count"/> 字节。</summary>
    /// <returns>实际读到的字节数。</returns>
    /// <exception cref="TimeoutException">在 <see cref="ReadTimeout"/> 内没有数据。</exception>
    int Read(byte[] buffer, int offset, int count);

    /// <summary>丢弃接收缓冲区中的待读数据。</summary>
    void DiscardInBuffer();

    /// <summary>异步写入原始字节。</summary>
    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>冲刷发送缓冲区。</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
