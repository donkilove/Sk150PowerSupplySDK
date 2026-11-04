# 串口传输可注入化（injectable-serial-port）

> 状态：done · 日期：2026-02-19

## 目标

让 `Sk150PowerSupplySDK` 的串口传输可注入，从而：

1. 补上 `SerialRegisterChannel` 与 `Sk150Discovery` 的测试覆盖缺口（改造前零覆盖）；
2. 为后续 MachineWorker 的 `Sk150ChannelAdapter` 提供可测前提。

本轮只改 SDK，不碰 MachineWorker、不改 Web 面板、不打 tag 不发包。

## 范围与非目标

**范围内**

| 文件 | 动作 |
|---|---|
| `src/Sk150PowerSupply/ISerialPort.cs` | 新增公开接口 |
| `src/Sk150PowerSupply/SystemSerialPort.cs` | 新增默认实现（唯一实例化 `SerialPort` 之处） |
| `src/Sk150PowerSupply/SerialRegisterChannel.cs` | 依赖抽象 + 新增两个构造函数 |
| `src/Sk150PowerSupply/Sk150Discovery.cs` | 端口枚举与通道工厂可注入 |
| `src/Sk150PowerSupply/Sk150PowerSupply.csproj` | 版本 0.1.0 → 0.2.0 |
| `tests/.../Fakes/FakeSerialPort.cs` | 新增测试替身 |
| `tests/.../SerialRegisterChannelTests.cs` | 新增 10 个用例 |
| `tests/.../Sk150DiscoveryTests.cs` | 新增 6 个用例 |

**非目标**

- MachineWorker 侧适配器与 Step（下一批次）
- Web 面板任何改动（用户明确为实验工具）
- 打 tag、发布任何包
- 机台控制逻辑
- 重排或修改既有 45 个测试

## 验收标准

| 编号 | 内容 |
|---|---|
| AC-1 | 既有 45 个测试语义不变，全部通过 |
| AC-2 | 经假串口喂真实 MODBUS 帧，验证 FC03/FC06/FC10 请求的组帧字节与解析结果 |
| AC-3 | 收帧循环：分片到达能拼成完整帧；线路静默抛 `Sk150TimeoutException` |
| AC-4 | 重试语义：`Sk150LinkException` 重试至 `Retries`；`ModbusException` 绝不重试 |
| AC-5 | 全部新用例零真实串口 |
| AC-6 | `PortName`/`SlaveAddress`/`IsOpen`/Dispose 幂等/缺 PortName 抛 `ArgumentException` 不回归 |
| AC-7 | Release 构建 0 警告 0 错误 |

## 验收标准 ↔ 测试用例映射

| 验收标准 | 测试用例 | 位置 |
|---|---|---|
| AC-1 | 全量 `dotnet test`：61 通过 / 0 失败 | `docs/evidence/injectable-serial-port-green.txt` |
| AC-2 | `ReadHoldingRegistersAsync_SendsFc03AndParsesTheResponse`、`WriteSingleRegisterAsync_SendsFc06AndAcceptsTheEcho`、`WriteMultipleRegistersAsync_SendsFc10InOneTransaction` | `SerialRegisterChannelTests.cs` |
| AC-3 | `ReadHoldingRegistersAsync_AssemblesAFragmentedResponse`、`ReadHoldingRegistersAsync_ThrowsTimeoutWhenTheLineStaysSilent` | 同上 |
| AC-4 | `ReadHoldingRegistersAsync_RetriesLinkFailuresUpToTheConfiguredCount`、`ReadHoldingRegistersAsync_DoesNotRetryModbusExceptions` | 同上 |
| AC-5 | 全部新用例只依赖 `FakeSerialPort` / `MockRegisterChannel` | 两个测试文件 |
| AC-6 | `PortAndSlaveAddressAreExposedFromEffect`、`DisposeIsIdempotentAndClosesThePort`、`OptionsConstructor_StillRejectsAMissingPortName` | 同上 |
| AC-7 | Release 构建日志：0 警告 0 错误 | `docs/evidence/injectable-serial-port-green.txt` |
| — | 发现层 6 个用例（`Sk150DiscoveryTests.cs`）覆盖端口注入、排序、首个应答、全静默、探针成功/异常 | `Sk150DiscoveryTests.cs` |

## 数据流与接口变化

**新增公开类型**

```csharp
public interface ISerialPort : IDisposable
{
    string PortName { get; }
    int BaudRate { get; }
    bool IsOpen { get; }
    int BytesToRead { get; }
    int ReadTimeout { get; set; }
    int WriteTimeout { get; set; }
    void Open();
    void Close();
    int Read(byte[] buffer, int offset, int count);
    void DiscardInBuffer();
    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);
    Task FlushAsync(CancellationToken ct = default);
}

public sealed class SystemSerialPort : ISerialPort
{
    public SystemSerialPort(string portName, int baudRate);
}
```

**新增公开重载（既有签名零改动）**

