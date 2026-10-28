using System.Globalization;
using System.Text.Json;
using Sk150PowerSupply;
using Psu = Sk150PowerSupply.Sk150PowerSupply;

namespace Sk150Cli;

/// <summary>
/// Command-line front end for the SK150 facade: status readout, setpoints, output control, presets
/// and raw register access.
/// </summary>
internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitUsage = 1;
    private const int ExitLink = 2;
    private const int ExitDevice = 3;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Any(IsHelp))
        {
            PrintHelp();
            return args.Length == 0 ? ExitUsage : ExitOk;
        }

        if (args is ["--version"] or ["-V"])
        {
            Console.WriteLine($"sk150 {typeof(Program).Assembly.GetName().Version}");
            return ExitOk;
        }

        CliOptions options;
        List<string> positional;
        try
        {
            (options, positional) = CliOptions.Parse(args);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return ExitUsage;
        }

        if (positional.Count == 0)
        {
            Console.Error.WriteLine("error: no command given.");
            PrintHelp();
            return ExitUsage;
        }

        string command = positional[0];
        try
        {
            return await RunAsync(command, [.. positional.Skip(1)], options).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ModbusException or Sk150LinkException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return ExitLink;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return ExitUsage;
        }
    }

    private static async Task<int> RunAsync(string command, IReadOnlyList<string> positional, CliOptions options)
    {
        if (command == "ports")
        {
            PrintPorts();
            return ExitOk;
        }

        if (command == "scan")
        {
            return await ScanAsync(options).ConfigureAwait(false);
        }

        await using Psu psu = await ConnectAsync(options).ConfigureAwait(false);

        switch (command)
        {
            case "status":
                PrintStatus(await psu.ReadStatusAsync().ConfigureAwait(false), options.Json);
                return ExitOk;

            case "info":
                PrintInfo(await psu.ReadDeviceInfoAsync().ConfigureAwait(false), options.Json);
                return ExitOk;

            case "monitor":
                return await MonitorAsync(psu, positional, options).ConfigureAwait(false);

            case "v":
                await psu.SetVoltageAsync(ParseDecimal(positional, 0, "volts")).ConfigureAwait(false);
                Console.WriteLine($"voltage setpoint -> {ParseDecimal(positional, 0, "volts"):0.00} V");
                return ExitOk;

            case "i":
                await psu.SetCurrentAsync(ParseDecimal(positional, 0, "amps")).ConfigureAwait(false);
                Console.WriteLine($"current limit -> {ParseDecimal(positional, 0, "amps"):0.000} A");
                return ExitOk;

            case "set":
                decimal volts = ParseDecimal(positional, 0, "volts");
                decimal amps = ParseDecimal(positional, 1, "amps");
                await psu.SetVoltageAndCurrentAsync(volts, amps).ConfigureAwait(false);
                Console.WriteLine($"setpoints -> {volts:0.00} V / {amps:0.000} A");
                return ExitOk;

            case "on":
                await psu.TurnOnAsync().ConfigureAwait(false);
                Console.WriteLine("output ON");
                return ExitOk;

            case "off":
                await psu.TurnOffAsync().ConfigureAwait(false);
                Console.WriteLine("output OFF");
                return ExitOk;

            case "preset":
            {
                int index = ParseInt(positional, 0, "preset index");
                await psu.LoadPresetAsync(index).ConfigureAwait(false);
                Console.WriteLine($"loaded preset M{index}");
                return ExitOk;
            }

            case "preset-read":
            {
                int index = ParseInt(positional, 0, "preset index");
                PrintPreset(await psu.ReadPresetAsync(index).ConfigureAwait(false), options.Json);
                return ExitOk;
            }

            case "preset-write":
                return await WritePresetAsync(psu, positional, options).ConfigureAwait(false);

            case "reg-read":
            {
                ushort address = ParseUShort(positional, 0, "address");
                ushort count = positional.Count > 1 ? ParseUShort(positional, 1, "count") : (ushort)1;
                ushort[] registers = await psu.ReadRawAsync(address, count).ConfigureAwait(false);
                for (int i = 0; i < registers.Length; i++)
                {
                    Console.WriteLine(
                        $"0x{address + i:X4} ({address + i,5}) = {registers[i],6}  0x{registers[i]:X4}");
                }

                return ExitOk;
            }

            case "reg-write":
            {
                ushort address = ParseUShort(positional, 0, "address");
                ushort value = ParseUShort(positional, 1, "value");
                await psu.WriteRawAsync(address, value).ConfigureAwait(false);
                Console.WriteLine($"wrote {value} (0x{value:X4}) to 0x{address:X4}");
                return ExitOk;
            }

            case "backlight":
                await psu.SetBacklightAsync(ParseInt(positional, 0, "level")).ConfigureAwait(false);
                Console.WriteLine($"backlight -> {ParseInt(positional, 0, "level")}");
                return ExitOk;

            case "buzzer":
            {
                bool on = ParseOnOff(positional, 0);
                await psu.SetBuzzerAsync(on).ConfigureAwait(false);
                Console.WriteLine($"buzzer {(on ? "ON" : "OFF")}");
                return ExitOk;
            }

            default:
                Console.Error.WriteLine($"error: unknown command '{command}'.");
                PrintHelp();
                return ExitUsage;
        }
    }

    private static async Task<int> ScanAsync(CliOptions options)
    {
        IReadOnlyList<string> ports = string.IsNullOrWhiteSpace(options.Ports)
            ? Sk150Discovery.GetPortNames()
            : options.Ports.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (ports.Count == 0)
        {
            Console.Error.WriteLine("no serial ports found on this machine.");
            return ExitLink;
        }

        Console.WriteLine($"scanning {ports.Count} port(s): {string.Join(", ", ports)}");
        Sk150ProbeResult? hit = await Sk150Discovery
            .FindAsync(ports, slaveAddresses: [options.Slave])
            .ConfigureAwait(false);

        if (hit is null)
        {
            Console.Error.WriteLine(
                "no SK150 answered. Check TX/RX crossing, a common GND, that the module is powered, " +
                "and that the baud rate matches (try --baud 9600).");
            return ExitLink;
        }

        Console.WriteLine($"found {hit}");
        Console.WriteLine($"use: --port {hit.PortName} --baud {hit.BaudRate} --slave {hit.SlaveAddress}");
        return ExitOk;
    }

    private static async Task<int> MonitorAsync(Psu psu, IReadOnlyList<string> positional, CliOptions options)
    {
        int intervalMs = positional.Count > 0 ? ParseInt(positional, 0, "interval ms") : options.IntervalMs;
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += handler;

        try
        {
            await foreach (Sk150Status status in psu
                .MonitorAsync(TimeSpan.FromMilliseconds(intervalMs), cts.Token)
                .ConfigureAwait(false))
            {
                if (options.Json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(status, JsonOptions));
                    continue;
                }

                Console.Write(
                    $"\r{status.VoltageOut,7:0.00} V {status.CurrentOut,7:0.000} A {status.PowerOut,7:0.00} W" +
                    $"  in {status.VoltageIn,6:0.00} V  {status.TemperatureIn,5:0.0} C" +
                    $"  {(status.OutputEnabled ? "ON " : "OFF")} {status.ControlMode,-15} {status.Protection,-19}");
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C ends the monitor loop.
        }
        finally
        {
            Console.CancelKeyPress -= handler;
            Console.WriteLine();
        }

        return ExitOk;
    }

    private static async Task<int> WritePresetAsync(Psu psu, IReadOnlyList<string> positional, CliOptions options)
    {
        int index = ParseInt(positional, 0, "preset index");
        if (options.PresetVoltage is null || options.PresetCurrent is null)
        {
            Console.Error.WriteLine("error: preset-write needs at least --v <volts> and --i <amps>.");
            return ExitUsage;
        }

        var preset = new Sk150Preset
        {
            Index = index,
            Voltage = options.PresetVoltage.Value,
            Current = options.PresetCurrent.Value,
            LowVoltageProtection = options.PresetLvp ?? 0m,
            OverVoltageProtection = options.PresetOvp ?? 0m,
            OverCurrentProtection = options.PresetOcp ?? 0m,
            OverPowerProtection = options.PresetOpp ?? 0m,
            OverTemperatureProtection = options.PresetOtp ?? 0m,
            MaxRuntimeHours = options.PresetMaxHours ?? 0,
            MaxRuntimeMinutes = options.PresetMaxMinutes ?? 0,
            MaxCapacityMah = options.PresetMaxMah ?? 0,
            MaxEnergy10Mwh = options.PresetMaxWh10 ?? 0,
            PowerOnOutputEnabled = options.PresetPowerOn,
        };

        await psu.WritePresetAsync(index, preset).ConfigureAwait(false);
        Console.WriteLine($"preset M{index} written: {preset}");
        return ExitOk;
    }

    private static async Task<Psu> ConnectAsync(CliOptions options)
    {
        if (options.Mock)
        {
            Console.Error.WriteLine("note: mock mode, no hardware is touched.");
            return Psu.CreateMock();
        }

        var psuOptions = new Sk150Options
        {
            PortName = options.Port,
            BaudRate = options.Baud,
            SlaveAddress = options.Slave,
            ResponseTimeout = TimeSpan.FromMilliseconds(options.TimeoutMs),
            SafetyLimits = options.NoLimits
                ? Sk150SafetyLimits.None
                : options.MaxVoltage is null && options.MaxCurrent is null
                    ? Sk150SafetyLimits.Default
                    : new Sk150SafetyLimits(options.MaxVoltage, options.MaxCurrent),
        };

        if (!string.IsNullOrWhiteSpace(options.Port))
        {
            return await Psu.OpenAsync(psuOptions).ConfigureAwait(false);
        }

        Console.Error.WriteLine("no --port given, scanning...");
        Sk150ProbeResult? hit = await Sk150Discovery
            .FindAsync(slaveAddresses: [options.Slave])
            .ConfigureAwait(false);
        if (hit is null)
        {
            throw new Sk150LinkException(
                "no SK150 answered on any port. Pass --port COM4 (see 'sk150 ports') or use --mock.");
        }

        Console.Error.WriteLine($"using {hit.PortName} @ {hit.BaudRate} slave {hit.SlaveAddress}");
        psuOptions.PortName = hit.PortName;
        psuOptions.BaudRate = hit.BaudRate;
        psuOptions.SlaveAddress = hit.SlaveAddress;
        return await Psu.OpenAsync(psuOptions).ConfigureAwait(false);
    }

    private static void PrintStatus(Sk150Status status, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(status, JsonOptions));
            return;
        }

        ArgumentNullException.ThrowIfNull(status);
        Console.WriteLine($"output    {(status.OutputEnabled ? "ON" : "OFF")}   mode {status.ControlMode}   " +
                          $"protection {status.Protection}");
        Console.WriteLine($"set       {status.VoltageSet,7:0.00} V {status.CurrentSet,8:0.000} A");
        Console.WriteLine($"actual    {status.VoltageOut,7:0.00} V {status.CurrentOut,8:0.000} A {status.PowerOut,8:0.00} W");
        Console.WriteLine($"input     {status.VoltageIn,7:0.00} V   temp {status.TemperatureIn,5:0.0} " +
                          $"{UnitSuffix(status.TemperatureUnit)}   " +
                          $"external {(status.TemperatureEx is { } t ? $"{t:0.0} {UnitSuffix(status.TemperatureUnit)}" : "not connected")}");
        Console.WriteLine($"runtime   {status.Runtime:hh\\:mm\\:ss}   capacity {status.CapacityAh:0.000} Ah   " +
                          $"energy {status.EnergyWh:0.00} Wh");
        Console.WriteLine($"device    model 0x{status.ModelId:X4} ({Sk150DeviceInfo.DescribeModel(status.ModelId)})   " +
                          $"firmware {status.FirmwareVersion}   slave {status.SlaveAddress}   " +
                          $"baud {(status.BaudRate?.ToString(CultureInfo.InvariantCulture) ?? $"index {status.BaudRateIndex}")}");
        if (status.Protection != Sk150ProtectionCode.None)
        {
            Console.Error.WriteLine($"warning: protection active ({status.Protection}, code {status.ProtectionRaw}).");
        }
    }

    private static void PrintInfo(Sk150DeviceInfo info, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(info, JsonOptions));
            return;
        }

        Console.WriteLine($"model     {info.ModelName} (0x{info.ModelId:X4})");
        Console.WriteLine($"firmware  {info.FirmwareVersion}");
        Console.WriteLine($"slave     {info.SlaveAddress}");
        Console.WriteLine($"baud      {(info.BaudRate?.ToString(CultureInfo.InvariantCulture) ?? $"index {info.BaudRateIndex}")}");
        Console.WriteLine($"probe     {(info.HasExternalProbe ? "connected" : "not connected")}");
    }

    private static void PrintPreset(Sk150Preset preset, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(preset, JsonOptions));
            return;
        }

        Console.WriteLine($"group M{preset.Index}");
        Console.WriteLine($"  set      {preset.Voltage,7:0.00} V {preset.Current,8:0.000} A");
        Console.WriteLine($"  OVP/OCP  {preset.OverVoltageProtection,7:0.00} V {preset.OverCurrentProtection,8:0.000} A");
        Console.WriteLine($"  LVP      {preset.LowVoltageProtection,7:0.00} V");
        Console.WriteLine($"  OPP      {preset.OverPowerProtection,7:0.0} W");
        Console.WriteLine($"  OTP      {preset.OverTemperatureProtection,7:0.0}");
        Console.WriteLine($"  limits   {preset.MaxRuntime:hh\\:mm}   {preset.MaxCapacityMah} mAh   " +
                          $"{preset.MaxEnergy10Mwh * 10} mWh");
        Console.WriteLine($"  power-on {(preset.PowerOnOutputEnabled ? "ON" : "OFF")}");
    }

    private static void PrintPorts()
    {
        IReadOnlyList<string> ports = Sk150Discovery.GetPortNames();
        if (ports.Count == 0)
        {
            Console.WriteLine("no serial ports found.");
            return;
        }

        foreach (string port in ports)
        {
            Console.WriteLine(port);
        }
    }

    private static string UnitSuffix(Sk150TemperatureUnit unit)
        => unit == Sk150TemperatureUnit.Fahrenheit ? "F" : "C";

    private static decimal ParseDecimal(IReadOnlyList<string> positional, int index, string name)
    {
        if (index >= positional.Count)
        {
            throw new ArgumentException($"missing {name} argument.");
        }

        if (!decimal.TryParse(positional[index], NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value))
        {
            throw new ArgumentException($"'{positional[index]}' is not a number ({name}).");
        }

        return value;
    }

    private static int ParseInt(IReadOnlyList<string> positional, int index, string name)
    {
        if (index >= positional.Count)
        {
            throw new ArgumentException($"missing {name} argument.");
        }

        if (!int.TryParse(positional[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            throw new ArgumentException($"'{positional[index]}' is not an integer ({name}).");
        }

        return value;
    }

    private static ushort ParseUShort(IReadOnlyList<string> positional, int index, string name)
    {
        if (index >= positional.Count)
        {
            throw new ArgumentException($"missing {name} argument.");
        }

        string text = positional[index];
        bool parsed = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ushort.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort value)
            : ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        if (!parsed)
        {
            throw new ArgumentException($"'{text}' is not a 16-bit value ({name}).");
        }

        return value;
    }

    private static bool ParseOnOff(IReadOnlyList<string> positional, int index)
    {
        if (index >= positional.Count)
        {
            throw new ArgumentException("missing on/off argument.");
        }

        return positional[index].ToLowerInvariant() switch
        {
            "on" or "1" or "true" => true,
            "off" or "0" or "false" => false,
            string other => throw new ArgumentException($"'{other}' is not on/off."),
        };
    }

    private static bool IsHelp(string arg) => arg is "-h" or "--help" or "help";

    private static void PrintHelp()
    {
        Console.WriteLine("""
            sk150 - control XY-SK / ZK-SK series buck-boost modules (SK150) over MODBUS RTU

            usage: sk150 <command> [args] [options]

            commands
              ports                       list serial ports
              scan                        sweep ports/baud rates and report the first device
              status                      read and print the full status block
              info                        read model, firmware, slave address, baud rate
              monitor [interval-ms]       print live measurements until Ctrl+C
              v <volts>                   set the output voltage
              i <amps>                    set the current limit
              set <volts> <amps>          write both setpoints in one transaction
              on | off                    enable / disable the output
              preset <0-9>                load stored setup group M0-M9
              preset-read <0-9>           print a stored setup group
              preset-write <0-9> [opts]   store a setup group (see preset options)
              reg-read <addr> [count]     read raw holding registers
              reg-write <addr> <value>    write one raw holding register
              backlight <0-5>             set screen brightness
              buzzer <on|off>             enable / disable the beeper

            options
              --port <name>               serial port, e.g. COM4 (omit to auto-scan)
              --baud <rate>               link speed, default 115200
              --slave <1-247>             MODBUS slave address, default 1
              --timeout <ms>              response timeout, default 500
              --mock                      run against an in-memory device, no hardware
              --json                      machine-readable output where supported
              --max-v <volts>             raise/lower the voltage guard (default 36)
              --max-i <amps>              raise/lower the current guard (default 5)
              --no-limits                 disable the nameplate guard

            preset options (preset-write)
              --v <volts> --i <amps>      setpoints (required)
              --ovp <volts> --ocp <amps> --opp <watts> --lvp <volts> --otp <deg>
              --max-hours <h> --max-minutes <m> --max-mah <mah> --max-wh10 <units>
              --power-on                  enable the output at power-on for this group

            examples
              sk150 ports
              sk150 scan
              sk150 --port COM4 status
              sk150 --port COM4 v 12.5
              sk150 --port COM4 i 2
              sk150 --port COM4 set 12.5 2
              sk150 --port COM4 on
              sk150 --mock status
            """);
    }
}

