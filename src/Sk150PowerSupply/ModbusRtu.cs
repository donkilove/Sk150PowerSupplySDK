using System.Buffers.Binary;

namespace Sk150PowerSupply;

/// <summary>
/// MODBUS RTU framing for the SK150 link: CRC-16 (polynomial 0xA001), request frames, and
/// response parsing for function codes 0x03, 0x06 and 0x10.
/// </summary>
/// <remarks>
/// Every multi-byte field is big-endian except the CRC, which is transmitted low byte first.
/// The framing here is transport-agnostic and has no knowledge of the SK150 register map;
/// see <see cref="Sk150Registers"/> for that.
/// </remarks>
public static class ModbusRtu
{
    /// <summary>Function code 0x03 — read holding registers.</summary>
    public const byte ReadHoldingRegisters = 0x03;

    /// <summary>Function code 0x06 — write a single register.</summary>
    public const byte WriteSingleRegister = 0x06;

    /// <summary>Function code 0x10 — write multiple registers.</summary>
    public const byte WriteMultipleRegisters = 0x10;

    /// <summary>Bit set in the function code of an exception response.</summary>
    public const byte ExceptionFlag = 0x80;

    /// <summary>Largest frame any SK150 exchange produces (30 registers + header + CRC).</summary>
    public const int MaxFrameLength = 260;

    /// <summary>Shortest legal response frame: address, function, and CRC (exception responses).</summary>
    public const int MinimumResponseLength = 5;

