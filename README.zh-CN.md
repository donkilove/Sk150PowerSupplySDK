# Sk150PowerSupplySDK

用于控制 **XY-SK / ZK-SK 系列数控升降压电源模块**（SK60、SK120、SK150）的 .NET 库与命令行工具，
通过模块的 MODBUS RTU 串口通信。

- `Sk150PowerSupply` —— 库：状态解码、设定值、保护阈值、预设组、输出控制、实时监视。
- `sk150` —— 命令行：隔空读屏、设电压电流、开关输出、载入预设、读写原始寄存器。
- 全离线可用：整套 API 可跑在内存模拟设备上，测试和演示都不需要硬件。

寄存器地址与换算在系列内通用；寄存器表已在一台 **固件 142 的 SK150** 上验证。寄存器表及出处见
[`docs/references/sk120x-protocol.md`](docs/references/sk120x-protocol.md)。

## 接线

模块上的 4pin 排针是 **3.3/5 V TTL 串口，不隔离** —— 它的 GND 与输入电源负极是同一个节点。

| 模块排针 | USB-TTL 适配器 | 说明 |
| --- | --- | --- |
| TX | RXD | 交叉：模块发、适配器收 |
| RX | TXD | 交叉：适配器发、模块收 |
| GND | GND | 必须共地，这根不能省 |
| 5V | 不要接 | 这是模块的 5 V **输出**，不是输入 |

通信时模块必须由主输入供电（屏幕点亮）。默认链路：**115200 8N1，从机地址 1**。

## 安装

```bash
dotnet add package Sk150PowerSupplySDK
```

需要 .NET 10 及以上（唯一依赖是 `System.IO.Ports` 10.0.0）。

## 库用法

```csharp
using Sk150PowerSupply;

await using Sk150PowerSupply psu = await Sk150PowerSupply.OpenAsync("COM4");

Sk150Status status = await psu.ReadStatusAsync();
Console.WriteLine(status);   // output ON ConstantVoltage 11.98 V 1.234 A ...

await psu.SetVoltageAndCurrentAsync(12.5m, 2.0m);   // 一次 MODBUS 事务写入两个设定值
await psu.TurnOnAsync();

await foreach (Sk150Status reading in psu.MonitorAsync(TimeSpan.FromMilliseconds(500)))
{
    Console.WriteLine($"{reading.VoltageOut:0.00} V {reading.CurrentOut:0.000} A {reading.PowerOut:0.00} W");
}
```

预设组 M0-M9：

```csharp
Sk150Preset preset = await psu.ReadPresetAsync(3);
Console.WriteLine(preset);                       // M3: 12.00 V / 2.000 A, OVP 15.00 V, ...

preset = preset with { Voltage = 9.0m, Current = 1.0m };
await psu.WritePresetAsync(3, preset);           // 0x0050 + 3 * 0x10 起 14 个寄存器
await psu.LoadPresetAsync(3);                    // 把 M3 套用到当前设定
```

### 安全限值

每次写电压/电流都会过一遍 `Sk150Options.SafetyLimits`，默认按 SK150 铭牌（36 V / 5 A）。超限是
**抛异常拒绝，而不是悄悄钳位**，这样单位搞错时会立刻暴露，而不是安静地设错。

```csharp
await using Sk150PowerSupply small = await Sk150PowerSupply.OpenAsync(new Sk150Options
{
    PortName = "COM4",
    SafetyLimits = new Sk150SafetyLimits(MaxVoltage: 24m, MaxCurrent: 2m),
});

// Sk150SafetyLimits.None 完全关闭这层保护。
```

### 没有硬件？用模拟设备

```csharp
using Sk150PowerSupply psu = Sk150PowerSupply.CreateMock();
Sk150Status status = await psu.ReadStatusAsync();   // 一份合理的 SK150 快照
```

