namespace Sk150PowerSupply;

/// <summary>Base exception for failures on the SK150 link (framing, timeout, port I/O).</summary>
public class Sk150LinkException : Exception
{
    /// <summary>Creates a link exception with a message.</summary>
    public Sk150LinkException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a link exception with a message and an inner cause.</summary>
    public Sk150LinkException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The device did not answer within the configured response timeout.</summary>
public sealed class Sk150TimeoutException : Sk150LinkException
{
    /// <summary>Creates a timeout exception with a message.</summary>
    public Sk150TimeoutException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// A MODBUS exception response: the device understood the request and refused it
/// (illegal address, illegal value, busy, and so on).
/// </summary>
public sealed class ModbusException : Exception
{
    /// <summary>Creates a MODBUS exception from the decoded response.</summary>
    /// <param name="slaveAddress">Slave address that answered.</param>
    /// <param name="functionCode">Function code the device rejected (exception bit already stripped).</param>
    /// <param name="exceptionCode">MODBUS exception code from the response body.</param>
    public ModbusException(byte slaveAddress, byte functionCode, byte exceptionCode)
        : base(BuildMessage(slaveAddress, functionCode, exceptionCode))
    {
        SlaveAddress = slaveAddress;
        FunctionCode = functionCode;
        ExceptionCode = exceptionCode;
    }

    /// <summary>Slave address that answered.</summary>
    public byte SlaveAddress { get; }

    /// <summary>Function code the device rejected, without the 0x80 exception bit.</summary>
    public byte FunctionCode { get; }

    /// <summary>Raw exception code.</summary>
    public byte ExceptionCode { get; }

    /// <summary>The exception code as an enum value.</summary>
    public ModbusExceptionCode Code => (ModbusExceptionCode)ExceptionCode;

    private static string BuildMessage(byte slaveAddress, byte functionCode, byte exceptionCode)
    {
        string name = Enum.IsDefined(typeof(ModbusExceptionCode), exceptionCode)
            ? ((ModbusExceptionCode)exceptionCode).ToString()
            : "Unknown";
        return $"MODBUS exception {exceptionCode} ({name}) for function 0x{functionCode:X2} on slave {slaveAddress}.";
    }
}

/// <summary>MODBUS exception codes the SK150 firmware can return.</summary>
public enum ModbusExceptionCode : byte
{
    /// <summary>The function code is not supported.</summary>
    IllegalFunction = 0x01,

    /// <summary>The register address is not mapped.</summary>
    IllegalDataAddress = 0x02,

    /// <summary>The value is outside the range the register accepts.</summary>
    IllegalDataValue = 0x03,

    /// <summary>The device failed while executing the request.</summary>
    SlaveDeviceFailure = 0x04,

    /// <summary>The request was accepted and is being processed.</summary>
    Acknowledge = 0x05,

    /// <summary>The device is busy; retry later.</summary>
    SlaveDeviceBusy = 0x06,

    /// <summary>The device rejected the write (negative acknowledgement).</summary>
    NegativeAcknowledge = 0x07,

    /// <summary>The device timed out waiting for a dependent operation.</summary>
    GatewayTargetFailedToRespond = 0x0B,
}
