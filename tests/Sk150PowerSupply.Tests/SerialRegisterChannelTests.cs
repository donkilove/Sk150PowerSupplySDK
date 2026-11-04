using System.Buffers.Binary;
using Sk150PowerSupply.Tests.Fakes;

namespace Sk150PowerSupply.Tests;

/// <summary>
/// 串口通道在字节层的行为：组帧、收帧、重试与超时。
/// 全部经 <see cref="FakeSerialPort"/> 驱动真实 MODBUS 帧，进程内不打开任何真实串口。
/// </summary>
public sealed class SerialRegisterChannelTests
{
    private static readonly byte Slave = 1;

    /// <summary>构造一个短超时、快重试的通道，避免测试因默认 500 ms 超时而变慢。</summary>
    private static SerialRegisterChannel Create(
        FakeSerialPort port,
        int retries = 3,
        int timeoutMs = 100)
    {
        var options = new Sk150Options
        {
            BaudRate = 115200,
            SlaveAddress = Slave,
            ResponseTimeout = TimeSpan.FromMilliseconds(timeoutMs),
            InterFrameDelay = TimeSpan.Zero,
            RetryDelay = TimeSpan.FromMilliseconds(1),
            Retries = retries,
        };

        return new SerialRegisterChannel(port, options);
    }

