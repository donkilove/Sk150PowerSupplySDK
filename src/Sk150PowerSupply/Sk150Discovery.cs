using System.IO.Ports;

namespace Sk150PowerSupply;

/// <summary>A device that answered during a scan.</summary>
/// <param name="PortName">Serial port it answered on.</param>
/// <param name="BaudRate">Link speed that worked.</param>
/// <param name="SlaveAddress">MODBUS slave address that answered.</param>
/// <param name="Device">Identity read from the device.</param>
public sealed record Sk150ProbeResult(
    string PortName,
    int BaudRate,
    byte SlaveAddress,
    Sk150DeviceInfo Device)
{
    /// <summary>Short human-readable summary of the hit.</summary>
    public override string ToString()
        => $"{PortName} @ {BaudRate} 8N1 slave {SlaveAddress}: {Device}";
}

/// <summary>
/// Finds an SK150 on the machine's serial ports. Scanning opens each candidate port and sends one
/// read; ports that are busy, missing, or silent are skipped.
/// </summary>
public static class Sk150Discovery
{
    /// <summary>Baud rates tried by default, in the order they are attempted.</summary>
    public static IReadOnlyList<int> DefaultBaudRates { get; } = [115200, 9600, 38400, 19200, 57600];

    /// <summary>Slave addresses tried by default.</summary>
    public static IReadOnlyList<byte> DefaultSlaveAddresses { get; } = [1, 2, 3];

    /// <summary>Returns the machine's serial port names, sorted.</summary>
    public static IReadOnlyList<string> GetPortNames()
        => SerialPort.GetPortNames().OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Checks one port/baud/slave combination and returns the identity, or null when silent.</summary>
    /// <param name="portName">Serial port to probe.</param>
    /// <param name="baudRate">Link speed to try.</param>
    /// <param name="slaveAddress">Slave address to try.</param>
    /// <param name="responseTimeout">How long to wait for the answer; 300 ms by default.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    public static async Task<Sk150DeviceInfo?> ProbeAsync(
        string portName,
        int baudRate = 115200,
        byte slaveAddress = 1,
        TimeSpan? responseTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var options = new Sk150Options
        {
            PortName = portName,
            BaudRate = baudRate,
            SlaveAddress = slaveAddress,
            ResponseTimeout = responseTimeout ?? TimeSpan.FromMilliseconds(300),
            Retries = 1,
        };

        try
        {
            await using Sk150PowerSupply psu = await Sk150PowerSupply
                .OpenAsync(options, cancellationToken)
                .ConfigureAwait(false);
            return await psu.ReadDeviceInfoAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is Sk150LinkException or ModbusException or IOException)
        {
            return null;
        }
    }

    /// <summary>Sweeps ports, baud rates and slave addresses and returns the first device found.</summary>
    /// <param name="ports">Ports to try; defaults to every port on the machine.</param>
    /// <param name="baudRates">Rates to try; defaults to <see cref="DefaultBaudRates"/>.</param>
    /// <param name="slaveAddresses">Addresses to try; defaults to <see cref="DefaultSlaveAddresses"/>.</param>
    /// <param name="cancellationToken">Cancels the sweep.</param>
    public static async Task<Sk150ProbeResult?> FindAsync(
        IEnumerable<string>? ports = null,
        IEnumerable<int>? baudRates = null,
        IEnumerable<byte>? slaveAddresses = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> candidates = ports?.ToArray() ?? GetPortNames();
        IReadOnlyList<int> rates = baudRates?.ToArray() ?? DefaultBaudRates;
        IReadOnlyList<byte> addresses = slaveAddresses?.ToArray() ?? DefaultSlaveAddresses;

        foreach (string port in candidates)
        {
            foreach (int rate in rates)
            {
                foreach (byte address in addresses)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Sk150DeviceInfo? device = await ProbeAsync(port, rate, address, null, cancellationToken)
                        .ConfigureAwait(false);
                    if (device is not null)
                    {
                        return new Sk150ProbeResult(port, rate, address, device);
                    }
                }
            }
        }

        return null;
    }
}
