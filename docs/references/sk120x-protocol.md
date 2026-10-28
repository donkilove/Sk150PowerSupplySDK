# SK120X / SK150 MODBUS RTU protocol reference

Reference note for the register map this SDK implements. Values were cross-checked against a physical
SK150 (firmware 142) and against the publicly documented map for the SK120X family:

- Source: <https://kbralten.github.io/device_protocols/sk120x.html> (SK120X Power Supply Modbus RTU
  Protocol Reference) — summarised here for offline reference, with the hardware observations this
  project added.

Transport: MODBUS RTU, 8N1, default **115200** baud, default slave **1**. Function codes used:
`0x03` read holding registers, `0x06` write single register, `0x10` write multiple registers.
Multi-byte fields are big-endian except the CRC-16 (poly `0xA001`, init `0xFFFF`), which is transmitted
low byte first.

## Status block, 0x0000-0x001D (30 registers)

| Address | Dec | Name | R/W | Unit | Scale |
| --- | --- | --- | --- | --- | --- |
| 0x0000 | 0 | V-SET | R/W | V | 100 |
| 0x0001 | 1 | I-SET | R/W | A | 1000 |
| 0x0002 | 2 | V-OUT | R | V | 100 |
| 0x0003 | 3 | I-OUT | R | A | 1000 |
| 0x0004 | 4 | POWER | R | W | 100 |
| 0x0005 | 5 | V-IN | R | V | 100 |
| 0x0006 | 6 | AH-LOW | R | mAh | 1 |
| 0x0007 | 7 | AH-HIGH | R | mAh | 1 |
| 0x0008 | 8 | WH-LOW | R | mWh | 1 |
| 0x0009 | 9 | WH-HIGH | R | mWh | 1 |
| 0x000A | 10 | OUT_H | R | h | 1 |
| 0x000B | 11 | OUT_M | R | min | 1 |
| 0x000C | 12 | OUT_S | R | s | 1 |
| 0x000D | 13 | T_IN | R | °C/F | 10 |
| 0x000E | 14 | T_EX | R | °C/F | 10 |
| 0x000F | 15 | LOCK | R/W | — | 1 |
| 0x0010 | 16 | PROTECT | R (mostly) | — | 1 |
| 0x0011 | 17 | CVCC | R | — | 1 |
| 0x0012 | 18 | ONOFF | R/W | — | 1 |
| 0x0013 | 19 | F-C | R/W | — | 1 |
| 0x0014 | 20 | B-LED | R/W | — | 1 |
| 0x0015 | 21 | SLEEP | R/W | min | 1 |
| 0x0016 | 22 | MODEL | R | — | 1 |
| 0x0017 | 23 | VERSION | R | — | 1 |
| 0x0018 | 24 | SLAVE-ADD | R/W | — | 1 |
| 0x0019 | 25 | BAUDRATE | R/W | — | 1 |
| 0x001A | 26 | T-IN-CAL | R/W | °C/F | 10 |
| 0x001B | 27 | T-EX-CAL | R/W | °C/F | 10 |
| 0x001C | 28 | BUZZER | R/W | — | 1 |
| 0x001D | 29 | EXTRACT-M | R/W | — | 1 |

## Preset groups M0-M9

Address = `0x0050 + group * 0x0010`, 14 registers per group. M0 is the power-on group; M1/M2 are the
front-panel quick-access groups.

| Offset | Name | R/W | Unit | Scale |
| --- | --- | --- | --- | --- |
| +0x00 | V-SET | R/W | V | 100 |
| +0x01 | I-SET | R/W | A | 1000 |
| +0x02 | S-LVP | R/W | V | 100 |
| +0x03 | S-OVP | R/W | V | 100 |
| +0x04 | S-OCP | R/W | A | 1000 |
| +0x05 | S-OPP | R/W | W | 10 |
| +0x06 | S-OHP_H | R/W | h | 1 |
| +0x07 | S-OHP_M | R/W | min | 1 |
| +0x08 | S-OAH_L | R/W | mAh | 1 |
| +0x09 | S-OAH_H | R/W | mAh | 1 |
| +0x0A | S-OWH_L | R/W | 10 mWh | 1 |
| +0x0B | S-OWH_H | R/W | 10 mWh | 1 |
| +0x0C | S-OTP | R/W | °C/F | 1 |
| +0x0D | S-INI | R/W | — | 1 (power-on output state) |

Worked examples: M0 V-SET = 0x0050, M1 V-SET = 0x0060, M2 I-SET = 0x0071.

## Protection codes (0x0010)

| Value | Code | Meaning |
| --- | --- | --- |
| 0 | Normal | operating normally |
| 1 | OVP | over voltage |
| 2 | OCP | over current |
| 3 | OPP | over power |
| 4 | LVP | input undervoltage |
| 5 | OAH | capacity limit reached |
| 6 | OHP | runtime limit reached |
| 7 | OTP | over temperature (internal) |
| 8 | OEP | external probe temperature |
| 9 | OWH | energy limit reached |
| 10 | ICP | input / reverse protection |

## Baud rate index (0x0019)

| Index | Baud |
| --- | --- |
| 0 | 9600 |
| 1 | 14400 |
| 2 | 19200 |
| 3 | 38400 |
| 4 | 56000 |
| 5 | 57600 |
| 6 | 115200 |

## Hardware observations not in the vendor documentation

- **No NTC probe fitted:** register 0x000E reads **8888** (= 888.8 in the configured unit). The SDK
  maps that value to "no probe" (`Sk150Status.TemperatureEx == null`) rather than reporting 888.8 °C.
- **Model id:** the SK150 reports **0x5960** in register 0x0016; firmware 142 was seen on register 0x0017.
- **Zero-visible-current quirk:** with the output enabled and no load, the measured current register
  reports single digits (a few mA) rather than exactly zero — normal for a low-side shunt amplifier.