/// <summary>Parsed command-line options.</summary>
internal sealed class CliOptions
{
    public string? Port { get; private set; }
    public int Baud { get; private set; } = 115200;
    public byte Slave { get; private set; } = 1;
    public int TimeoutMs { get; private set; } = 500;
    public int IntervalMs { get; private set; } = 500;
    public bool Mock { get; private set; }
    public bool Json { get; private set; }
    public bool NoLimits { get; private set; }
    public decimal? MaxVoltage { get; private set; }
    public decimal? MaxCurrent { get; private set; }
    public string? Ports { get; private set; }

    public decimal? PresetVoltage { get; private set; }
    public decimal? PresetCurrent { get; private set; }
    public decimal? PresetOvp { get; private set; }
    public decimal? PresetOcp { get; private set; }
    public decimal? PresetOpp { get; private set; }
    public decimal? PresetLvp { get; private set; }
    public decimal? PresetOtp { get; private set; }
    public int? PresetMaxHours { get; private set; }
    public int? PresetMaxMinutes { get; private set; }
    public int? PresetMaxMah { get; private set; }
    public int? PresetMaxWh10 { get; private set; }
    public bool PresetPowerOn { get; private set; }

    public static (CliOptions Options, List<string> Positional) Parse(IReadOnlyList<string> args)
    {
        var options = new CliOptions();
        var positional = new List<string>();

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                positional.Add(arg);
                continue;
            }

