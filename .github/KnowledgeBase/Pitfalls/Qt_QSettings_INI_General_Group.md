# Qt QSettings INI 的 `[General]` 组名陷阱

## 现象
在 INI 文件中存在以下配置：

```ini
[General]
Theme=Dark
Brightness=0.8
```

但在 Qt 代码中：
- `QSettings::childGroups()` 不会列出 `General`
- `settings.value("General/Theme")` 返回 `Invalid`
- `settings.value("Theme")` 才能读到 `Dark`

## 根因
这是 Qt 的历史兼容行为：当使用 INI 格式时，`[General]` 被当作“默认段/根段”。
也就是说，`[General]` 中的键会被当作根键存储/读取，而不是 `General/<key>`。

## 影响
- 若代码严格按 `General/<key>` 读取，会导致配置“看起来丢失”。
- 调试时容易被 `childGroups()` 的输出误导（误以为 INI 段消失）。
- 写入时若混用两种约定，可能导致配置源不一致、迁移困难。

## 建议（两种可选方案）

### 方案 A：保留 `[General]`，按“根键”读取
当你决定继续使用 `[General]` 时，建议**明确约定：一律使用根键读取**。

- 读取：`settings.value("Theme")`、`settings.value("Brightness")`
- 避免：`settings.value("General/Theme")`（会得到 `Invalid`）

优点：兼容 Qt 的默认行为、对旧文件零迁移。

### 方案 B：避免使用 `[General]`，改为明确段名（推荐用于新功能）
将 `[General]` 改为更明确的段名，例如 `[Application]`，使键路径更直观（`Application/Theme`）。

优点：`childGroups()` 结果更符合直觉、减少新人误解、键空间更清晰。

## 相关
- Qt 类：`QSettings`
- 场景：`QSettings::IniFormat` + INI 文件段名为 `[General]`
