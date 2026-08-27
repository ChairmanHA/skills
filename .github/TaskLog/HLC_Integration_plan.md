# HLC (Hardware Layer Control) 集成方案与实施计划

**日期**: 2026-01-20
**状态**: 计划中 (Planned)
**任务目标**: 集成基于 `hlc` 命令行工具的硬件控制功能（电池、背光、风扇、震动），并确保跨平台兼容性（Linux/Windows）。

## 1. 需求分析

需要在 VNA-GUI 客户端中集成对嵌入式硬件的控制和监控。
- **监控**: 获取并显示电池电量与预期剩余时间（Battery Lifetime）。
- **控制**: 调节屏幕亮度、风扇转速、测试震动反馈。
- **平台策略**:
    - **Linux (Raspberry Pi)**: 通过 `QProcess` 调用 `hlc` 命令实际执行。
    - **Windows/Desktop**: 屏蔽实际调用，返回模拟数据或默认值，防止报错。

## 2. 架构设计

### 2.1 核心模块: `HardwareController` (Singleton)
创建一个单例类 `HardwareController` 全权负责与操作系统/`hlc` 交互。任何 UI 组件不应直接调用系统命令。

-   **平台隔离实现 (`.cpp`)**:
    使用预编译指令 `#ifdef Q_OS_LINUX` 包裹 `QProcess` 的调用逻辑。
    在 `#else` (Windows) 分支中，函数体为空或仅输出 qDebug 日志，`getBatteryLevel()` 恒定返回 100。

## 3. `hlc` 命令参考 (2026-01-20 Updated)

### Command Help
```text
hlc [OPTIONS]

OPTIONS:
  -h,     --help              Print this help message and exit
  -v,     --version           Query HLC Version
  -b,     --battery           Display battery information
          --bright            Query Screen Brightness (0 - 1)
          --set-bright FLOAT  Set Screen Brightness (0 - 1)
          --trigger-feedback  Trigger Haptic feedback
          --feedback-strength Query Haptic feedback strength
          --feedback-duration Query Haptic feedback duration
          --set-feedback-strength FLOAT
                              Set Haptic feedback strength
          --set-feedback-duration FLOAT
                              Set Haptic feedback duration
          --fan-rpm           Query fan rpm
          --set-fan-rpm FLOAT Set fan rpm
  -i,     --info              Query Device info
```

### `hlc -b` Output Example
```text
AC Line Statue: Unknown
Battery Charging Status: Yes
Battery Percent: 24%
Battery Lifetime: 33 min
Battery Life CapRep: 3706mAh
Battery FullCapRep: 15258mAh
Battery Voltage: 7.85219V
Battery Current: -4.0375A
```
*Target: Parse "Battery Percent: 24%"*

### `hlc --trigger-feedback` Output Example
```text
htra@raspberrypi:~$ hlc --trigger-feedback
Force trigger a vibration, duration = 120, Strength = 0.2
```
这个应该是触发一次震动，要和程序中的全局eventfilter结合使用，来实现点击震动反馈。
> 注：`duration` 的单位在 `hlc --help` 中未明确标注，当前先按毫秒处理，后续需在树莓派上确认实际单位。

## 5. 注意事项
- **非阻塞调用**: 所有的 `hlc` 调用应当快速返回。如果 `hlc` 存在阻塞风险，需要考虑将其放入后台线程（虽然 `QProcess` 自身是异步的，但 waitForFinished 会阻塞 GUI，应当使用信号槽处理回显，或者确认命令执行极快）。
- **电池轮询优化**: 复用单个 `QProcess` 实例并加防重入/超时控制，避免每秒新建进程导致系统负担；轮询周期建议 5~30 秒。
- **权限**: 确认 `hlc` 在树莓派上是否需要 `sudo` 权限运行。如果需要，代码中需相应处理。