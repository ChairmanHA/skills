# SMA100B 风格的 Instrument Home 与配置导航

日期：2026-10-12
状态：已按确认方案接入；Sweep 88×88 px 方形使能按钮位于完整扫描卡片内部；Home 的 System 摘要为四行两列，进入 System 后为六类入口、分类页面与 Tab。最新分类重构仅静态检查，未编译或运行；此前 Core 插件 Debug 构建属于历史验证。128 px 顶行、Waveform、左 System/右 Sweep 及右侧 Back 保持现有布局，尚未真机验收。
适用：5 寸 1024×600、`--ui-mode=instrument`。本文 Main 指 Waveform 参数工作页，桌面 `--ui-mode=main` 不在本轮范围内。

## 1. 设计依据与范围

新 Home 是信号与配置的一级总览：直接编辑 Freq/Level、切换 RF/MOD，看当前 Waveform 与 Sweep 摘要，再进入配置。当前设置 Home 的全部内容归入 System Config。Home 不放左侧层级栏，内容按用户要求分三行。

本地参考为 [SMA100B User Manual，版本 14](C:/Users/jsl/Desktop/SignalDocs/SMA100B_UserManual_en_14.pdf)，已提取并查看相关页面：

| 手册位置 | 观察到的交互 | 本草案采用的部分 |
|---|---|---|
| 第 52 页，图 3-9；第 53 页，3.4.2.1–3.4.2.2 | 顶部频率/电平直接编辑；Home 功能块显示关键参数，并作为配置入口 | 小标题、大数字；Waveform/Sweep/System 摘要入口 |
| 第 53–54 页，3.4.2.3 与表 3-10 | 左侧任务栏包含 Home 与已打开的对话框入口 | 保留一个明确的 Home 返回入口，按用户要求省去整条任务/层级栏 |
| 第 56 页，3.4.3 | Home 返回总览；关闭图标/Esc 关闭对话框 | 区分返回总览、关闭功能弹窗、完成/取消参数输入 |

手册使用 Modulation 功能块；这里按 SGStudio 业务使用 Waveform。手册没有定义本文的三行尺寸、单列 Main 或 Sweep 快速开关；这些是用户要求与本草案建议，不声称是 SMA 原样布局。本文示例值用于说明显示内容，不是当前设备读数。

本草案取代此前 Instrument 的“设置 Home + 全宽 Main + Home/BaseBand 双按钮悬浮框 + 4×4 类型选择弹窗”导航方案。[原设计文档](<5寸屏幕的Home Main 交互设计.md>) 保留已接入版本的事实和历史。

## 2. Home：三行总览

```text
┌────────────────────────────────────────────────────────────────────────────────────────────┐
│ Freq（小标题）                 │ Level / PEP（小标题）          │ RF         │ MOD         │
│ 500.1234 MHz                   │ -20.00 dBm                     │ [ ON ]     │ [ ON ]      │
│                                │ RMS: -23.00 dBm                │            │             │
├────────────────────────────────────────────────────────────────────────────────────────────┤
│ Waveform 配置                                                                          >   │
│ 当前调制：多音                                                                             │
│ 音调数量：5       频率间隔：1.000 MHz       音调相位：Random                               │
│ 随机种子：1                                                                                │
│                                                                                            │
│ 点击此区域 → Main：左侧现有 Panel，右侧单列类型列表                                        │
├────────────────────────────────────────────────────────────────────────────────────────────┤
│ System                                  >   │ Sweep                                ┌────┐ │
│ 参考源：Internal       参考频率：100 MHz     │ 频率扫描：1.000 → 2.000 GHz           │ ON │ │
│ 输入触发：Bus          触发动作：Sweep      │ 步进：10 MHz                         │    │ │
│ 本振模式：Auto         RF 端口：Standard    │ 驻留：1 ms                           └────┘ │
│ 参考输出：OFF          温度：42.1 °C        │                            （方形，ON 绿色）│
│ 点击 → 当前系统设置内容                     │ 点左摘要 → Sweep；点右开关 → 切换使能       │
└────────────────────────────────────────────────────────────────────────────────────────────┘
```

