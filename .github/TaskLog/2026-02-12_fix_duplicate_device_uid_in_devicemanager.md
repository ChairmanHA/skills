# 修复：设备列表出现重复 UID（Connect 菜单显示 4 台但实际 2 台）

日期：2026-02-12

## 现象
- HTRA 扫描器 `device_list_usb()` 实际只枚举到 2 台设备。
- 前端 Connect 设备下拉菜单出现 4 条记录，其中后 3 条 UID 完全相同（截图所示）。

## 已排除
- UI 层菜单累加：`MainWindow::updateConnectMenu()` 每次 `aboutToShow` 都会 `m_connectMenu->clear()`，因此重复项来源于 `DeviceManager::allDevices()` 返回的设备列表本身。

## 根因分析
根因在 `DeviceManagerPrivate::onDevicesDiscovered()` 的 reconcile 逻辑只做了“发现列表的 UID 去重”，但 **对已注册列表 `registeredDevice` 没有做 UID 唯一性约束**。

具体触发路径（高概率）：
1. `HTRADeviceScanner` 周期性扫描时会 `new FancyDevice()` 产出列表。
2. `devicesDiscovered` → `DeviceManagerPrivate::onDevicesDiscovered` 使用 `Qt::QueuedConnection` 投递。
3. 若上层处理滞后/堆积，可能在第一次发现尚未完成注册前，又来了第二次发现，导致同一 UID 的新对象再次被当作“未注册指针”加入 `registeredDevice`。
4. 之后扫描器会尝试复用 `allDevices()` 建 UID 映射，但因为列表里已存在重复 UID，对外展示就会出现重复项（且常表现为同一个 UID 多次）。

当前代码的关键问题：
- `registerDevice()` 仅按“指针是否已存在”去重。
- `onDevicesDiscovered()` 的 `devicesToRemove` 逻辑只移除“UID 不在快照中”的设备，不会移除“UID 在快照中但重复注册”的设备。

## 修复目标
- `DeviceManager::allDevices()` 内部集合对外表现为“同一 `DeviceUID` 至多一台设备”。
- 即使扫描器短时间内多次发现并创建同 UID 的新对象，也不会导致重复注册。

## 修复方案（最小改动，集中在 Manager）
在 `DeviceManagerPrivate::onDevicesDiscovered()` 中：
1. 在持锁遍历 `registeredDevice` 时建立 `registeredByUid` 映射：
   - 若发现同一 UID 的多个已注册对象：保留 `currentDevice`（如其 UID 匹配），否则保留第一个；其余加入 `devicesToRemove`（作为重复项清理）。
2. 处理发现列表：
   - 若发现的 UID 已存在于 `registeredByUid`（已注册）：删除新发现的重复对象（避免再注册）。
   - 若不存在：加入 `devicesToAdd`。
3. 继续保留“快照缺失则移除”的语义。

## 风险与边界
- `unregisterDevice()` 会 `delete device`：因此清理重复注册时必须确保不误删 `currentDevice`。
- 不在此变更中引入多设备并发 open 逻辑；仅保证列表一致性。

## 验证方式
- 插入 2 台设备时，Connect 菜单应稳定显示 2 条，不随时间增长。
- 重复打开 Connect 菜单多次不应出现条目增长（因为来源列表已唯一）。
- 若刻意制造扫描信号堆积，也应保持 UID 唯一。
