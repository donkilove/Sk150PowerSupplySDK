namespace Sk150PowerSupply;

/// <summary>
/// Transport abstraction for MODBUS RTU register access. The SK150 facade depends only on this
/// interface, so the same code runs against real hardware
/// (<see cref="SerialRegisterChannel"/>) and against an in-memory device
/// (<see cref="MockRegisterChannel"/>).
/// </summary>
/// <remarks>The channel owns MODBUS framing: register addresses are plain 16-bit values.</remarks>
public interface IRegisterChannel : IDisposable, IAsyncDisposable
{
    /// <summary>True when the underlying link is open and usable.</summary>
    bool IsOpen { get; }

    /// <summary>Opens the link. Idempotent.</summary>
    Task OpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes the link. Idempotent.</summary>
    Task CloseAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads <paramref name="count"/> holding registers starting at <paramref name="startAddress"/>.</summary>
    /// <exception cref="Sk150TimeoutException">The device did not answer in time.</exception>
    /// <exception cref="Sk150LinkException">The response was malformed or the port failed.</exception>
    /// <exception cref="ModbusException">The device returned a MODBUS exception.</exception>
    Task<ushort[]> ReadHoldingRegistersAsync(
        ushort startAddress,
        ushort count,
        CancellationToken cancellationToken = default);

    /// <summary>Writes one holding register.</summary>
    Task WriteSingleRegisterAsync(
        ushort address,
        ushort value,
        CancellationToken cancellationToken = default);

    /// <summary>Writes consecutive holding registers in one transaction.</summary>
    Task WriteMultipleRegistersAsync(
        ushort startAddress,
        IReadOnlyList<ushort> values,
        CancellationToken cancellationToken = default);
}