**第一行：复用 CommonPanel 的 Freq、Level、RF、MOD。**

Freq 与 Level 都是可编辑控件，点击沿用现有数值输入；标题较小、数值行较大，单位保持清楚。Level 继续使用现有 PEP/RMS 语义，PEP 主值突出，RMS 作为较小的辅助读数，不重新定义功率含义。RF、MOD 开关直接操作原开关，显示原真实状态和可用性。

这行只保留上述四项；Sweep 从 CommonPanel 移到第三行。首页不另加标题行、状态条或侧边导航，保证主要读数与配置块的空间。

**第二行：整宽 Waveform 配置摘要。**

明确显示当前选择的业务类型及其实际参数。例如多音显示音调数量、间隔、相位模式，Random 时补充随机种子；参数来自现有业务配置并随回写更新。数字调制则显示其现有调制类型及关键参数，回放等类型显示各自的已有配置，不套用多音字段。

点击区域进入 Main，直接打开上次选定类型的现有 Panel。首页摘要用于看当前配置；进入 Main 后参数仍在原 Panel 中编辑。此行不另设与顶部 MOD 重复的快速开关。

**第三行：左 System、右 Sweep。**

Sweep 摘要显示当前类型（频率 / 功率 / List）、关键范围和当前使能状态，类型变化后摘要随之变化。扫描区域保持完整的 496×192 px 卡片，标题位于上方；其下原内容区左侧为参数，右侧是卡片内部的 88×88 px 正方形快速开关，26 px 状态字，在内容区垂直居中，保留卡片右内边距 16 px，与参数间隔 8 px。使能时绿色高亮，关闭时保持普通样式。点方形开关只切换 Sweep 使能、留在 Home；点卡片其余区域进入 Sweep 配置。卡片使用 SummaryCardButton 的 hitButton 排除子按钮命中范围，使父卡片收到按钮范围内的点击也不会触发导航；不增加事件重放或另一份使能状态。C++ fixed size 与局部 QSS min/max-width/height 仍为 88 px，padding 为 0。

快速开关与现有 StepSweepPanel 的 Enabled 使用同一状态与操作入口，保留原校验、设备可用性和业务回写。显示“已使能”不等同于“设备正在扫描”；若现有执行状态是等待触发等，应使用已有状态说明。操作未完成或未成功时不能仅因为点过开关就显示成功。

Home 中的 System 卡片显示四行两列的八项当前配置与读数，按频率基准、触发执行、射频硬件和监视读数分组：

| 行 | 左列 | 右列 |
| --- | --- | --- |
| 1 | 参考时钟源 | 参考频率 |
| 2 | 输入触发源 | 输入触发动作 |
| 3 | 本振模式 | RF 输出端口 |
| 4 | 参考时钟输出 ON/OFF | 设备温度 |

配置值与枚举名称复用系统设置现有 property 及设备回写；参考频率取 CommonDeviceProfile 的有效频率，避免把内参考模式下保存的外参考编辑值当作当前参考频率。温度复用现有 DeviceRealTimeStatus（设备温度，非 CPU 温度），以一位小数和 °C 显示。设置/回写、枚举选项、连接状态与温度变化刷新摘要，返回 Home 立即同步；无连接、无枚举显示项或温度无效时显示 `—`。八项各占一个 QLabel 单行，不拼空格模拟列；文字超宽省略，整卡 tooltip 保留全部原文，点击仍进入现有 System Config。

**建议尺寸，供确认比例。**

