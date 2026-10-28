namespace Sk150PowerSupply;

/// <summary>
/// Connection and behaviour options for an SK150 link. One instance configures both the serial
/// transport and the device facade.
/// </summary>
public sealed class Sk150Options
{
    /// <summary>Serial port name, for example "COM4" or "/dev/ttyUSB0". Required for a serial link.</summary>
    public string? PortName { get; set; }

    /// <summary>Link speed. The SK150 defaults to 115200 for the SK120/SK150 family.</summary>
    public int BaudRate { get; set; } = 115200;

    /// <summary>MODBUS slave address, 1-247. The factory default is 1.</summary>
    public byte SlaveAddress { get; set; } = 1;

    /// <summary>How long to wait for a response frame before giving up.</summary>
    public TimeSpan ResponseTimeout { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Silence inserted before each request (MODBUS requires 3.5 character times).</summary>
    public TimeSpan InterFrameDelay { get; set; } = TimeSpan.FromMilliseconds(2);

    /// <summary>Silence that ends a partially received frame.</summary>
    public TimeSpan InterByteTimeout { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>Delay between retries after a framing or timeout failure.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>Total attempts per request, including the first one. 1 disables retries.</summary>
    public int Retries { get; set; } = 3;

    /// <summary>
    /// Nameplate guard applied to voltage and current writes. Defaults to
    /// <see cref="Sk150SafetyLimits.Default"/>; set to <see cref="Sk150SafetyLimits.None"/> to
    /// disable, or tighten it for a smaller module.
    /// </summary>
    public Sk150SafetyLimits SafetyLimits { get; set; } = Sk150SafetyLimits.Default;

    internal void Validate()
    {
        if (BaudRate is < 1200 or > 921600)
        {
            throw new ArgumentOutOfRangeException(nameof(BaudRate), BaudRate, "Baud rate must be 1200-921600.");
        }

        if (SlaveAddress is < 1 or > 247)
        {
            throw new ArgumentOutOfRangeException(nameof(SlaveAddress), SlaveAddress, "Slave address must be 1-247.");
        }

        if (Retries < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Retries), Retries, "At least one attempt is required.");
        }

        if (ResponseTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ResponseTimeout), ResponseTimeout, "Timeout must be positive.");
        }
    }

    /// <summary>Returns a shallow copy of this instance.</summary>
    public Sk150Options Clone()
    {
        Sk150Options copy = (Sk150Options)MemberwiseClone();
        return copy;
    }
}

/// <summary>
/// Guard rails for voltage and current writes. Values above a limit are rejected instead of being
/// clamped, so a unit mix-up fails loudly rather than silently producing the wrong setpoint.
/// </summary>
/// <param name="MaxVoltage">Largest voltage the caller may set, in volts. Null disables the check.</param>
/// <param name="MaxCurrent">Largest current limit the caller may set, in amperes. Null disables the check.</param>
public sealed record Sk150SafetyLimits(decimal? MaxVoltage, decimal? MaxCurrent)
{
    /// <summary>
    /// Conservative nameplate guard for an SK150 (150 W module): 36 V and 5 A. Tighten or replace
    /// this for the SK60/SK120, or for a module whose output range differs.
    /// </summary>
    public static Sk150SafetyLimits Default { get; } = new(36m, 5m);

    /// <summary>No limits. Every value a 16-bit register can hold is accepted.</summary>
    public static Sk150SafetyLimits None { get; } = new(null, null);

    /// <summary>Throws when <paramref name="voltage"/> exceeds <see cref="MaxVoltage"/>.</summary>
    public void EnsureVoltageAllowed(decimal voltage)
    {
        if (voltage < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(voltage), voltage, "Voltage cannot be negative.");
        }

        if (MaxVoltage is { } max && voltage > max)
        {
            throw new ArgumentOutOfRangeException(
                nameof(voltage), voltage, $"Voltage {voltage} V exceeds the configured limit of {max} V.");
        }
    }

    /// <summary>Throws when <paramref name="current"/> exceeds <see cref="MaxCurrent"/>.</summary>
    public void EnsureCurrentAllowed(decimal current)
    {
        if (current < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(current), current, "Current cannot be negative.");
        }

        if (MaxCurrent is { } max && current > max)
        {
            throw new ArgumentOutOfRangeException(
                nameof(current), current, $"Current {current} A exceeds the configured limit of {max} A.");
        }
    }
}
