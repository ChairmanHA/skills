# USB-only 平台隐藏 Device 菜单

## Scope

- 复用 HTRA 插件构造阶段已经确定的 `Plugin::usbPortOnly()` 状态。
- 在插件 `extensionsInitialized()` 阶段、Core 已完成菜单注册后，将 MainWindow 菜单栏中的 `Device` 菜单隐藏。
- 非 USB-only 环境保持现有菜单可见行为。

## Evidence and assumptions

- 观察：`m_usbPortOnly` 由 `Plugin::checkPowerSupply()` 设置，当前仅在 ARM Linux 的 HLC 电源路由构建上可能为 `true`。
- 观察：`Device` 菜单由 `MainWindow::registerDefaultContainers()` 创建并注册为 `Core::Constants::MENU_DEVICE`。
- 观察：HTRA 已依赖 Core；Core 不依赖 HTRA。
- 推断：由 HTRA 在自身 `extensionsInitialized()` 中把已知状态传给 MainWindow，可以保持既有插件依赖方向，并满足“插件加载好之后”再更新菜单的时序要求。

## Success criteria

- `usbPortOnly() == true` 时，顶层 `Device` 菜单 action 不可见。
- `usbPortOnly() == false` 时，顶层 `Device` 菜单保持可见。
- Core 不新增对 HTRA 的依赖，现有 Device 菜单及其 action 注册逻辑不变。

## Verification level

- `static`

## Verification checklist

- [x] HTRA 的 CMake 目标仍只沿现有方向链接 Core。
- [x] 菜单隐藏发生在 HTRA `extensionsInitialized()`，且仅在 `m_usbPortOnly == true` 时执行。
- [x] MainWindow 通过 `QMenu::menuAction()` 控制顶层菜单可见性。
- [x] `git diff --check` 通过；未修改无关文件。

## Verification result

- 静态检查通过。
- 按仓库默认规则未执行编译或运行验证。