| 区域 | 初始建议 |
|---|---|
| 外边距 / 行列间距 | 12 px / 8 px |
| 第一行 | 高 128 px；Freq/Level 各 360 px；RF/MOD 各 128×128 px |
| 第二行 Waveform | 宽 1000 px，高 240 px |
| 第三行 | System/Sweep 各宽 496 px，高 192 px |
| Sweep 快速开关 | 扫描卡片内部内容区右侧 88×88 px，字号 26 px；与左参数间隔 8 px，右内边距 16 px |
| Freq/Level 字号 | 标题 22 px，主要数值 38 px，辅助 RMS 22 px；RF/MOD 状态 28 px |
| 摘要块字号 | 类型 30 px；Waveform/Sweep 参数 24 px；System 四行两列参数 20 px |

宽度：360 + 360 + 128 + 128 + 3×8 + 2×12 = 1024 px。
高度：128 + 240 + 192 + 2×8 + 2×12 = 600 px。
线框不按字符精确模拟像素；以上是设计起点，尚未真机验收。

## 3. Waveform Main：左侧已有 Panel，右侧单列

Main 右侧保留单列业务类型列表，上下箭头滚动，条目高亮表示当前选择；条目来源、许可与设备可用性沿用现有业务列表。下图仅示意部分条目，不重新规定注册次序。

左侧直接复用当前 MultitonePanel。这里画 Random 模式，沿用其实际参数顺序；不存在额外的“多音参数 / 当前 BaseBand”公共标题栏。

```text
┌────────────────────────────────────────────────────────────────────────────────────────────┐
│ Enabled                              │ Tone Phase                           │ [ Back ]     │
│          [ 开 | 关 ]                 │          Random ▾                    │      ↑       │
│──────────────────────────────────────┼──────────────────────────────────────│ [多音]       │
│ Phase Random Seed                    │ Freq Spacing                         │  数字调制    │
│          1                           │          1.000 MHz                   │  AM          │
│──────────────────────────────────────┼──────────────────────────────────────│  FM          │
│ Tone Count                           │                                      │  脉冲        │
│          5                           │                                      │  斜波扫描    │
│─────────────────────────────────────────────────────────────────────────────│  …           │
│ Tone Num       FrequencyOffset                     Enabled                  │              │
│     1             -2.000 MHz                         [x]                    │              │
│     2             -1.000 MHz                         [x]                    │              │
│     3              0 Hz                              [x]                    │              │
│     4              1.000 MHz                         [x]                    │              │
│     5              2.000 MHz                         [x]                    │              │
│                                  …（表格 / Panel 可滚动）                   │              │
│─────────────────────────────────────────────────────────────────────────────│      ↓       │
│          [ Save IQ Data ]            │                                      │              │
└────────────────────────────────────────────────────────────────────────────────────────────┘
```

- 右侧列表约 132 px，列表与左侧参数区间距 8 px；左侧约 860 px。列表固定单列，不因空间改变而换成两列；上下箭头只滚动类型列表。
- Back 按钮放在右侧列表的上方，不占左侧 Panel 的行高。点击类型条目切换已有 Panel，保留其原参数绑定、动态可见性和业务选择行为。
- 多音 Random 模式：第一行 Enabled/Tone Phase；第二行 Seed/Freq Spacing；第三行 Count/空位；随后是跨两列的三列音调表和左下 Save IQ Data。
- Fixed/Parabolic 模式继续隐藏 Seed，Count 上移，与 Freq Spacing 同行。Notch Width、Discrete Tone Mode、Show Waveform 沿用当前隐藏状态；不添加 AutoScale 或“生成波形”按钮。
- 左侧参数按钮继续采用大控件尺度，纵向空间不足允许滚动；右侧 Back 与类型列表独立于参数区滚动。
- 二级配置页优先给配置内容留空间，不重复首页 128 px 高的大 CommonPanel 行；需要调整 Freq/Level 或 RF/MOD 时点 Back，首页即达。
- 进入 Sweep 或 System 是导航，不将 Waveform 类型改为 Sweep/System；返回后仍看到原 Waveform 配置与类型。现有列表中若保留 Sweep 入口，它应进入同一份 Sweep 配置内容，不能产生第二套扫描参数。

## 4. Sweep 配置：现有 StepSweepPanel

