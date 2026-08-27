# Legacy MiniBar Popup Host 历史结论

> 2026-07-21：本文只保留已删除 in-process `MiniBarWindow` 的可复用结论。
> 当前 helper 实现见
> [Helper Layer-Shell 当前边界](minibar_helper_layershell_parity_gaps.md)，
> 当前排障入口见
> [Wayland / LayerShellQt Debug 指南](minibar_wayland_layershell_debug_guide.md)。

legacy popup host 的逐次尝试、临时 workaround 和已删除源码细节已移除，避免与
当前 helper 路径混淆。

## 可复用结论

### Host 与视觉

- Hosted business/Sweep panel 采用 normal-mode 内容、host-local QSS 和标题区；
  不把 compact mode 扩散回共享 panel。
- provider list 保留 `QMenu`/action 模型。更换 `QMenu`、`QDialog` 或自绘 widget
  不能单独解决 Wayland 问题；只要对象仍是 top-level，就必须处理其 surface
  role。
- `WA_QuitOnClose=false` 是 helper-owned popup 的必要生命周期约束。

### Wayland overlay

- 进程级 layer-shell integration 会接管所有 top-level `QWindow`，不是只接管
  minibar base。
- Hosted panel 需要 fullscreen transparent overlay host 才能可靠捕获桌面级
  outside-click；panel 内容作为 overlay child 接收内部输入。
- top-level menu/enum popup 必须在首次 mapping 前配置 output、layer、anchors、
  size 与 margins。
- Wayland 几何以 host 已提交的 visual top-left/size 为事实源；不能混用
  `frameGeometry()` 与不同 surface 的 global mouse position 做唯一 hit test。

### Ownership 与关闭顺序

- owned 判断需要覆盖 QObject parent chain、top-level widget 和 native
  `windowHandle()`。
- `ApplicationDeactivate` 不能独立证明发生了外点；内部 surface 切换也可能产生
  deactivate/focus 变化。
- 关闭从最内层开始：keyboard/enum/menu -> hosted panel -> overlay/base。
- 如果 compositor 已把外点送给另一个客户端，本进程的 application event filter
  无法补抓这次点击，必须由覆盖目标区域的 owned surface 承担 outside catcher。

## 已否定方向

- 依靠更长 suppression timer、`singleShot(0)` 或强制 `activateWindow()` 修正
  surface ownership。
- 仅记录 transient visual rect 并替换全部 hit test。
- 把 Wayland overlay 逻辑无条件扩展到 Win32。
- 仅更换顶层 widget 类型而不改变/配置 native surface role。

这些结论继续约束当前 helper，但 legacy 类型名、旧 popup 复用策略和逐次现场日志
不再作为当前实现说明。