            string name = arg[2..];
            string? inline = null;
            int equals = name.IndexOf('=', StringComparison.Ordinal);
            if (equals >= 0)
            {
                inline = name[(equals + 1)..];
                name = name[..equals];
            }

            string Next(string optionName)
            {
                if (inline is not null)
                {
                    return inline;
                }

                if (i + 1 >= args.Count)
                {
                    throw new ArgumentException($"option --{optionName} needs a value.");
                }

                return args[++i];
            }

            switch (name)
            {
                case "port":
                    options.Port = Next(name);
                    break;
                case "ports":
                    options.Ports = Next(name);
                    break;
                case "baud":
                    options.Baud = int.Parse(Next(name), CultureInfo.InvariantCulture);
                    break;
                case "slave":
                    options.Slave = byte.Parse(Next(name), CultureInfo.InvariantCulture);
                    break;
                case "timeout":
                    options.TimeoutMs = int.Parse(Next(name), CultureInfo.InvariantCulture);
                    break;
                case "interval":
                    options.IntervalMs = int.Parse(Next(name), CultureInfo.InvariantCulture);
                    break;
                case "max-v":
                    options.MaxVoltage = decimal.Parse(Next(name), CultureInfo.InvariantCulture);
                    break;
                case "max-i":
                    options.MaxCurrent = decimal.Parse(Next(name), CultureInfo.InvariantCulture);
                    break;
                case "mock":
                    options.Mock = true;
                    break;
                case "json":
                    options.Json = true;
                    break;
                case "no-limits":
                    options.NoLimits = true;
                    break;
                case "v":
                    options.PresetVoltage = decimal.Parse(Next(name), CultureInfo.InvariantCulture);
                    break;
                case "i":
                    options.PresetCurrent = decimal.Parse(Next(name), CultureInfo.InvariantCulture);
                    break;
                case "ovp":
                    options.PresetOvp = decimal.Parse(Next(name), CultureInfo.InvariantCulture);
                    break;
                case "ocp":
                    options.PresetOcp = decimal.Parse(Next(name), CultureInfo.InvariantCulture);
                    break;
                case "opp":
                    options.PresetOpp = decimal.Parse(Next(name), CultureInfo.InvariantCulture);
                    break;
                case "lvp":
                    options.PresetLvp = decimal.Parse(Next(name), CultureInfo.InvariantCulture);
                    break;
                case "otp":
                    options.PresetOtp = decimal.Parse(Next(name), CultureInfo.InvariantCulture);
                    break;
                case "max-hours":
                    options.PresetMaxHours = int.Parse(Next(name), CultureInfo.InvariantCulture);
                    break;
                case "max-minutes":
                    options.PresetMaxMinutes = int.Parse(Next(name), CultureInfo.InvariantCulture);
                    break;
                case "max-mah":
                    options.PresetMaxMah = int.Parse(Next(name), CultureInfo.InvariantCulture);
                    break;
                case "max-wh10":
                    options.PresetMaxWh10 = int.Parse(Next(name), CultureInfo.InvariantCulture);
                    break;
                case "power-on":
                    options.PresetPowerOn = true;
                    break;
                default:
                    throw new ArgumentException($"unknown option --{name}.");
            }
        }

        return (options, positional);
    }
}
