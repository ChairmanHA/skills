# 2026-04-13 ETH Dialog Local Interface Container QSS Fix

## Goal

- 修复 EthConnectDialog 中本地接口展示区域看起来不是透明背景的问题。
- 保持 QLabel 本身的透明样式不变，同时让多网卡场景下的 QComboBox 本体和下拉项也不再被全局 QWidget 背景色污染。
- 去掉本地下拉项的选中/悬停高亮，避免 UI 误导用户认为当前选中网卡会直接决定最终连接路径。
- 去掉收起态 QComboBox 当前项的选中高亮，但保留 popup 列表中的 hover 反馈。
- 修复 popup 列表 hover 样式实际未命中的问题。
- 将 ETH 对话框的原生 `QComboBox` 替换为项目现有 `Controls::ComboBox`，尽量与 GNSS 对话框保持一致。
- 同步修复深色主题与浅色主题，避免主题切换后行为不一致。

## Reasoning

- 当前 `m_localInterfaceValueLabel` 的透明样式已经命中，但它的直接父对象 `localInterfaceContainer` 是普通 `QWidget`，会继承全局 `QWidget` 背景色。
- 当前多网卡场景下显示的是普通 `QComboBox`，它没有命中现有 `Controls--ComboBox` 规则，所以本体和弹出的 item view 也会继承全局 `QWidget` 背景色。
- GNSS 对话框已经稳定使用 `Controls::ComboBox`，它会在构造时替换默认 popup view，并直接命中现有 `Controls--ComboBox` 全局规则。
- 因此仅把容器设为透明还不够，需要在 EthConnectDialog 的局部 QSS 中继续覆盖 `QComboBox` 与其下拉视图。
- Qt 原生 `QComboBox` 的 popup item 在 `selected` 状态下仍会走系统/样式默认高亮色，所以还需要显式覆盖 `item:selected` 与 `item:hover`。
- Qt 原生 `QComboBox` 收起后当前文本区域也会在焦点/选中态使用系统 highlight，需要在控件本体上覆盖 `selection-background-color`。
- popup 列表仍应保留 hover 反馈，否则下拉交互会显得发木；真正需要去掉的是 popup 收起后的当前项高亮，以及 popup 中的 selected 常驻高亮。
- 仅靠 `QDialog#EthConnectDialog QComboBox QAbstractItemView::item:hover` 这类祖先链选择器并不稳，因为 QComboBox 的 popup 是内部单独 view/container，状态命中和对象层级都比普通子控件更脆弱。
- 默认 popup view 的 hover 反馈还依赖 mouse tracking；不显式打开时，`::item:hover` 可能根本不会进入稳定命中状态。
- 继续给 ETH 对话框堆原生 `QComboBox` 局部补丁，只会把问题留在主题层；改回统一控件体系比继续微调 QSS 更稳。
- 翻译调用位于匿名命名空间的自由函数里，不能直接写裸 `tr(...)`；当前 `QCoreApplication::translate("EthConnectDialog", ...)` 在本项目翻译框架下可以正常工作。

## Plan

- 保留 `localInterfaceContainer` 的透明背景规则。
- 在 `configuration/theme.css` 为 `QDialog#EthConnectDialog QComboBox`、下拉视图和 item 增加透明背景覆盖。
- 在 `configuration/theme_light.css` 添加同样的规则。
- 为 `item:selected` 和 `item:hover` 添加透明覆盖，去掉蓝色高亮。
- 为 `QComboBox` 本体添加透明 `selection-background-color`，去掉收起态当前项高亮。
- 将 popup `item:hover` 恢复为主题 hover 色，仅保留 `item:selected` 为透明。
- 在代码里为 `m_localInterfaceComboBox` 和其实际 popup view 设置专用 `objectName`，并开启 popup view 的 mouse tracking。
- QSS 改为直接命中 `#localInterfaceComboBox` 与 `#localInterfaceComboPopup`，避免继续依赖脆弱的内部层级选择器。
- 将 `m_localInterfaceComboBox` 直接替换为 `Controls::ComboBox`。
- 删除这批只服务于原生 `QComboBox` 的局部 objectName / popup 规则，改为像 GNSS 对话框那样只做最小的对话框级 ComboBox 字体覆盖。
- 对变更文件做静态检查，不额外改动其它样式。

## Validation

- 对两份 QSS 做静态检查，确认选择器只作用于 EthConnectDialog。
- 对两份 QSS 做静态检查，确认“收起态无高亮、popup hover 保留、popup selected 不常驻高亮”的选择器组合完整。
- 对代码做静态检查，确认 popup view 的 objectName 和 mouse tracking 已在创建时设置。
- 对代码做静态检查，确认 ETH 对话框已经切换到 `Controls::ComboBox`，不再依赖原生 popup 补丁。
- 确认翻译结论基于 `main.cpp` 的自定义 `Utils::Translator` 与其 `translate()` 回退逻辑，而不是主观判断。
- 不额外运行程序；本次改动仅做最小修复。