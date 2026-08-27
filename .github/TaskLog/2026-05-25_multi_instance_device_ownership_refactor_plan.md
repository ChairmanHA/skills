# 2026-05-25 Multi-instance Device Ownership Refactor Plan

## 目标

- 第一实例始终允许启动（第一次双击打开软件），即使当前没有连接任何设备。
- 全机任意时刻只允许存在一个“未持有任何设备”的空闲实例。
- 只有当已有实例均已经实际持有设备会话时，新的实例才允许继续启动。
- 跨实例自动防干扰只针对 USB 做协调；ETH 仍是手工输入 endpoint 的连接链路。
- 当允许启动新实例时，只能自动附着空闲 USB；若不存在空闲 USB，则直接进入一个不持有任何设备的空闲实例。
- 对手工 ETH 输入同一 IP/Port 导致的冲突不做软件级仲裁，风险由用户自行承担。

## 设计思路

- 先做实例准入，再做设备选择。是否允许新实例启动，和新实例最终连接哪台设备，是两个独立判断。
- 用轻量跨进程状态表作为唯一事实源，统一描述“哪些实例仍是空闲实例”以及“哪些 USB 仍被某实例持有”。
- 跨实例自动协调只覆盖 USB，因为当前系统只能稳定 discovery USB，不能自动 discovery ETH。
- 当前架构仍是 currentDevice-centric：单实例任一时刻只保留一个实际交互控制通路，多台设备的并行控制依赖多开软件实例，而不是单实例内部保留多个 device session。
- 自动附着只发生在“当前实例没有 currentDevice”这一前提下，且目标必须来自 free USB 集合，而不是来自“本地刚好扫描到了哪台设备”。

## 当前语义

### 实例状态

- Idle：当前实例未持有任何设备。
- HoldingUsb：当前实例仍持有至少一台 USB 设备。
- HoldingEth：当前实例当前通过 ETH 持有设备会话，且没有 USB 持有集合时也视为已消费实例名额。

### USB 持有语义

- 共享状态表为每个实例维护一个 ownedUsbKeys 集合。
- free USB 的判定方式是 discoveredUsbKeys 减去 otherSessions.ownedUsbKeys。
- 只要某台 USB 仍在当前实例的持有集合里，其他实例就不应自动选中它。

### 单实例控制语义

- 单实例切换设备时，只允许控制切换后的那一台 currentDevice；切走的旧设备必须立即 release，不再继续占有控制权。
- USB 与 ETH 在 switch 路径上统一采用“release old session -> open new session”的语义，不再保留“USB park / ETH release”的链路差异。
- 如果用户需要同时控制多台设备，依赖多开软件实例实现；每个实例各自持有并控制自己的设备会话。

### 启动与运行期附着语义

1. 新实例启动时先注册当前 session，并以 Idle 进入状态表。
2. 若系统里已经存在另一个 Idle 实例，则当前新实例直接拒绝继续启动。
3. 若不存在其他 Idle 实例，则继续等待 USB 扫描稳定并计算 free USB。
4. 启动期与运行期断联后的 no-current 状态都只允许自动附着 free USB，不允许回抢 otherOwnedUsbKeys 中的设备。
5. 若不存在 free USB，则实例保持 no-current Idle，不再回退到启动期 ETH gate，也不在运行期 fallback 中强行抢设备。
6. 手工 ETH 连接保留为主窗口内的普通连接入口，不承担自动门禁或自动仲裁职责。

## 实现方式

- 用 InstanceStateRegistry 维护 sessionState 与 ownedUsbKeys，并在读写时清理 stale session；这份表是跨实例 owner 判定的唯一事实源。
- 启动期由 MainWindow 基于 otherOwnedUsbKeys 计算 free USB 集合；只有 free USB 才能成为 startup target。
- 运行期由 DeviceManager 统一维护 ownership：USB open、close、switch、unregister、disconnect 都会同步回写 ownedUsbKeys，并根据 currentDevice 与 ownedUsbKeys 回写实例状态。
- DeviceManager 的 no-current auto-select 路径统一受 registry 过滤，包括 registerDevice() 的即时 auto-select、setStartupAutoSelectSuppressed() 的 fallback，以及 onDevicesDiscovered() 的运行期 fallback。
- 设备切换和 open/close 仍通过 DeviceIoWorker 串行执行，避免同一进程内并发调用底层 SDK。
- Connect 菜单展示所有扫描到的 USB，但会把 otherOwnedUsbKeys 命中的设备置为 disabled，避免用户从 UI 层显式回抢 busy USB。

## 测试用例