下面是当前 Freq 扫描页的实际两列控件，不另做一个扫描参数面板：

```text
┌────────────────────────────────────────────────────────────────────────────────────────────┐
│ Sweep 配置                                                                     [ Back ]    │
├────────────────────────────────────────────────────────────────────────────────────────────┤
│ Enabled                                     │ Sweep Type                                   │
│                 [ 开 | 关 ]                 │                 Freq ▾                       │
├─────────────────────────────────────────────┼──────────────────────────────────────────────┤
│ Start Frequency                             │ Stop Frequency                               │
│                 1.000 GHz                   │                 2.000 GHz                    │
├─────────────────────────────────────────────┼──────────────────────────────────────────────┤
│ Frequency Step                              │ Dwell Time                                   │
│                 10 MHz                      │                 1 ms                         │
├────────────────────────────────────────────────────────────────────────────────────────────┤
│ 直接使用现有 StepSweepPanel；Power / List 切换复用已有配置内容。                              │
│ 参数超出视口时纵向滚动，Back 按钮不随参数滚动。                                                │
└────────────────────────────────────────────────────────────────────────────────────────────┘
```

Power 模式沿用 Start Level、Stop Level、Level Step、Dwell Time；List 模式沿用现有 ListModePanel。Home 的快速开关与这里的 Enabled 同步，配置页的参数回写也立即反映到首页摘要。

进入或退出配置页本身不使能/关闭扫描、不改变 RF/MOD。返回 Home 是导航，已经使能的扫描继续按原业务流程运行。

## 5. System Config：六类入口与分类页面

System 现采用六类入口、两列三行。参考时钟、触发与同步、射频与硬件的摘要展示当前设备配置；界面与偏好、配置与启动、接口与维护继续展示功能范围。Home 的 System 卡片仍显示八项当前状态及温度。

点击分类直接跳转内容页面，共用同一标题和右侧 Back，不创建分类模态弹窗。分类 Back 返回 System，System Back 返回 Home。

```text
┌────────────────────────────────────────────────────────────────────┐
│ System                                                  [ Back ]   │
├─────────────────────────────────┬──────────────────────────────────┤
│ 参考时钟                      > │ 触发与同步                     > │
│ 当前参考源、有效频率、输出      │ 当前输入/输出源、动作、边沿      │
│                                 │ 当前输出使能                     │
├─────────────────────────────────┼──────────────────────────────────┤
│ 射频与硬件                    > │ 界面与偏好                     > │
│ 当前本振、RF 端口、低功耗      │ 语言、主题、显示与交互偏好        │
├─────────────────────────────────┼──────────────────────────────────┤
│ 配置与启动                    > │ 接口与维护                     > │
│ 打开/保存、启动恢复策略         │ GPIO/GNSS/SCPI、升级/关于/退出    │
└─────────────────────────────────┴──────────────────────────────────┘
```

触发输入/输出、界面显示/交互采用 Tab；配置与启动为单页，文件动作与启动策略同时显示。选中 Tab 用原按钮主题蓝色整块高亮。语言、主题、启动状态复用原菜单动作，呈现为持续保留的 EnumTextButton，普通菜单状态变化不删除其 Popup；Auto Mod、锁屏、自动亮度、震动以及设备 RefOut/Trigger Output/Low Power 使用横向标签加 SwitchButton。

唯一 DeviceSettingPanel 保留设备参数与快捷接口；偏好内容提取为共用 PreferencePanel，main 继续由 PreferenceDialog 包装原布局，Instrument 在分类页面承载。机箱偏好与 debug-only 设备风扇保持各自条件与 owner。

GPIO、GNSS、SCPI、升级、关于、文件选择和亮度输入保留原功能窗口；关闭回所属分类页。页面和 Tab 切换不重置配置或改变 RF/MOD/扫描状态。完整参数分组、线框、归属与返回规则见 [System 分类页面与 Tab 设计](instrument_system_category_dialog_design.md)。

