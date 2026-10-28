namespace Sk150PowerSupply;

/// <summary>
/// Protection status reported by register 0x0010 — why the output was cut off.
/// </summary>
public enum Sk150ProtectionCode : ushort
{
    /// <summary>Operating normally.</summary>
    None = 0,

    /// <summary>Over-voltage protection tripped.</summary>
    OverVoltage = 1,

    /// <summary>Over-current protection tripped.</summary>
    OverCurrent = 2,

    /// <summary>Over-power protection tripped.</summary>
    OverPower = 3,

    /// <summary>Low input voltage protection tripped.</summary>
    LowVoltage = 4,

    /// <summary>Maximum accumulated capacity (Ah) reached.</summary>
    CapacityLimit = 5,

    /// <summary>Maximum runtime reached.</summary>
    RuntimeLimit = 6,

    /// <summary>Over-temperature protection tripped (internal sensor).</summary>
    OverTemperature = 7,

    /// <summary>External probe temperature protection tripped.</summary>
    ExternalTemperature = 8,

    /// <summary>Maximum accumulated energy (Wh) reached.</summary>
    EnergyLimit = 9,

    /// <summary>Input reverse / in-circuit protection tripped.</summary>
    InputProtection = 10,

    /// <summary>A code the firmware reported that is not in the documented table.</summary>
    Unknown = 0xFFFF,
}

/// <summary>Voltage/current regulation mode reported by register 0x0011.</summary>
public enum Sk150ControlMode : ushort
{
    /// <summary>Constant voltage: the output sits at the voltage setpoint.</summary>
    ConstantVoltage = 0,

    /// <summary>Constant current: the output is current-limited at the current setpoint.</summary>
    ConstantCurrent = 1,
}

/// <summary>Temperature unit reported by register 0x0013, which also scales 0x000D/0x000E.</summary>
public enum Sk150TemperatureUnit : ushort
{
    /// <summary>Degrees Celsius.</summary>
    Celsius = 0,

    /// <summary>Degrees Fahrenheit.</summary>
    Fahrenheit = 1,
}
