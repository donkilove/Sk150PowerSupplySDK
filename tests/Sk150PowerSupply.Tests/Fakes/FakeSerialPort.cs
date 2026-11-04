namespace Sk150PowerSupply.Tests.Fakes;

/// <summary>
/// 一次请求的预设结果：要么应答一段字节（可限制每次 <c>Read</c> 的字节数以模拟分片到达），
/// 要么完全静默（驱动超时路径）。
/// </summary>
internal sealed record ResponsePlan(byte[]? Frame, int MaxBytesPerRead)
{
    /// <summary>本次请求不应答，线路保持静默。</summary>
    public static ResponsePlan Silent() => new(null, int.MaxValue);

    /// <summary>本次请求应答给定字节；<paramref name="maxBytesPerRead"/> 限制单次读取量。</summary>
    public static ResponsePlan Reply(byte[] frame, int maxBytesPerRead = int.MaxValue)
        => new(frame, maxBytesPerRead);

    /// <summary>把应答字节放进接收缓冲。</summary>
    public void Materialize(Queue<byte> sink)
    {
        if (Frame is null)
        {
            return;
        }

        foreach (byte value in Frame)
        {
            sink.Enqueue(value);
        }
    }
}

/// <summary>
/// 可编程串口替身，用于在无硬件环境下驱动真实的 MODBUS 帧收发。
/// <para>
/// 响应计划在 <see cref="DiscardInBuffer"/> 时推进：真实实现在每次请求前都会清空输入缓冲，
/// 因此该调用即"请求边界"，可精确表达"第一次静默、第二次应答"这类重试场景。
/// 未预置计划的请求一律静默，测试意图不会因计划耗尽而变成假成功。
/// </para>
/// 全程不打开真实串口。
/// </summary>
internal sealed class FakeSerialPort : ISerialPort
{
    private readonly Queue<ResponsePlan> _plans = new();
    private readonly Queue<byte> _incoming = new();
    private readonly List<byte[]> _writes = new();

    private ResponsePlan _current = ResponsePlan.Silent();

    /// <summary>预置若干请求的应答计划，按顺序消费。</summary>
    public void Enqueue(params ResponsePlan[] plans)
    {
        foreach (ResponsePlan plan in plans)
        {
            _plans.Enqueue(plan);
        }
    }

    /// <summary>收到的请求字节，按到达顺序。</summary>
    public IReadOnlyList<byte[]> Writes => _writes;

    /// <summary>最近一次请求期间的 <c>Read</c> 调用次数（用于断言分片路径确实走过）。</summary>
    public int ReadCalls { get; private set; }

    /// <summary>已消费的计划数。</summary>
    public int PlansConsumed { get; private set; }

    public string PortName { get; init; } = "COM_FAKE";

    public int BaudRate { get; init; } = 115200;

    public bool IsOpen { get; private set; }

    public int ReadTimeout { get; set; } = 500;

    public int WriteTimeout { get; set; } = 500;

    public int BytesToRead => _incoming.Count;

    public void Open() => IsOpen = true;

    public void Close() => IsOpen = false;

    public int Read(byte[] buffer, int offset, int count)
    {
        ReadCalls++;
        if (_incoming.Count == 0)
        {
            // 真实 SerialPort 在超时设置下会抛 TimeoutException；上游会 continue 继续轮询。
            throw new TimeoutException();
        }

        int wanted = Math.Min(Math.Min(count, _current.MaxBytesPerRead), _incoming.Count);
        for (int i = 0; i < wanted; i++)
        {
            buffer[offset + i] = _incoming.Dequeue();
        }

        return wanted;
    }

    public void DiscardInBuffer()
    {
        _incoming.Clear();
        ReadCalls = 0;
        _current = _plans.Count > 0 ? _plans.Dequeue() : ResponsePlan.Silent();
        PlansConsumed++;
        _current.Materialize(_incoming);
    }

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        _writes.Add(data.ToArray());
        return Task.CompletedTask;
    }

    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Dispose() => IsOpen = false;
}