本轮不修改 Excel 翻译资源；新标题/摘要键的翻译暂缓，部分新文案会暂显示英文。页面重构已有分支提交，文档仅静态核对，未运行验收。

## 6. 返回 Home：右侧 Back 入口

三个二级页面统一在右上角提供 **一个固定 Back 按钮**：

| 页面 | Back 按钮放置 | 对配置空间的影响 |
|---|---|---|
| Main | 右侧单列列表上方 | 不挤占左侧业务 Panel 高度 |
| Sweep | 约 48 px 的配置页标题行最右侧 | 下方全宽承载原 StepSweepPanel |
| System Config | 现有约 48 px 标题行最右侧 | 替换原返回 Main 的入口 |

三个页面的 Back 按钮均沿用 132×48 px 的构造尺寸，并共用 System 的 48 px QSS min/max-height 与纵向 padding 规则；列表箭头继续使用独立的高度规则。统一的是同一盒模型下的样式基线，实际外框高度仍需结合主题边框运行验收。

固定入口每次都在同一触屏位置，能快速形成操作习惯，也不会遮挡参数、音调勾选列或滚动条。Home 首页本身没有这个按钮，也不为此增加左侧任务栏。

用户已确认固定 Back 入口返回 Home；旧 Home/BaseBand 悬浮框与类型选择弹窗路径已移除，不保留并行的悬浮导航。原屏幕锁仍复用自身实现，Instrument 下的定位参照完整页面栈。

页面关系：

```text
                           Home：三行总览
                ┌────────────┼─────────────┐
             Waveform      Sweep         System
                │            │             │
        Main：列表 + Panel   原扫描 Panel   六类摘要入口
                │            │             └─ 分类页面 / Tab
                │            │                  └─ 功能窗口 → 关闭回分类
                │            │             分类 Back → System
                └────────────┴─────────────┘
                      点 Back → 总览首页
```

建议统一以下返回行为：

- 二级页面点 Back 或按物理 Home 键，回总览；再次进入时保留原类型、配置和既有页面状态。导航不触发预设、不重置参数，也不停止当前输出或扫描。
- System 分类页增加一层返回：先回六类入口，再回 Home；Tab 切换不增加返回层级。
- 在总览上再按 Home 键，留在总览；不沿用旧 Home/Main 双向切换。这是新一级总览结构的交互建议。
- 功能弹窗使用原关闭流程，关闭先回所属配置页；物理 Home 键关闭一层功能弹窗时同样回所属页，再按页面层级返回。System 分类先回六类入口，再回 Home；升级等原关闭限制继续有效。
- 数值/文本键盘与确认框打开时先完成或取消输入，沿用已有输入层导航限制，避免 Home 操作丢弃未提交输入或跳过确认。

## 7. 实现与验证边界

已在现有 Core 文件中接入，不新增源文件或重建业务 Panel。CommonPanel 只调整 Instrument 呈现和归属位置；扫描业务、System 设置动作继续复用现有实现。

