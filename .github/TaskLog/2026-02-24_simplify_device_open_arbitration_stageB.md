# 简化 Stage B：单一 currentDevice + 禁止 UI/Scanner open

日期：2026-02-24

## 背景
Stage A 期间存在/曾存在两类问题：
1) open/close 可能在 UI 主线程执行，USB I/O 阻塞导致卡顿。
2) open/close 可能在 scanner 线程执行，带来线程安全/并发风险。

## 统一语义（简化）
- `currentDevice`：UI Connect 菜单 checked action 所标定的“当前选中目标设备”。
  - 是否已 open 与 current 语义无关；连接状态由 DeviceInfoWidget 显示。

## 核心约束（本轮聚焦）
- 任何可能耗时的设备 `open()/close()` **不允许**在：
  - Scanner 线程
  - UI 主线程

## 落地方案（尽量简）
- 复用 Core 的 `DeviceStatusUpdateThread` 作为“唯一设备 I/O 线程”。
  - status poll（`updateRealTimeStatus`）与 lifecycle（open/close/switch）都在同一线程串行执行，避免多线程同时触碰底层驱动。
- `DeviceManager::setCurrentDevice(IDevice*)` 只负责：
  1) 立即更新 `currentDevice`（供 Connect 菜单勾选、UI 上下文切换）。
  2) 停止 status timer（避免切换期间并发 I/O）。
  3) 投递一个 switch 任务到 I/O worker 线程执行 close/open。
  4) open 完成后在主线程发 `currentDeviceOpenStateChanged(opened)`，并按需启动 status updates。
- Scanner 只负责发现与上报（`devicesDiscovered`），严禁调用 `open()`。
  - 现有 HTRA scanner 中的 auto-retry open 代码保持注释，并计划从接口层移除 `deviceAutoOpened` 这一路。

## Anti-stale（最小）
- 引入 `requestId`：每次 setCurrentDevice / retry 递增。
- I/O worker 回传结果携带 requestId；主线程丢弃过期结果，避免切换后旧结果覆盖新状态。
