# USB UID runtime profile switching

日期：2026-08-17  
状态：已完成  
验证级别：`static`

## Scope

将现有 `DeviceRuntimeProfileCoordinator` 从“仅手工 ETH”扩展为“按设备 UID 保存源 profile，并在显式切换成功后按目标 UID 恢复 profile”。设备列表中的 USB 与 ETH 统一经过该 coordinator；ETH Connect 和标题栏 A/B 继续复用同一入口。

本次只统一 profile 切换编排，不统一设备生命周期。`DeviceManager` 中双 ETH 常驻、USB close/reopen、scanner reconciliation、ownership、retry 和 unregister 语义保持不变。标题栏 A/B 仍只展示两台已打开的手工 ETH；启动和扫描器自动选择仍由 `DeviceManager` 直接处理。

## Evidence and assumptions

- runtime profile 的 `common`、`bussiness`、active business 和当前页面内容不依赖传输类型；`deviceContext` 只用于设备身份校验。
- USB 设备在 scanner 枚举后、打开前已经具有 `DeviceUID32 + DeviceUID`；新建手工 ETH 需要首次打开成功后才获得 UID，因此入口必须继续允许“UID 暂不可用的手工 ETH”。
- 当前 USB 可能在 coordinator 创建前已被启动流程自动选中。第一次显式切换时，若当前设备已打开且 UID 有效，应先把它绑定为源设备并保存，避免目标继承源 UI 工作副本。
- USB 切走时由 `DeviceManager` 关闭，但正常情况下设备对象仍在 registry 中，可以在目标打开失败时重新选择并打开；若源设备已被 scanner 注销，则不尝试回滚。
- profile 文件继续使用 `h2_<uidHigh>_<uidLow>.json`。同一物理设备的 profile 按 UID 识别，不因 USB/ETH 传输方式拆分。
- `configuration/device_profiles` 是会话内切换缓存，应用退出后仍按现有逻辑清理，不作为跨会话持久配置。

## Success criteria

- 从已打开 USB 切到另一台 USB/ETH 前保存源 UID profile；目标打开成功后恢复目标 UID profile，目标无缓存时恢复默认状态。
- 从设备列表选择 USB 与 ETH 使用同一 coordinator 入口，不再按 `ethEndpoint()` 分支。
- 首次显式切换前已自动打开的 USB 被正确保存为源 profile。
- 目标打开失败时，仅当源设备仍在 registry 中才回滚；USB 源允许通过现有 `DeviceManager` 流程重新打开。
- 手工 ETH 在首次打开前 UID 为空的既有连接流程仍可启动，双 ETH `keepOldOpen` 与标题栏 A/B 行为不变。
- 错误信息和 profile metadata 不再声明目标必须是 ETH；metadata 记录实际 transport，并按 UID 校验。
- 通过 `git diff --check` 和静态引用检查；按仓库默认规则不编译、不运行。

## Implementation steps

1. 将 coordinator 的目标资格判断改为“有效 UID，或待首次打开的手工 ETH”。
2. 从 `DeviceInfo::PhysicalInterface` 生成 transport context，ETH endpoint 仅作为可选 metadata。
3. 第一次显式切换时绑定并保存当前已打开、UID 有效的源设备。
4. 目标失败时根据 registry 存活性回滚，支持重新打开 USB 源。
5. 将设备列表选择统一路由到 coordinator，完成静态检查。

## Result

- `DeviceRuntimeProfileCoordinator` 已按 UID 接受 USB 与手工 ETH，并记录实际 `PhysicalInterface` transport；profile 文件名与匹配规则继续只使用 UID。
- 第一次显式切换前，当前已打开且 UID 有效的设备会先保存为源 profile；独立的会话绑定标志避免 USB 注销后误判为首次绑定。
- USB/ETH 目标打开失败时，延迟回滚按源 UUID 重新查询 registry；源已注销时直接结束，不访问失效对象。
- `MainWindowDeviceController::selectDevice()` 不再按 `ethEndpoint()` 分流，设备列表和标题栏 A/B 均通过同一 coordinator；ETH Connect 保持既有入口。
- `DeviceManager` 及 USB/ETH 生命周期代码未修改，启动/扫描器自动选择仍保持原行为。
- `git diff --check` 通过；静态确认旧 ETH 专用错误文本已移除，修改文件仍包含在 `src/plugins/core/CMakeLists.txt`。按验证级别未编译、未运行。