| 编号 | 场景 | 预期 |
| --- | --- | --- |
| 1 | 首次启动，机器上无任何设备 | 程序正常启动，并以唯一 Idle 实例进入主窗口 |
| 2 | 已有一个 Idle 实例，再启动第二个实例 | 第二个实例在启动早期被直接拦截，不进入主流程 |
| 3 | 实例 A 已持有唯一一台 USB，再启动实例 B | 实例 B 允许启动，但不自动连接设备，而是以 no-current Idle 实例进入主窗口 |
| 4 | 实例 A 仅持有 ETH，机器上仍有 1 台 free USB，再启动实例 B | 实例 B 自动连接该 free USB，不因“已有实例存在”而被错误阻止 |
| 5 | 机器上有 2 台 USB，实例 A 已持有其中 1 台，再启动实例 B | 实例 B 只能自动连接剩余那台 free USB，不能回退去抢实例 A 的设备 |
| 6 | 当前没有 free USB，但系统里也没有其他 Idle 实例 | 新实例直接进入 no-current Idle 状态|
| 7 | Idle 实例进入主窗口后手工连接 ETH | 连接成功后，实例状态从 Idle 变为 HoldingEth，并继续正常进入工作流 |
| 8 | 实例 A 从 USB A 切换到 USB B | USB A 的 ownership 应被释放；若系统中不存在其他 Idle 实例，则新实例可以重新连接 USB A |
| 9 | 某实例异常退出后重新启动新实例 | stale session 会被回收，新的实例可重新成为唯一 Idle 实例或重新占用释放出的 USB |
| 10 | 新实例启动过程中存在其他实例已持有 USB | 新实例不会对其他实例已持有 USB 执行 setCurrentDevice(first)、open 或 reconfigure |
| 11 | 机器上原有 2 台 USB，实例 A 与实例 B 分别持有一台；实例 A 所持 USB 运行期断联 | 实例 A 进入 no-current Idle 后不得自动回抢实例 B 仍持有的 USB；若系统中不存在其他 free USB，则 A 保持 no-current |

## 最重要的下一步收口

- 把 startup 与 runtime 的 auto-attach 目标选择合并成唯一一条 registry-aware chooser 路径。
- 当前实现虽然两条路径都已经受 otherOwnedUsbKeys 过滤，但 startup 仍通过 MainWindow::availableStartupUsbDevices() 先生成并按 UID 稳定排序 free USB；runtime 则通过 DeviceManager::firstAutoSelectableScannerDevice() 直接按 registeredDevice 当前顺序选第一台 free USB。两条路径的过滤前提已经一致，目标选择顺序却仍然分叉。
- 下一步应把“从 free USB 集合中挑出唯一 auto-attach target”的决策下沉成单一 helper，让 startup 与 runtime 共用同一份排序/优先级策略；今天可以继续保持“按稳定 UID 顺序选第一台”，以后要改 chooser 或 last-used，也只改这一处。

## 仍可能出现 Bug 的情况

- 当系统里同时存在多台 free USB 时，startup 与 runtime 可能因为选择顺序不一致而附着到不同设备；表现为同一套物理环境下，冷启动和运行期断联恢复选中的设备不一致。
- 当 USB 重新枚举顺序发生变化时，runtime 依赖 registeredDevice 当前顺序，可能在不同轮次附着到不同 free USB，表现为设备选择结果不稳定。
- 如果后续再改 auto-attach 策略时只修改 startup 或只修改 runtime，其中一条路径就可能再次与另一条路径漂移，重新引入“过滤一致、选择不一致”或者“某一路径忘记查 registry”的回归。
- 多实例仍共享 Settings.ini、Profile.json、DeviceHistory.json 等写路径；即使 ownership 逻辑正确，多个实例仍可能出现配置互写、最近设备记录互相覆盖等非设备 owner 层面的 bug。

## 2026-05-27 Follow-up：单实例 USB1 拔下后切到 USB2

- 现象目标：保持当前“多实例下同一时间只能有一个实例去自动连接 USB”的行为不变，同时保证“只有一个实例时，USB1 断联后再插入 USB2，当前实例仍能自动附着到 USB2”。
- 本地假设：运行期 auto-select 目前只有“挑第一台 free scanner USB”的逻辑，没有表达“本实例对刚断联的 USB 只有优先权，不是绝对锁”。因此一旦后续补齐 reconnect reservation，如果没有“无其他 attach contender 时允许降级到别的 free USB”的分支，就会把单实例 USB1 -> USB2 场景也一起卡死。
- 收口方式：只在 DeviceManager 内维护一个进程内的 preferredReconnectUsbKey。运行期 chooser 先尝试命中当前实例偏好的 free USB；只有偏好 USB 当前不存在时，才在 `InstanceStateRegistry::hasOtherAttachContenderSession()` 为 false 的前提下退化到其他 free USB。
- 不动边界：不修改 startup gate；不改变 otherOwnedUsbKeys 的过滤语义；不允许在存在其他 Idle/ReconnectingUsb 实例时回退去抢别的 free USB，从而保持现有“不 race connect”的运行期表现。
- 便宜校验：单实例时 `hasOtherAttachContenderSession()` 为 false，USB1 断联后插入 USB2 应能选到 USB2；存在另一个 Idle 实例时该判定为 true，断联实例只能优先等自己的保留 USB，不会退化去抢别的 free USB。