`MockRegisterChannel` 还会记录每次写入（`Writes`）、允许直接读写寄存器（`mock[0x0012] = 1`）、
并可注入超时（`mock.QueueTimeouts(1)`）用于测试重试。

## 命令行

可以直接指定串口，也可以让它自己找：

```bash
sk150 ports                          # 列出串口
sk150 scan                           # 扫串口 × 波特率，报到第一个 SK150
sk150 --port COM4 status             # 读全状态
sk150 --port COM4 monitor            # 实时刷新，Ctrl+C 停止
sk150 --port COM4 v 12.5             # 设 12.50 V
sk150 --port COM4 i 2                # 限流 2.000 A
sk150 --port COM4 set 12.5 2         # 一次写入两个设定值
sk150 --port COM4 on | off           # 开 / 关输出
sk150 --port COM4 preset 3           # 载入 M3
sk150 --port COM4 preset-read 3      # 打印 M3
sk150 --port COM4 reg-read 0x0000 30 # 读原始寄存器
sk150 --mock status                  # 不接硬件也能跑
```

不安装也能直接从仓库运行：

```bash
dotnet run --project src/Sk150PowerSupply.Cli -- --port COM4 status
```

其它参数：`--baud`、`--slave`、`--timeout <ms>`、`--json`、`--max-v`、`--max-i`、`--no-limits`。
不给 `--port` 时会先扫描并把选中的串口打印出来。

## 寄存器表（摘要）

| 寄存器 | 含义 | 换算 |
| --- | --- | --- |
| 0x0000 / 0x0001 | 设定电压 / 设定电流 | ÷100 / ÷1000 |
| 0x0002 / 0x0003 | 实测电压 / 实测电流 | ÷100 / ÷1000 |
| 0x0004 / 0x0005 | 实测功率 / 输入电压 | ÷100 / ÷100 |
| 0x0006-0x0009 | 累计容量（mAh）与累计能量（mWh），低/高 16 位 | 1 |
| 0x000A-0x000C | 运行时间 时 / 分 / 秒 | 1 |
| 0x000D / 0x000E | 内部 / 外部探头温度 | ÷10 |
| 0x0010 / 0x0011 | 保护码 / CV-CC 模式 | 1 |
| 0x0012 | 输出开关 | 1 |
| 0x0016 / 0x0017 | 型号 ID（SK150 = 0x5960）/ 固件版本 | 1 |
| 0x0018 / 0x0019 | 从机地址 / 波特率索引 | 1 |
| 0x001D | 写 0-9 载入预设组 M0-M9 | 1 |
| 0x0050 + N·0x0010 | 第 N 组预设，共 14 个寄存器 | 混合 |

## 构建与测试

```bash
dotnet build Sk150PowerSupply.sln
dotnet test Sk150PowerSupply.sln
```

## 说明与限制

- 已在一台固件 142 的真实 SK150 上做过完整验证：本文件里的每条命令都跑过真机，包括设定值写入、
  输出开关、预设写入与还原。运行记录见
  [`docs/references/hardware-verification.md`](docs/references/hardware-verification.md)。
  SK60/SK120 据说共用同一套寄存器表，但本地未实测。
- 没插 NTC 探头时 0x000E 会报 **8888**，库把它转成 `TemperatureEx == null`。这个哨兵值不在官方文档里。
- 改波特率（0x0019）或从机地址（0x0018）会立即生效：改完要用新参数重开链路。
- `SerialRegisterChannel` 故意用同步轮询读串口。Windows 上 `SerialPort.BaseStream` 的异步读无法
  安全取消：取消会抛 `ERROR_OPERATION_ABORTED` 而不是 `OperationCanceledException`，还会把缓冲区
  留在半帧位置，表现成莫名其妙的 CRC 错误。别把这层循环"现代化"回 `ReadAsync`。
- 排针不隔离。上电时别碰输出端，抬设定电压之前先把过压保护设到负载能承受的位置。

## 许可

MIT —— 见 [LICENSE](LICENSE)。
