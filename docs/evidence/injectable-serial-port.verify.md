# injectable-serial-port 验证证据

> 生成：2026-02-19 · 规格：docs/specs/done/injectable-serial-port.md · 状态：完成
> 原始输出：`injectable-serial-port-red.txt`、`injectable-serial-port-green.txt`

## AC ↔ 验证映射

| 验收标准 | 做法 | 结果 | 原始输出 |
|---|---|---|---|
| AC-1 既有 45 测试不变 | 全量 `dotnet test` | 61 通过 / 0 失败（45 旧 + 16 新） | green.txt |
| AC-2 FC03/06/10 组帧往返 | 假串口预置 `ModbusRtu.Build*` 生成的响应帧，断言请求字节流与解析值 | 3 用例通过 | green.txt |
| AC-3 分片拼帧 / 静默超时 | 每次 `Read` 限 2 字节驱动拼帧；静默计划驱动超时 | 2 用例通过 | green.txt |
| AC-4 重试语义 | 2 次静默后成功 → 3 次写入；异常码 → 1 次写入即抛 | 2 用例通过 | green.txt |
| AC-5 零真实串口 | 新用例只依赖 `FakeSerialPort` / `MockRegisterChannel` | 全部通过，无硬件 | green.txt |
| AC-6 公开行为不回归 | 端口/从站/IsOpen/Dispose 幂等/缺 PortName 抛错 | 3 用例通过 | green.txt |
| AC-7 Release 0 警告 0 错误 | `dotnet build -c Release` | 0 警告 0 错误 | green.txt |

## Red（失败先行）

命令：`dotnet build Sk150PowerSupply.sln --nologo -v q` → 退出码 1

关键失败：

```
tests\Sk150PowerSupply.Tests\Fakes\FakeSerialPort.cs(40,40): error CS0246:
未能找到类型或命名空间名"ISerialPort"(是否缺少 using 指令或程序集引用?)
    0 个警告
    1 个错误
```

这是预期的第一步失败：测试替身实现尚不存在的 `ISerialPort`，驱动出接口契约。

**Red 后的第二次失败（测试自身缺陷，已修正）**：实现完成首轮后出现 60/61，
唯一失败为 `GetPortNames_UsesTheInjectedProviderWhenSupplied`——我断言了未排序的
`["COM9","COM3"]`，而 `GetPortNames` 的既有语义是排序后返回。属测试写错，已改为
断言 `["COM3","COM9"]`；产品代码未因此放宽既有行为。

## Green（通过）

- `dotnet build Sk150PowerSupply.sln -c Release --nologo` → **0 警告 0 错误**（退出码 0）
- `dotnet test Sk150PowerSupply.sln -c Release --nologo --no-build` → **通过 61 / 跳过 0 / 共 61**，344 ms

改动前基线（本轮开工时实测）：45/45 通过。改动后 61/61，新增 16 个用例。

## 未达成 / 主动放弃

无。AC-1…AC-7 全部达成。

超出规格的一处：`FindAsync` / `ProbeAsync` 的 `channelFactory` 参数位置与 `cancellationToken`
产生源码兼容性影响，经核查 CLI 的 3 处调用均用命名参数，未破坏既有调用方。

## 残余风险与后续

1. **接口同名冲突**：将来 MachineWorker 侧 `ISerialPort` 与本 SDK 同名，适配器需用全限定名。
2. **端口占用路径未测**：`Sk150LinkException` 包装 `UnauthorizedAccessException` 的分支需真实占用端口，未纳入自动化测试，保持原映射未改动。
3. **下一批次**（不在本规格范围）：`Sk150ChannelAdapter`（`ISerialPort` → `IRegisterChannel`）、按机型配置安全护栏、打 tag 发 GitHub Packages、供电 Step。
