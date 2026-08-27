# 实时 IO Bandwidth 文本精度切换防抖

## Scope

- 仅调整 `DeviceInfoWidget` 中实时 IO throughput 标签的宽度与对齐方式。
- 保留现有 `Utils::normalize1K()` 格式化、刷新频率和状态栏字段顺序。
- 调整零速率显隐语义：设备连接期间保留 fixed-width 空标签占位，仅在断开设备时隐藏。
- 同时覆盖普通 GUI 与 PGA/API 共用的 `DeviceInfoWidget` 布局路径。

## Observation And Inference

- 观察：`Utils::normalize1K()` 会移除小数末尾的 `0`，因此 `140.09MB/s` 与
  `140.1MB/s` 的文本自然宽度不同。
- 观察：`m_bandwidth` 当前使用 `QSizePolicy::Preferred`，文本变化会改变标签的
  `sizeHint()`，其后的 UID、API、GUI 版本等字段会随状态栏布局重新分配而位移。
- 推断：实测在精度由两位变为一位时出现的整体抖动，来自实时刷新期间的标签自然
  宽度变化，而不是 throughput 数据链路本身。
- 追加现场证据：使用预留最小宽度后，速率变为约 `16MB/s` 时仍有概率抖动。
- 更新推断：仅设置最小宽度仍保留了布局根据动态 `sizeHint()` 分配额外空间的可能；
  throughput 标签需要使用固定宽度，彻底移除实时文本对布局宽度的影响。
- 追加现场证据：关闭 Streaming 时 throughput 必然回到 `0`；原逻辑随即隐藏标签，
  即使标签本身是 fixed width，后续 GUI/API 字段仍会因这段占位被释放而整体移动。
- 追加现场证据：`200.12MB/s` 显示时最左侧的 `2` 被裁切，说明当前 fixed width
  仍小于主题生效后的真实文本内容宽度。
- 观察：`DeviceInfoWidget` 在 `MainWindow` 构造期创建，而启动主题在插件初始化完成后
  才通过 `applyStartupTheme()` 应用；构造期按默认字体计算的 fixed width 会早于 QSS
  中的 18px HarmonyOS 字体生效。
- 更新设计：预留宽度必须按主题生效后的 `QLabel::sizeHint()` 计算，并在主题信号完成
  后重新锁定；候选数字使用当前字体中最宽的数字字符，同时保留额外安全留白。

## Success Criteria

1. `140.09MB/s`、`140.1MB/s` 等同量级文本切换时，throughput 标签占用宽度不变。
2. 标签预留宽度覆盖 `uint32_t throughputBps` 经 `normalize1K(..., 2)` 格式化后的
   最宽候选格式，避免正常范围内再次动态扩宽。
3. 数值右对齐，单位一侧保持稳定。
4. 不改变吞吐量数值格式或其他状态栏控件行为。
5. throughput 标签的最小宽度和最大宽度一致，不再参与动态横向伸缩。
6. 连接设备且速率为 `0` 时清空文本但保留标签占位；设备断开时才隐藏标签。
7. `200.12MB/s` 及 `uint32_t` 范围内的其他格式不裁切首尾字符。
8. 启动主题或主题字体变化后重新计算 fixed width，不沿用构造期默认字体尺寸。

## Verification

- Level: static.
- 确认 `deviceinfowidget.cpp` 仍由 `src/plugins/core/CMakeLists.txt` 编译。
- 检查修改文件 diff，并执行 `git diff --check`。
- 按仓库默认策略不编译、不运行。

## Verification Result

- 普通 GUI 与 PGA/API 两条布局路径共用构造函数中的预留宽度设置。
- 预留宽度按主题生效后标签的真实 `sizeHint()` 重新计算；小数候选由当前字体最宽
  数字生成，并覆盖 KB、MB、GB 格式及现场值 `200.12MB/s`。
- 在标签真实内容宽度之外增加至少一个空格的安全留白，避免 glyph 边界贴边裁切。
- throughput 标签改为右对齐；现有格式化与显隐逻辑未修改。
- 根据追加现场测试，将预留宽度从 minimum width 收紧为 fixed width，避免布局继续
  参考实时文本的动态 `sizeHint()`。
- 关闭 Streaming 后的 `0` 速率只清空文本，连接期间不再切换标签显隐；断开设备仍
  会清空并隐藏标签。
- `deviceinfowidget.cpp` 仍由 `src/plugins/core/CMakeLists.txt` 编译。
- `git diff --check` 通过；按静态验证约定未编译、未运行。