```csharp
// SerialRegisterChannel
public SerialRegisterChannel(ISerialPort port, Sk150Options options);
public SerialRegisterChannel(Sk150Options options, Func<Sk150Options, ISerialPort> portFactory);
// 既有两个构造函数保持原签名与原行为

// Sk150Discovery（新参数均为可选，追加在末尾）
public static IReadOnlyList<string> GetPortNames(Func<IEnumerable<string>>? portNameProvider = null);
public static Task<Sk150DeviceInfo?> ProbeAsync(..., Func<Sk150Options, IRegisterChannel>? channelFactory = null, ...);
public static Task<Sk150ProbeResult?> FindAsync(..., Func<Sk150Options, IRegisterChannel>? channelFactory = null, Func<IEnumerable<string>>? portNameProvider = null, ...);
```

**数据流不变**：`Sk150Options` → channel → `ModbusRtu` 组帧 → 串口字节。唯一变化是字节来源从具体 `SerialPort` 变为 `ISerialPort`。收帧循环、重试逻辑、异常映射**一行未改**。

由 `ISerialPort` 与 `IRegisterChannel` 的既有公开构造函数 `Sk150PowerSupply(IRegisterChannel, Sk150Options?)` 组合，宿主程序（如 MachineWorker）无需修改 SDK 即可接入自己的串口实现。

## 边界情况与失败处理

| 情形 | 行为 | 覆盖 |
|---|---|---|
| 分片到达 | 轮询拼齐至期望长度，或达最小帧长后线路静默 | 测试 |
| 线路静默 | `Sk150TimeoutException`，消息含端口名 | 测试 |
| 链接失败 | 重试至 `Retries` 次，间隔 `RetryDelay` | 测试 |
| 设备异常码 | `ModbusException` 立即抛出，不重试 | 测试 |
| Dispose 重复调用 | 幂等 | 测试 |
| 缺 `PortName`（options 构造函数） | `ArgumentException` | 测试 |
| 端口被占用 | `Sk150LinkException` 包装 `UnauthorizedAccessException` | 保持原映射（需真实端口，未测） |
| 端口工厂返回 null | `ArgumentException` | 代码防护 |

## 测试方案

沿用仓库既有设施，不引入新框架：xUnit 2.9.3 + VSTest，工程 `tests/Sk150PowerSupply.Tests`（已有 `InternalsVisibleTo`）。

运行命令：

```bash
dotnet test Sk150PowerSupply.sln --nologo
dotnet build Sk150PowerSupply.sln -c Release --nologo
```

关键设计：`FakeSerialPort` 在 `DiscardInBuffer()` 时推进响应计划——真实实现每次请求前都清空输入缓冲，因此该调用即为「请求边界」，可精确表达「第一次静默、第二次应答」。未预置计划的请求一律静默，避免计划耗尽后测试变成假成功。

全部新用例经字节流驱动，进程内不打开任何真实串口，故 CI 的 `ubuntu-latest` 可正常执行。

## 假设与风险

**假设**：① 本轮不含 MachineWorker 侧代码；② 版本号 0.2.0（新增公开类型属特性增量）；③ 无未闭环事项需登记 `TODO.md`。

**风险与处置**

- **R1 `ReadFrameAsync` 的同步读语义**：原实现刻意用同步 `Read` 而非 `ReadAsync`（Windows 上取消挂起读抛 winerror 995 并污染缓冲）。处置：`ISerialPort.Read` 保持同步签名，收帧循环未改动，注释原文保留并更新 cref。
- **R2 `ReadTimeout` 必须暴露**：收帧循环依赖无数据时抛 `TimeoutException`。处置：接口保留该属性，`SystemSerialPort` 直通。
- **R3 命名冲突**：未来 MachineWorker 侧的 `ISerialPort` 与本接口同名，适配器需用全限定名。处置：留待下一批次处理。
- **回滚方案**：改动全为「新增类型 + 可选参数 + 新增重载」，`git revert` 单个提交即可完全还原。无数据迁移、无不可逆副作用。注意回滚后新增测试将编译失败，需一并回滚测试文件。

## 变更记录

| 时间 | 变化点 | 重新确认 |
|---|---|---|
| 2026-02-19 | 初版规格经 plan mode 批准 | 用户批准 |
| 2026-02-19 | 修正：`ReadFrameAsync` 实为轮询式（非阻塞读），适配难度低于初判 | 实现中更正，未影响范围 |

## 决策记录

- **决策日期**：2026-02-19
- **决策**：采用「方案 A」——只抽象 7 组成员（含 `Read`/`BytesToRead`/`ReadTimeout`/`ReadTimeout` 写/`DiscardInBuffer`/`WriteAsync`/`FlushAsync`），不抽象 `BaseStream`。
- **理由**：原实现的收帧循环已经是轮询式（`BytesToRead` + `Task.Delay(1)` + deadline），抽象出这组原语即可完整替换字节来源，收帧与重试逻辑无需改写；`ReadTimeout` 必须进接口，因为循环依赖无数据时抛 `TimeoutException`。
- **被放弃的备选**：
  - 方案 B（连 `BaseStream` 一并抽象）——徒增复杂度，且会让「同步读」的警示注释失去意义。
  - 方案 C（只加串口工厂委托，不改接口）——宿主侧拿到的仍是具体类，无法单测，不满足可测前提。
- **未达成的 AC**：无。AC-1…AC-7 全部达成。
