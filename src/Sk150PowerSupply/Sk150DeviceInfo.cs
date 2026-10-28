namespace Sk150PowerSupply;

/// <summary>
/// Static identification and link configuration read from the device: model, firmware, slave
/// address and baud rate.
/// </summary>
/// <param name="ModelId">Raw product model id (register 0x0016).</param>
/// <param name="ModelName">Friendly model name when the id is known, otherwise the hex id.</param>
/// <param name="FirmwareVersion">Raw firmware version (register 0x0017).</param>
/// <param name="SlaveAddress">Configured MODBUS slave address (register 0x0018).</param>
/// <param name="BaudRateIndex">Configured baud rate index (register 0x0019).</param>
/// <param name="BaudRate">Baud rate decoded from the index, or null when the index is unknown.</param>
/// <param name="HasExternalProbe">True when an NTC probe is connected (register 0x000E is not the sentinel).</param>
public sealed record Sk150DeviceInfo(
    ushort ModelId,
    string ModelName,
    ushort FirmwareVersion,
    byte SlaveAddress,
    int BaudRateIndex,
    int? BaudRate,
    bool HasExternalProbe)
{
    /// <summary>Maps a model id to its marketing name; unknown ids fall back to the hex value.</summary>
    /// <param name="modelId">Raw model id from register 0x0016.</param>
    public static string DescribeModel(ushort modelId) => modelId switch
    {
        Sk150Registers.ModelIdSk150 => "SK150",
        _ => $"0x{modelId:X4}",
    };

    /// <summary>Derives the device description from a status block.</summary>
    /// <param name="status">Decoded status block.</param>
    public static Sk150DeviceInfo FromStatus(Sk150Status status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new Sk150DeviceInfo(
            status.ModelId,
            DescribeModel(status.ModelId),
            status.FirmwareVersion,
            status.SlaveAddress,
            status.BaudRateIndex,
            status.BaudRate,
            status.TemperatureEx is not null);
    }

    /// <summary>Short human-readable summary of the device identity.</summary>
    public override string ToString()
        => $"{ModelName} firmware {FirmwareVersion}, slave {SlaveAddress}, " +
           $"baud {(BaudRate?.ToString() ?? $"index {BaudRateIndex}")}.";
}
