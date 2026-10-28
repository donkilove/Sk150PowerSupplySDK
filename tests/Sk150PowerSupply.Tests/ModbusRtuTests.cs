using Sk150PowerSupply;

namespace Sk150PowerSupply.Tests;

/// <summary>Framing checks: CRC, request frames, and response validation.</summary>
public sealed class ModbusRtuTests
{
    [Fact]
    public void ComputeCrc_MatchesTheModbusSpecificationExample()
    {
        // The MODBUS over serial line specification uses "11 03 00 6B 00 03 76 87" as its worked
        // example: the CRC travels low byte first, so the register value is 0x8776.
        byte[] body = [0x11, 0x03, 0x00, 0x6B, 0x00, 0x03];

        Assert.Equal(0x8776, ModbusRtu.ComputeCrc(body));
    }

    [Fact]
    public void BuildReadHoldingRegisters_ProducesTheExpectedFrame()
    {
        byte[] frame = ModbusRtu.BuildReadHoldingRegisters(1, 0x0000, 30);

        Assert.Equal(new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x1E, 0xC5, 0xC2 }, frame);
    }

    [Fact]
    public void BuildWriteSingleRegister_ProducesTheExpectedFrame()
    {
        byte[] frame = ModbusRtu.BuildWriteSingleRegister(1, Sk150Registers.VoltageSet, 1250);

        Assert.Equal(new byte[] { 0x01, 0x06, 0x00, 0x00, 0x04, 0xE2, 0x0B, 0x43 }, frame);
    }

    [Fact]
    public void BuildWriteMultipleRegisters_EncodesCountByteCountAndValues()
    {
        byte[] frame = ModbusRtu.BuildWriteMultipleRegisters(1, 0x0000, [1250, 2000]);

        Assert.Equal(13, frame.Length);
        Assert.Equal(0x10, frame[1]);
        Assert.Equal(0x00, frame[2]);
        Assert.Equal(0x00, frame[3]);
        Assert.Equal(0x00, frame[4]);
        Assert.Equal(0x02, frame[5]);
        Assert.Equal(0x04, frame[6]);
        Assert.Equal(0x04, frame[7]);
        Assert.Equal(0xE2, frame[8]);
        Assert.Equal(0x07, frame[9]);
        Assert.Equal(0xD0, frame[10]);
        ModbusRtu.EnsureCrc(frame);
    }

    [Fact]
    public void ParseReadHoldingRegistersResponse_DecodesValues()
    {
        // 01 03 04 01 4A 00 64 + CRC = two registers: 330 (3.30 V) and 100 (0.100 A).
        byte[] response = [0x01, 0x03, 0x04, 0x01, 0x4A, 0x00, 0x64, 0x00, 0x00];
        ModbusRtu.AppendCrc(response);

        ushort[] values = ModbusRtu.ParseReadHoldingRegistersResponse(response, 1, 2);

        Assert.Equal(new ushort[] { 330, 100 }, values);
    }

    [Fact]
    public void ParseReadHoldingRegistersResponse_RejectsAWatchdogCorruptedCrc()
    {
        byte[] response = [0x01, 0x03, 0x04, 0x01, 0x4A, 0x00, 0x64, 0x00, 0x00];
        ModbusRtu.AppendCrc(response);
        response[^1] ^= 0xFF;

        Assert.Throws<Sk150LinkException>(
            () => ModbusRtu.ParseReadHoldingRegistersResponse(response, 1, 2));
    }

    [Fact]
    public void ParseReadHoldingRegistersResponse_RejectsAnUnexpectedRegisterCount()
    {
        byte[] response = [0x01, 0x03, 0x04, 0x01, 0x4A, 0x00, 0x64, 0x00, 0x00];
        ModbusRtu.AppendCrc(response);

        Assert.Throws<Sk150LinkException>(
            () => ModbusRtu.ParseReadHoldingRegistersResponse(response, 1, 3));
    }

    [Fact]
    public void ParseReadHoldingRegistersResponse_SurfacesExceptionResponses()
    {
        byte[] response = [0x01, 0x83, 0x02, 0x00, 0x00];
        ModbusRtu.AppendCrc(response);

        var exception = Assert.Throws<ModbusException>(
            () => ModbusRtu.ParseReadHoldingRegistersResponse(response, 1, 1));

        Assert.Equal(ModbusExceptionCode.IllegalDataAddress, exception.Code);
        Assert.Equal(ModbusRtu.ReadHoldingRegisters, exception.FunctionCode);
    }

    [Fact]
    public void ParseWriteResponse_RejectsAMismatchedEcho()
    {
        byte[] response = ModbusRtu.BuildWriteSingleRegister(1, Sk150Registers.VoltageSet, 1250);

        Assert.Throws<Sk150LinkException>(
            () => ModbusRtu.ParseWriteResponse(response, 1, ModbusRtu.WriteSingleRegister, 0x0000, 1200));
    }

    [Fact]
    public void BuildReadHoldingRegisters_RejectsCountsOutsideTheSpecification()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ModbusRtu.BuildReadHoldingRegisters(1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ModbusRtu.BuildReadHoldingRegisters(1, 0, 126));
    }
}