    /// <summary>FC03 应答帧：[slave][0x03][byteCount][data…][crc]。</summary>
    private static byte[] ReadResponse(params ushort[] values)
    {
        var frame = new byte[3 + (values.Length * 2) + 2];
        frame[0] = Slave;
        frame[1] = ModbusRtu.ReadHoldingRegisters;
        frame[2] = (byte)(values.Length * 2);
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(3 + (i * 2)), values[i]);
        }

        ModbusRtu.AppendCrc(frame);
        return frame;
    }

    /// <summary>FC06/FC10 应答帧：8 字节回显。</summary>
    private static byte[] WriteEcho(byte functionCode, ushort address, ushort valueOrCount)
    {
        var frame = new byte[8];
        frame[0] = Slave;
        frame[1] = functionCode;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2), address);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), valueOrCount);
        ModbusRtu.AppendCrc(frame);
        return frame;
    }

    /// <summary>MODBUS 异常应答：[slave][fc|0x80][code][crc]。</summary>
    private static byte[] ExceptionResponse(byte functionCode, byte exceptionCode)
    {
        var frame = new byte[5];
        frame[0] = Slave;
        frame[1] = (byte)(functionCode | ModbusRtu.ExceptionFlag);
        frame[2] = exceptionCode;
        ModbusRtu.AppendCrc(frame);
        return frame;
    }

    [Fact]
    public async Task ReadHoldingRegistersAsync_SendsFc03AndParsesTheResponse()
    {
        var port = new FakeSerialPort();
        port.Open();
        port.Enqueue(ResponsePlan.Reply(ReadResponse(0x0ABC, 0x1234)));
        SerialRegisterChannel channel = Create(port);

        ushort[] values = await channel.ReadHoldingRegistersAsync(0x0000, 2);

        Assert.Equal([0x0ABC, 0x1234], values);
        byte[] expectedRequest = ModbusRtu.BuildReadHoldingRegisters(Slave, 0x0000, 2);
        Assert.Equal(expectedRequest, Assert.Single(port.Writes));
    }

    [Fact]
    public async Task WriteSingleRegisterAsync_SendsFc06AndAcceptsTheEcho()
    {
        var port = new FakeSerialPort();
        port.Open();
        port.Enqueue(ResponsePlan.Reply(WriteEcho(ModbusRtu.WriteSingleRegister, 0x000E, 0x01F4)));
        SerialRegisterChannel channel = Create(port);

        await channel.WriteSingleRegisterAsync(0x000E, 0x01F4);

        byte[] expectedRequest = ModbusRtu.BuildWriteSingleRegister(Slave, 0x000E, 0x01F4);
        Assert.Equal(expectedRequest, Assert.Single(port.Writes));
    }

    [Fact]
    public async Task WriteMultipleRegistersAsync_SendsFc10InOneTransaction()
    {
        var port = new FakeSerialPort();
        port.Open();
        port.Enqueue(ResponsePlan.Reply(WriteEcho(ModbusRtu.WriteMultipleRegisters, 0x0000, 2)));
        SerialRegisterChannel channel = Create(port);

        await channel.WriteMultipleRegistersAsync(0x0000, [0x04D8, 0x0064]);

        byte[] expectedRequest = ModbusRtu.BuildWriteMultipleRegisters(Slave, 0x0000, [0x04D8, 0x0064]);
        Assert.Equal(expectedRequest, Assert.Single(port.Writes));
    }

    [Fact]
    public async Task ReadHoldingRegistersAsync_AssemblesAFragmentedResponse()
    {
        var port = new FakeSerialPort();
        port.Open();
        // 每次 Read 最多交付 2 字节：一帧 9 字节的应答必须跨多次读取拼齐。
        port.Enqueue(ResponsePlan.Reply(ReadResponse(0x0ABC, 0x1234), maxBytesPerRead: 2));
        SerialRegisterChannel channel = Create(port);

        ushort[] values = await channel.ReadHoldingRegistersAsync(0x0000, 2);

        Assert.Equal([0x0ABC, 0x1234], values);
        Assert.True(port.ReadCalls > 1, $"分片应答应触发多次读取，实际 {port.ReadCalls} 次");
    }

    [Fact]
    public async Task ReadHoldingRegistersAsync_ThrowsTimeoutWhenTheLineStaysSilent()
    {
        var port = new FakeSerialPort();
        port.Open();
        port.Enqueue(ResponsePlan.Silent());
        SerialRegisterChannel channel = Create(port, retries: 1, timeoutMs: 50);

        Sk150TimeoutException ex = await Assert.ThrowsAsync<Sk150TimeoutException>(
            () => channel.ReadHoldingRegistersAsync(0x0000, 2));

        Assert.Contains("COM_FAKE", ex.Message);
    }

    [Fact]
    public async Task ReadHoldingRegistersAsync_RetriesLinkFailuresUpToTheConfiguredCount()
    {
        var port = new FakeSerialPort();
        port.Open();
        // 前两次静默（链接失败），第三次成功应答 ⇒ 共 3 次请求。
        port.Enqueue(
            ResponsePlan.Silent(),
            ResponsePlan.Silent(),
            ResponsePlan.Reply(ReadResponse(0x0007)));
        SerialRegisterChannel channel = Create(port, retries: 3);

        ushort[] values = await channel.ReadHoldingRegistersAsync(0x0000, 1);

        Assert.Equal([0x0007], values);
        Assert.Equal(3, port.Writes.Count);
    }

    [Fact]
    public async Task ReadHoldingRegistersAsync_DoesNotRetryModbusExceptions()
    {
        var port = new FakeSerialPort();
        port.Open();
        port.Enqueue(ResponsePlan.Reply(
            ExceptionResponse(ModbusRtu.ReadHoldingRegisters, (byte)ModbusExceptionCode.IllegalDataAddress)));
        SerialRegisterChannel channel = Create(port, retries: 3);

        ModbusException ex = await Assert.ThrowsAsync<ModbusException>(
            () => channel.ReadHoldingRegistersAsync(0x0000, 1));

        Assert.Equal(ModbusExceptionCode.IllegalDataAddress, ex.Code);
        Assert.Single(port.Writes);   // 设备异常码绝不重试
    }

    [Fact]
    public void PortAndSlaveAddressAreExposedFromEffect()
    {
        var port = new FakeSerialPort { PortName = "COM42", BaudRate = 115200 };
        port.Open();

        SerialRegisterChannel channel = Create(port);

        Assert.Equal("COM42", channel.PortName);
        Assert.Equal(Slave, channel.SlaveAddress);
        Assert.True(channel.IsOpen);
    }

    [Fact]
    public void DisposeIsIdempotentAndClosesThePort()
    {
        var port = new FakeSerialPort();
        port.Open();
        SerialRegisterChannel channel = Create(port);

        channel.Dispose();
        channel.Dispose();

        Assert.False(port.IsOpen);
        Assert.False(channel.IsOpen);
    }

    [Fact]
    public void OptionsConstructor_StillRejectsAMissingPortName()
    {
        // 既有契约不变：不带 PortName 的 options 构造必须大声失败。
        Assert.Throws<ArgumentException>(() => new SerialRegisterChannel(new Sk150Options()));
    }
}
