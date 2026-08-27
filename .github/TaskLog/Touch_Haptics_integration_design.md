# 触屏震动反馈（Haptics）整合设计建议

**日期**: 2026-01-22
**状态**: done

## 目标
- 仅在“真实触摸输入”发生时触发一次震动反馈（典型：Tap/Press）。
- 与鼠标输入严格区分；避免触摸导致的“合成鼠标事件”产生重复震动。
- 提供统一的开关（可持久化），并在不支持平台上无害降级（no-op）。
- 逻辑放在应用层（qApp 级别）或输入反馈专用模块，不散落到各个控件。

## 关键原则（工程实践）
1. **单一入口**：所有触摸反馈从一个地方触发（如 `InputFeedbackController`/`HapticFeedbackManager`），UI 组件不直接调 `HardwareController`。
2. **事件过滤优于侵入控件**：通过 `qApp->installEventFilter(...)` 捕获全局输入事件；避免修改大量控件或重复接线。
3. **只对 Touch 触发**：优先只监听 `QEvent::TouchBegin`（或 tap gesture），而不是监听 `MouseButtonPress`。
4. **防重入 + 节流**：增加最小触发间隔（例如 80~150ms），避免拖动/多点触控/抖动导致频繁触发。
5. **开关与能力分离**：
   - `HardwareController::isHapticFeedbackAvailable()` 表示平台/硬件能力。
   - `HardwareController::isHapticFeedbackEnabled()` 表示用户开关（`QSettings` 持久化）。
   - 触发条件应同时满足 `available && enabled`。

## 推荐架构

### 1) HardwareController
职责：
- 封装调用 `hlc --trigger-feedback`（Linux）或 no-op（Windows）。
- 保存/发出 `m_hapticFeedbackEnabled` 状态变更信号，建议持久化到 `QSettings`。
- 可选：在 `triggerHapticFeedback()` 内部再做一次节流（双保险），防止被误调用频繁触发。

### 2) 新增：InputFeedbackController
职责：
- 安装为 `QObject` 并在 `main.cpp` 中创建（QApplication 初始化后）。
- `installEventFilter(qApp)`，统一处理 `QEvent`。
- 仅对触摸事件触发一次震动：
  - 推荐：`QEvent::TouchBegin` 时触发。
  - 可选增强：识别 Tap（TouchBegin + TouchEnd 位移小于阈值）再触发，避免滚动/拖动触发。

为何不把事件过滤直接塞进 HardwareController：
- HardwareController 是“硬件层控制”，输入语义属于“交互反馈层”；分层更清晰，方便未来对按键音/提示音等扩展。

## 如何区分触摸 vs 鼠标（避免双触发）
Qt 在触屏平台上常见行为：触摸可能伴随“合成鼠标事件”。

推荐策略（从稳到强）：

### A. 只监听 Touch 事件（首选）
- EventFilter 只处理 `QEvent::TouchBegin`/`TouchEnd`。
- 完全不处理 `MouseButtonPress`，从源头避免重复。
- 前提：触摸事件能到达应用层（通常可以，尤其是在启用了触摸的窗口/控件上）。

### B. 若必须兼容某些设备只发鼠标事件
- 仍以 Touch 为主；对 `MouseButtonPress` 增加过滤：
  - 通过 `QMouseEvent::source()` 过滤合成鼠标（`Qt::MouseEventNotSynthesized` 才认为是真鼠标）。
  - 或：维护 `lastTouchTimestamp`，若鼠标事件发生在 Touch 后短窗口内（例如 300ms），视为合成事件并忽略。

不建议全局关闭 `Qt::AA_SynthesizeMouseForUnhandledTouchEvents`：
- 可能破坏现有控件对“鼠标输入”的依赖，带来未知 UI 行为差异。

## 触发时机建议
- **默认**：在 `TouchBegin` 立即触发（反馈“按下”最直觉）。
- **更精确（可选）**：仅在判定为 Tap 时触发（TouchEnd 且位移小）。
- 不建议对 `TouchUpdate`（移动/滑动）触发。

## 震动开关（Enable）建议
- 由 `HardwareController` 维护开关状态，并提供信号 `hapticFeedbackEnabledChanged(bool)`。
- `InputFeedbackController` 不持有开关，只在触发前查询 `HardwareController`。
- 开关持久化：`QSettings`（key 例如 `ui/hapticsEnabled`）。
- UI（SystemSettingsDialog）只负责改开关，不直接触发硬件命令。

## 运行时节流（Rate limit）建议
- 在 `InputFeedbackController` 内使用 `QElapsedTimer`：
  - `if (!timer.isValid() || timer.elapsed() > 120) trigger();`
- 若担心多点触控：同一帧多个 TouchPoint 只触发一次。

## 兼容性
- Windows：`HardwareController::isHapticFeedbackAvailable()` 返回 false 或 trigger 为 no-op。
- Linux：若 `hlc` 不存在/失败，应快速失败（no-op + 日志），不影响 UI。

## 测试/验收建议
- 触摸一次 -> 震动一次（不因合成鼠标重复震动）。
- 拖动/滚动列表不应持续震动（除非明确设计需要）。
- 关闭开关后，触摸无震动。
- Windows 下无报错、无阻塞。