- MainWindow 的 Instrument 根栈依次承载 Home 总览、Main、Sweep、System，启动配置加载后停在总览。Main 右列固定 Back 加同一份业务列表，左侧仍由 FancyTabWidget 承载原 Panel；独立 Sweep 页直接承载唯一 StepSweepPanel，System 由原设置 Home 代码改为 InstrumentSystemWidget。
- System 内部栈承载分类概览与分类内容，共用一条标题/Back；设备和偏好只调整 Instrument 呈现，main 保留原布局。详细归属与静态边界见 [System 分类页面文档](instrument_system_category_dialog_design.md)；新增翻译键尚未补 Excel。
- 桌面 main 仍将 Sweep/General Settings 注册到 FancyTabWidget 的 standalone 页面，CommonPanel 与右侧 dock 布局和原响应式判定保留。新 Instrument 列表分支固定单列 132 px，条目 130×100 px，上下按钮各 40 px，滚动条隐藏但箭头和触摸仍滚动原列表。
- Home 的 Waveform 摘要读取当前 Panel 中可见 LabelButton/EnumTextButton 的原标题与原格式化显示值，最多展示六项，完整字段放在 tooltip；不复制业务 profile、枚举或单位格式规则。复用字段的 LayoutRequest/显隐通知与页面切换通知更新摘要，回 Home 时也立即同步。
- System 摘要读取 RefClockSource/TriggerSource/TriggerAction/LoMode/RfPort 的值与原 enumDisplayOptions 名称、CommonDeviceProfile::refClockFrequency() 的有效频率与 systemClockOut() 的参考输出镜像；这是与设置页相同的当前配置镜像，设备成功配置及参考时钟查询回写后同步，不增加独立硬件查询。温度从 DeviceRuntimeBridge 已过滤当前设备并负责断开清空的 DeviceRealTimeStatus property 获取，沿用 InvalidDeviceTemperature 哨兵判定。Home 不保存另一套配置或温度缓存；订阅原 property 的 valueChanged 和枚举选项动态属性事件，复用现有合并刷新与 showEvent；单元格 resize/font change 后重算省略显示。
- Sweep 摘要读取原 getProfile()/listModeProfileMap()，显示频率/功率范围、步进、驻留或 List 范围；快速按钮调用原 setBtnEnabledChecked()，状态取自原开关，继续走现有载波模式校验与拒绝回写。sweepEnabled 动态样式属性只反映原使能状态，变化时刷新按钮样式；两种主题均使用现有 Panel 的 ON 绿色 #00ce01 / 深色文字 #054604，关闭或不可用时不匹配高亮。Sweep 可用性仍由 MainWindow 的原统一规则裁决。
- 仅读取已有基带参数，不额外绑数值键盘。Freq/Level 原绑定继续由唯一 CommonPanel 负责；Level 的 PEP 大数值与辅助 RMS 共用已有功率单位换算。新尺寸和字体只匹配 Instrument 总览/工作页/System 对象，键盘自身规则保留。
- 菜单动作、翻译源表、SCPI、插件业务接口和持久化字段不变；启动用户预设文件选择取消时，控制器恢复原 QAction 选中状态。Instrument 的配置恢复不因旧的“当前页为 Sweep”记录而离开 System 或首页；载波与扫描参数仍按原恢复流程处理，当前基带工作页由 FancyTabWidget 保留。

静态依据：

- Core CMake 纳入 commonpanel、instrumenthomewidget、devicesettingdialog、stepsweeppanel、listmodepanel；HTRA CMake 纳入 multitonepanel.cpp/.ui。
- 已核对多音 UI 与 updateSeedDependentRows() 的动态行布局，以及 StepSweepPanel 的三种扫描页面、Enabled 状态入口和现有设置 Home 的布局。
- Home 尺寸预算为 1024×600；二级页内容允许滚动。实际字号、译文、触摸尺寸与平台显示尚未验收。

Sweep 开关恢复卡片内及点击边界修正见 [本轮 TaskLog](../TaskLog/2026-10-12_restore-sweep-toggle-inside-card.md)，本轮按用户要求仅静态检查。上轮方形开关尺寸见 [此前 TaskLog](../TaskLog/2026-10-12_enlarge-home-sweep-toggle.md)，其卡片外位置已被本轮修正替代。System 四行两列与字符串编译修复见 [对应 TaskLog](../TaskLog/2026-10-12_fix-system-summary-encoding-and-grid.md)。首次配置与温度摘要见 [实时摘要 TaskLog](../TaskLog/2026-10-12_instrument-system-live-summary.md)。此前细调见 [Waveform/右侧 Back TaskLog](../TaskLog/2026-10-12_refine-instrument-waveform-and-back-layout.md)。实施记录见 [SMA 风格 Instrument Home TaskLog](../TaskLog/2026-10-12_implement-sma-instrument-home.md)。此前 Core Debug 构建已通过；实际布局、触摸、深浅主题、翻译和 Wayland 的运行验收仍待进行。
