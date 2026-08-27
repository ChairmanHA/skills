# Fix: DeviceInfoWidget 在跨 DPI 屏幕拖动时出现重影

## 问题描述
Frameless MainWindow 在高 DPI 大屏幕上启动后，拖动到低 DPI 笔记本屏幕，再拖回大屏幕时，
`m_deviceInfoWidget`（位于 QStatusBar 中）出现重影（双重渲染），但稍微改变窗口大小即可恢复。

## 根因分析

1. **`WM_DPICHANGED` 处理不完整**：当前仅处理了最大化窗口(`IsZoomed`)的 margins 调整，
   对非最大化窗口跨 DPI 屏幕拖动时无任何处理。

2. **Qt5 frameless 窗口的 backing store 缓存问题**：DPI 变化时，Qt 会内部重缩放控件几何，
   但 frameless 窗口（`WM_NCCALCSIZE` 返回 `WVR_REDRAW`）的 client area 不会被系统强制
   完全重绘。旧 DPI 下的 backing store 内容残留，叠加新 DPI 渲染 → 出现重影。

3. **`showEvent` 中的 1px move 只在首次显示时生效**：后续跨屏拖动不触发 `showEvent`，
   且 `move` 不改变窗口大小，不会强制 backing store 重分配。

## 解决方案

在 `WM_DPICHANGED` 消息处理中，对 **所有窗口状态**（不仅限于最大化）执行：

1. `SetWindowPos(SWP_FRAMECHANGED)` — 强制系统重新计算窗口框架
2. 对非最大化窗口，执行 resize +1/-1 技巧（等效于用户手动"改变一下窗口大小"），
   强制 Qt backing store 完全失效并重建
3. `RedrawWindow(RDW_INVALIDATE | RDW_FRAME | RDW_ALLCHILDREN | RDW_ERASE)` — 
   彻底清除残留渲染

使用 `QTimer::singleShot(0, ...)` 延迟执行，确保 Qt 先完成其自身的 DPI 适配处理后再触发。

## 修改文件
- `plugins/core/mainwindow.cpp` — `nativeEvent()` 中的 `WM_DPICHANGED` 分支

## DPI awareness 模式选择

本修复依赖于 **Per‑Monitor DPI Aware** 模式才能生效。在 `qt.conf` 中配置：

```ini
[Platforms]
WindowsArguments = dpiawareness=2
```

### 三种 DPI awareness 模式的对比

| 模式 | qt.conf 配置 | 行为 | 优点 | 缺点 | 是否触发 WM_DPICHANGED |
|------|------------|------|------|------|-------|
| **DPI Unaware** | `dpiawareness=0` | Windows 对整个窗口做位图缩放 | 跨屏稳定，不触发 DPI 变化逻辑 | 高 DPI 下模糊，字体不清晰 | ❌ 否 |
| **System DPI Aware** | `dpiawareness=1` | 应用在启动时读取主屏 DPI，之后固定；跨屏时 Windows 做位图缩放 | 主屏清晰 | 跨屏后模糊，仍可能出现重影 | ⚠️ 部分 |
| **Per‑Monitor DPI Aware** | `dpiawareness=2` | 应用在每次跨屏时收到 `WM_DPICHANGED` 信号，自己处理重绘 | 所有屏幕都清晰，完全由应用控制 | 需要完善的 DPI 变化处理逻辑 | ✅ 是 |

### 推荐配置

**保持 `dpiawareness=2`**，并确保 `main.cpp` 中已启用 Qt High‑DPI 缩放：

```cpp
QApplication::setAttribute(Qt::AA_EnableHighDpiScaling);    // 启用高 DPI 感知
QApplication::setAttribute(Qt::AA_UseHighDpiPixmaps);       // 启用高 DPI 像素图
```

这样可以确保：
- 所有屏幕上字体和控件都清晰
- 跨屏拖动时能正确触发 DPI 变化处理
- 本修复的 backing store 重建逻辑能正确执行

### 如果字体仍然模糊

检查以下项：
1. 是否设置了 `QT_AUTO_SCREEN_SCALE_FACTOR=0` 或其他禁用 DPI 自动缩放的环境变量
2. Windows 系统"兼容性缩放"是否对 exe 做了强制位图缩放（右键 exe → 属性 → 兼容性 → 禁用全屏优化）
3. Qt 的平台插件版本是否足够新以支持 per‑monitor DPI
