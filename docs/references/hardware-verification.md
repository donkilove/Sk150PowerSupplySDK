# Hardware verification log

One full read/write pass of the SDK against a physical SK150, driven entirely through
`src/Sk150PowerSupply.Cli`. Environment: Windows, USB-to-TTL adapter on **COM4**, MODBUS RTU
115200 8N1, slave 1, module firmware 142, input 24.61 V.

## Baseline (before any write)

```
model     SK150 (0x5960)
firmware  142
slave     1
baud      115200
probe     not connected

output    ON   mode ConstantVoltage   protection None
set          3.30 V    0.100 A
actual       3.30 V    0.004 A     0.01 W
input       24.61 V   temp  34.6 C   external not connected
runtime   00:27:47   capacity 0.001 Ah   energy 0.00 Wh
```

Registers 0x0013-0x001C read back as `0, 5, 0, 0x5960, 142, 1, 6, 0, 0, 1` — backlight 5, model
0x5960, firmware 142, slave 1, baud index 6 (115200), buzzer on.

## Steps and results

| # | Action | Observed |
| --- | --- | --- |
| 1 | `status`, `info` | frame accepted, CRC ok, values as listed above |
| 2 | `preset-read 0` / `preset-read 1` | M0 3.30 V / 0.100 A, M1 5.00 V / 5.100 A, both OVP 38 V, OCP 5.2 A, LVP 5.5 V, OPP 65 W, OTP 95 |
| 3 | `v 40` (over the 36 V guard) | rejected client-side: `error: Voltage 40 V exceeds the configured limit of 36 V.` — nothing written |
| 4 | `set 3.5 0.3` (single FC10 transaction) | read back exactly `3.50 V / 0.300 A`, actual output followed to 3.50 V |
| 5 | `off` | `ONOFF` flipped to 0; actual fell 3.50 V → 2.70 V (output capacitor discharging, load nearly open) |
| 6 | `on` | `ONOFF` back to 1, actual returned to 3.19 V and settled at 3.40 V after the next setpoint |
| 7 | `v 3.4`, `i 0.2` (FC06 each) | read back `3.40 V / 0.200 A` |
| 8 | `monitor 300` | continuous readings ~1 per 300 ms, all fields plausible |
| 9 | `preset-write 9 --v 12 --i 1 ...` | acknowledged, `preset-read 9` returned 12.00 V / 1.000 A |
| 10 | `preset-write 9 --v 24 --i 5.1 ...` (restore) | `preset-read 9` returned the original 24.00 V / 5.100 A |
| 11 | `off`, `preset 9` | setpoints became 24.00 V / 5.100 A while the output stayed off |
| 12 | `set 3.3 0.1`, `on` (restore baseline) | final status identical to baseline except runtime/capacity counters |

Device left in its original state: output **ON**, 3.30 V, 0.100 A, preset M9 unchanged.

## Bug found and fixed during this run

First attempts failed with `CRC mismatch: computed 0x907A, frame carried 0x225A` and
`Read from COM4 failed: The I/O operation has been aborted because of either a thread exit or an
application request.`, while the same request sent from Python returned a clean 65-byte frame.

Cause: `SerialRegisterChannel.ReadFrameAsync` used `SerialPort.BaseStream.ReadAsync` with a
`CancellationTokenSource.CancelAfter(...)` slice. On Windows, cancelling a pending read surfaces as
`IOException` winerror 995 (`ERROR_OPERATION_ABORTED`) rather than `OperationCanceledException`, so
the timeout path leaked an exception *and* left the input buffer mid-frame; the next parse then ran
its CRC check over stale bytes.

Fix: `ReadFrameAsync` now polls `SerialPort.BytesToRead` and uses synchronous `SerialPort.Read`
(honouring `ReadTimeout`), never cancelling a pending I/O. Same code path now returns the correct
frame on the first attempt.

## Observation worth keeping

- 0x000E reads **8888** with no NTC probe attached — confirmed on hardware, and the SDK maps it to
  "no probe" rather than 888.8 °C.
- Measured current never quite reaches 0 A with the output enabled and nothing connected: 4-5 mA
  (`0x0003` = 4 or 5), consistent with a low-side shunt amplifier offset.
- Opening the port while DTR/RTS are asserted silenced the module entirely in a raw-pyserial test;
  the SDK opens with `DtrEnable = false` and `RtsEnable = false`, which works.
