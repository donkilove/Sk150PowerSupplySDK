# Sk150PowerSupplySDK

A .NET library and command-line tool for controlling **XY-SK / ZK-SK series CNC buck-boost power
modules** — SK60, SK120, SK150 — over their MODBUS RTU serial link.

- `Sk150PowerSupply` — the library: status decoding, setpoints, protection thresholds, preset groups,
  output control, and a live monitor.
- `sk150` — the CLI: read the display over the wire, set voltage and current, switch the output, load
  presets, and dump raw registers.
- Offline-first: the whole API runs against an in-memory device, so tests and demos need no hardware.

Register addresses and scaling are shared across the family; the map was verified against an
**SK150 running firmware 142**. See [`docs/references/sk120x-protocol.md`](docs/references/sk120x-protocol.md)
for the register table and its source.

## Wiring

The 4-pin header on the module is a **3.3/5 V TTL UART, not isolated** — its GND is the same node as
the input supply negative.

| Module pin | USB-TTL adapter | Note |
| --- | --- | --- |
| TX | RXD | crossed: module transmits, adapter receives |
| RX | TXD | crossed: adapter transmits, module receives |
| GND | GND | required — a shared ground is not optional |
| 5V | leave unconnected | this is the module's 5 V *output*, not an input |

The module must be powered from its main input (screen lit) while the UART is in use. Default link:
**115200 8N1, slave address 1**.

## Install

```bash
dotnet add package Sk150PowerSupplySDK
```

Requires .NET 10 or later (`System.IO.Ports` 10.0.0 is the only dependency).

## Library

```csharp
using Sk150PowerSupply;

await using Sk150PowerSupply psu = await Sk150PowerSupply.OpenAsync("COM4");

Sk150Status status = await psu.ReadStatusAsync();
Console.WriteLine(status);   // output ON ConstantVoltage 11.98 V 1.234 A ...

await psu.SetVoltageAndCurrentAsync(12.5m, 2.0m);   // one MODBUS transaction
await psu.TurnOnAsync();

await foreach (Sk150Status reading in psu.MonitorAsync(TimeSpan.FromMilliseconds(500)))
{
    Console.WriteLine($"{reading.VoltageOut:0.00} V {reading.CurrentOut:0.000} A {reading.PowerOut:0.00} W");
}
```

Preset groups M0-M9:

```csharp
Sk150Preset preset = await psu.ReadPresetAsync(3);
Console.WriteLine(preset);                       // M3: 12.00 V / 2.000 A, OVP 15.00 V, ...

preset = preset with { Voltage = 9.0m, Current = 1.0m };
await psu.WritePresetAsync(3, preset);           // 14 registers at 0x0050 + 3 * 0x10
await psu.LoadPresetAsync(3);                    // applies M3 to the live setpoints
```

### Safety limits

Every voltage and current write is checked against `Sk150Options.SafetyLimits`, which defaults to the
SK150 nameplate (36 V / 5 A). Values above the limit are **rejected, not clamped**, so a wrong-unit
mistake fails loudly instead of quietly setting the wrong setpoint.

```csharp
await using Sk150PowerSupply small = await Sk150PowerSupply.OpenAsync(new Sk150Options
{
    PortName = "COM4",
    SafetyLimits = new Sk150SafetyLimits(MaxVoltage: 24m, MaxCurrent: 2m),
});

// Sk150SafetyLimits.None disables the guard entirely.
```

### No hardware? Use the mock

```csharp
using Sk150PowerSupply psu = Sk150PowerSupply.CreateMock();
Sk150Status status = await psu.ReadStatusAsync();   // a plausible SK150 snapshot
```

`MockRegisterChannel` also records every write (`Writes`), lets you poke registers directly
(`mock[0x0012] = 1`), and can inject timeouts (`mock.QueueTimeouts(1)`) for retry tests.

## CLI

The CLI finds the device, or you point it at a port:

```bash
sk150 ports                          # list serial ports
sk150 scan                           # sweep ports x baud rates, report the first SK150
sk150 --port COM4 status             # full readout
sk150 --port COM4 monitor            # live measurements, Ctrl+C to stop
sk150 --port COM4 v 12.5             # set 12.50 V
sk150 --port COM4 i 2                # current limit 2.000 A
sk150 --port COM4 set 12.5 2         # both setpoints in one transaction
sk150 --port COM4 on | off           # output enable / disable
sk150 --port COM4 preset 3           # load stored group M3
sk150 --port COM4 preset-read 3      # print stored group M3
sk150 --port COM4 reg-read 0x0000 30 # raw registers
sk150 --mock status                  # run without hardware
```

Run it from the repository without installing:

```bash
dotnet run --project src/Sk150PowerSupply.Cli -- --port COM4 status
```

Options: `--baud`, `--slave`, `--timeout <ms>`, `--json`, `--max-v`, `--max-i`, `--no-limits`.
If `--port` is omitted the CLI scans for the device and reports the port it picked.

## Register map (summary)

| Register | Meaning | Scale |
| --- | --- | --- |
| 0x0000 / 0x0001 | voltage setpoint / current limit | ÷100 / ÷1000 |
| 0x0002 / 0x0003 | measured voltage / current | ÷100 / ÷1000 |
| 0x0004 / 0x0005 | measured power / input voltage | ÷100 / ÷100 |
| 0x0006-0x0009 | accumulated capacity (mAh) and energy (mWh), low/high halves | 1 |
| 0x000A-0x000C | runtime hours / minutes / seconds | 1 |
| 0x000D / 0x000E | internal / external probe temperature | ÷10 |
| 0x0010 / 0x0011 | protection code / CV-CC mode | 1 |
| 0x0012 | output enable | 1 |
| 0x0016 / 0x0017 | model id (SK150 = 0x5960) / firmware version | 1 |
| 0x0018 / 0x0019 | slave address / baud rate index | 1 |
| 0x001D | write 0-9 to load preset group M0-M9 | 1 |
| 0x0050 + N·0x0010 | preset group N, 14 registers | mixed |

## Build and test

```bash
dotnet build Sk150PowerSupply.sln
dotnet test Sk150PowerSupply.sln
```

## Notes and limitations

- Verified end to end against one physical SK150 on firmware 142: every command in this README was
  run against real hardware, including setpoint writes, output switching and a preset write/restore.
  The transcript is in [`docs/references/hardware-verification.md`](docs/references/hardware-verification.md).
  Other family members (SK60, SK120) are reported to share the map but have not been tested here.
- Register 0x000E reports **8888** when no NTC probe is fitted; the library surfaces that as
  `TemperatureEx == null`. The sentinel is not in the vendor documentation.
- Changing the baud rate (0x0019) or the slave address (0x0018) takes effect immediately: reopen the
  link at the new settings afterwards.
- `SerialRegisterChannel` reads the port synchronously and poll-driven on purpose. async reads on
  `SerialPort.BaseStream` cannot be cancelled safely on Windows: a cancelled read raises
  `ERROR_OPERATION_ABORTED` instead of `OperationCanceledException` and leaves the buffer mid-frame,
  which shows up as bogus CRC errors. Do not "modernise" that loop back into `ReadAsync`.
- The header is not isolated. Do not touch the output terminals while the input is live, and set the
  over-voltage protection to something your load can survive before raising the setpoint.

## License

MIT — see [LICENSE](LICENSE).
