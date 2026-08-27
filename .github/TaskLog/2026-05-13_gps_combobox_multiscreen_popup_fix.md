# GPS ComboBox 多屏 popup 重影修复

## 问题现象

打开 GPS 对话框后，在主屏点击 `antenna` 下拉，popup 正常；在副屏点击同一个下拉，副屏 popup 仍会正常展开，但主屏对应位置还会再出现一个相同 popup，形成双 popup / 重影。

## 本地根因假设

`Controls::ComboBox::showPopup()` 先调用 `QComboBox::showPopup()` 让 Qt 创建并显示 popup，然后又用 `popupWidgetTopLeftConer()` 重新计算 popup 几何。

当前 `popupWidgetTopLeftConer()` 固定使用 `QGuiApplication::primaryScreen()->availableGeometry()` 作为边界裁剪依据。这在单屏成立，但在副屏打开 popup 时，Qt 已经基于当前屏幕放好了 popup；随后我们的二次几何修正又把同一个 popup 容器按“主屏坐标系”裁剪，导致多屏场景下出现额外的错误位置窗口表现。

## 最小判别检查

1. 检查 `ComboBox::popupWidgetTopLeftConer()` 是否只依赖 `primaryScreen()`。
2. 对照仓库里已有多屏代码，确认推荐模式是 `screenAt(globalPoint)`，而不是固定主屏。

## 最小修改策略

1. 给 popup 边界计算引入“当前屏幕”选择：优先取 popup 顶点所在屏幕，再退回 ComboBox 所在窗口屏幕，最后才退回主屏。
2. 保持现有 `QComboBox::showPopup()` 和现有宽高计算逻辑不变，只修正屏幕选取与裁剪基准，避免扩大行为面。
3. 如果 popup 当前几何已经有效，直接用它的 top-left 作为判定参考点，避免副屏 / 负坐标 / 非主屏布局被误裁到主屏。

## 验证口径

1. 对 `controls/combobox.cpp` 做一次定向 Debug 编译，确认无新编译错误。
2. 静态确认 popup 几何裁剪不再绑定主屏，而是绑定 popup 当前所在屏幕。
3. 向用户明确解释：双 popup 的根因是“Qt 已显示一次 + 自定义二次重定位使用了错误屏幕基准”，不是 GPS 业务层重复创建 popup。