    /// <summary>
    /// Computes the MODBUS CRC-16 of <paramref name="data"/> (initial value 0xFFFF, reflected
    /// polynomial 0xA001). The returned value is the register value; on the wire it is sent
    /// low byte first.
    /// </summary>
    public static ushort ComputeCrc(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            }
        }

        return crc;
    }

    /// <summary>Builds a FC03 read-holding-registers request frame.</summary>
    /// <param name="slaveAddress">MODBUS slave address, 1-247.</param>
    /// <param name="startAddress">First register address.</param>
    /// <param name="count">Register count, 1-125.</param>
    public static byte[] BuildReadHoldingRegisters(byte slaveAddress, ushort startAddress, ushort count)
    {
        if (count is 0 or > 125)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "MODBUS FC03 allows 1-125 registers per request.");
        }

        var frame = new byte[8];
        frame[0] = slaveAddress;
        frame[1] = ReadHoldingRegisters;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2), startAddress);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), count);
        AppendCrc(frame);
        return frame;
    }

    /// <summary>Builds a FC06 write-single-register request frame.</summary>
    public static byte[] BuildWriteSingleRegister(byte slaveAddress, ushort address, ushort value)
    {
        var frame = new byte[8];
        frame[0] = slaveAddress;
        frame[1] = WriteSingleRegister;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2), address);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), value);
        AppendCrc(frame);
        return frame;
    }

    /// <summary>Builds a FC10 write-multiple-registers request frame.</summary>
    public static byte[] BuildWriteMultipleRegisters(byte slaveAddress, ushort startAddress, IReadOnlyList<ushort> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count is 0 or > 123)
        {
            throw new ArgumentOutOfRangeException(nameof(values), values.Count, "MODBUS FC10 allows 1-123 registers per request.");
        }

        var frame = new byte[9 + (values.Count * 2)];
        frame[0] = slaveAddress;
        frame[1] = WriteMultipleRegisters;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2), startAddress);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), (ushort)values.Count);
        frame[6] = (byte)(values.Count * 2);
        for (int i = 0; i < values.Count; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(7 + (i * 2)), values[i]);
        }

        AppendCrc(frame);
        return frame;
    }

    /// <summary>
    /// Parses a FC03 response into register values, validating the slave address, function code,
    /// byte count and CRC. Exception responses are surfaced as <see cref="ModbusException"/>.
    /// </summary>
    public static ushort[] ParseReadHoldingRegistersResponse(
        ReadOnlySpan<byte> frame,
        byte slaveAddress,
        ushort expectedCount)
    {
        EnsureHeader(frame, slaveAddress, ReadHoldingRegisters);
        ThrowIfExceptionFrame(frame);
        EnsureCrc(frame);

        int byteCount = frame[2];
        if (byteCount % 2 != 0)
        {
            throw new Sk150LinkException($"Response byte count {byteCount} is not a whole number of registers.");
        }

        if (byteCount != expectedCount * 2)
        {
            throw new Sk150LinkException(
                $"Response carried {byteCount / 2} registers but {expectedCount} were requested.");
        }

        if (frame.Length < 3 + byteCount + 2)
        {
            throw new Sk150LinkException(
                $"Response truncated: {frame.Length} bytes, expected {3 + byteCount + 2}.");
        }

        var values = new ushort[byteCount / 2];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(3 + (i * 2), 2));
        }

        return values;
    }

    /// <summary>
    /// Validates the acknowledgement of a write (FC06 echoes the register and value; FC10 echoes
    /// the start address and register count).
    /// </summary>
    public static void ParseWriteResponse(
        ReadOnlySpan<byte> frame,
        byte slaveAddress,
        byte functionCode,
        ushort address,
        ushort valueOrCount)
    {
        EnsureHeader(frame, slaveAddress, functionCode);
        ThrowIfExceptionFrame(frame);
        EnsureCrc(frame);

        if (frame.Length < 8)
        {
            throw new Sk150LinkException($"Write acknowledgement truncated: {frame.Length} bytes, expected 8.");
        }

        ushort echoedAddress = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(2, 2));
        ushort echoedValue = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(4, 2));
        if (echoedAddress != address || echoedValue != valueOrCount)
        {
            throw new Sk150LinkException(
                $"Write acknowledgement mismatch: device echoed 0x{echoedAddress:X4}=0x{echoedValue:X4}, " +
                $"sent 0x{address:X4}=0x{valueOrCount:X4}.");
        }
    }

    /// <summary>Throws <see cref="Sk150LinkException"/> when the frame CRC does not match.</summary>
    public static void EnsureCrc(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < MinimumResponseLength)
        {
            throw new Sk150LinkException($"Frame too short for a CRC check ({frame.Length} bytes).");
        }

        ushort expected = ComputeCrc(frame[..^2]);
        ushort actual = BinaryPrimitives.ReadUInt16LittleEndian(frame[^2..]);
        if (expected != actual)
        {
            throw new Sk150LinkException(
                $"CRC mismatch: computed 0x{expected:X4}, frame carried 0x{actual:X4}.");
        }
    }

    /// <summary>Returns true when the frame is a MODBUS exception response (function code | 0x80).</summary>
    public static bool IsExceptionFrame(ReadOnlySpan<byte> frame)
        => frame.Length >= 3 && (frame[1] & ExceptionFlag) != 0;

    /// <summary>Appends the two CRC bytes to <paramref name="frame"/>, overwriting the last two bytes.</summary>
    public static void AppendCrc(Span<byte> frame)
    {
        ushort crc = ComputeCrc(frame[..^2]);
        BinaryPrimitives.WriteUInt16LittleEndian(frame[^2..], crc);
    }

    private static void EnsureHeader(ReadOnlySpan<byte> frame, byte slaveAddress, byte functionCode)
    {
        if (frame.Length < MinimumResponseLength)
        {
            throw new Sk150LinkException(
                $"Response too short ({frame.Length} bytes); a MODBUS response needs at least {MinimumResponseLength}.");
        }

        if (frame[0] != slaveAddress)
        {
            throw new Sk150LinkException($"Response came from slave {frame[0]}, expected {slaveAddress}.");
        }

        if (!IsExceptionFrame(frame) && frame[1] != functionCode)
        {
            throw new Sk150LinkException(
                $"Unexpected function code 0x{frame[1]:X2}, expected 0x{functionCode:X2}.");
        }
    }

    private static void ThrowIfExceptionFrame(ReadOnlySpan<byte> frame)
    {
        if (IsExceptionFrame(frame))
        {
            byte function = (byte)(frame[1] & ~ExceptionFlag);
            throw new ModbusException(frame[0], function, frame[2]);
        }
    }
}
