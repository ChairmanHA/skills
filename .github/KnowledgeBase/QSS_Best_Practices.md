# Qt QSS 工程实践指南

## 1. 核心机制差异 (VS Web CSS)

QSS 是基于 CSS2.1 的子集，但有其特殊的渲染逻辑。

- **非完全层叠继承**：在 Web 中，给 `body` 设置 `font-family` 会遗传给子元素。但在 Qt 中，如果你给 `QWidget` 设置样式，**不会**自动作用到其子控件（除非设置了 `Qt::WA_StyleSheet` 属性或者使用类型选择器明确指定）。
- **Sub-controls (子控件)**：这是 QSS 的灵魂。例如 `QComboBox` 本身是一个 Widget，但它包含 `::drop-down` (下拉箭头区域) 和 `::down-arrow` (箭头图标)。必须分开绘制。
- **Pseudo-states (伪状态)**：除 `:hover`, `:checked` 外，Qt 特有 `:pressed`, `:open` (下拉菜单打开时), `:editable` 等状态。

## 2. 常用选择器最佳实践

### 2.1 类与类型选择器
- `QPushButton`: 匹配 `QPushButton` 及其**所有子类**（如你自定义的 `MyButton`）。
- `.QPushButton`: **只匹配** `QPushButton` 类本身，**不匹配**其子类。
  - *Tip*: 如果你发现你的自定义控件如果不经意间继承了父类的样式，改用 `.BaseClass` 可以阻断继承。
- `namespace--ClassName`: Qt 处理命名空间的方式是将 `::` 替换为 `--`。
  - 例如 `Controls::RightPanel` 在 QSS 中写作 `Controls--RightPanel`。

### 2.2 属性选择器 (动态换肤神器)
利用 Qt 的 `Q_PROPERTY` 机制，可以在 C++ 中设置属性，在 QSS 中响应。

**C++:**
```cpp
// 给某个输入框标记为错误状态
myLineEdit->setProperty("state", "error");
myLineEdit->style()->unpolish(myLineEdit); // 强制刷新样式，必须调用！
myLineEdit->style()->polish(myLineEdit);
```

**QSS:**
```css
QLineEdit[state="error"] {
    border: 2px solid red;
    background-color: #ffe6e6;
}
```

#### 声明属性、动态属性与 setter 的区别

- `QLineEdit[state="error"]` 这类属性选择器只会读取当前属性值来匹配样式，不会主动写属性。
- `QObject::setProperty("name", value)` 有两条路径：
  - 如果对象的 meta-object 上存在同名 `Q_PROPERTY`，且该属性带 `WRITE` setter，Qt 会先做类型转换，再调用这个 setter。
  - 如果对象上不存在这个声明属性，Qt 才会创建或更新一个 dynamic property；这时不会执行你自定义的 setter、布局调整或额外刷新逻辑。
- 以仓库里的 `SwitchButton::compactMode` 为例：
  - 放进 QSS 的 `SwitchButton[compactMode="true"]`，适合承载 `min-width`、`padding`、`margin`、颜色等纯视觉规则。
  - 保留在 `SwitchButton::setCompactMode()` 里的，是 QSS 不擅长承载的内部 `QHBoxLayout::setContentsMargins()` 与显式 repolish / update。
  - 因此 `switchButton->setCompactMode(true)` 与 `switchButton->setProperty("compactMode", true)` 在这个类上最终会走到同一个 setter；前者只是更直白、更类型安全，也避免属性名拼写错误后悄悄退化成 dynamic property。
- 如果样式表是在属性已经为 `true` 之后才安装的，仍需要一次明确的样式刷新或再次走 setter；选择器本身不会主动把旧控件重刷成新样式。

### 2.3 包含与后代选择器
- `QDialog QPushButton`: QDialog 内部的**所有** QPushButton（子孙后代）。
- `QDialog > QPushButton`: QDialog **直接**子节点中的 QPushButton（亲儿子）。

### 2.4 ID 选择器
- `QPushButton#okButton`: 匹配 `objectName` 为 "okButton" 的按钮。
  - *Tip*: 在 Designer 中设置 `objectName`，或者代码中 `btn->setObjectName("okButton")`。

## 3. 布局与盒模型陷阱

### 3.1 边框内绘图 (Background-clip)
`border-radius` 有时会导致背景溢出。
```css
QPushButton {
    border-radius: 5px;
    background-color: red;
    /* 如果发现圆角处有杂边，尝试添加 layout-margin 相关属性或使用 clip */
}
```

### 3.2 经典的各种 Image
- `background-image`: 平铺，大小不随控件改变（除非 repeat）。
- `border-image`: **九宫格拉伸**。这是做不失真 UI 最重要的属性。
  - `border-image: url(:/bg.png) 4 4 4 4 stretch stretch;`
  - 数字代表切割线的距离（上右下左），图像会被切成9块，4个角不变，中间拉伸。

### 3.3 尺寸限制
`min-width`, `max-width` 在 QSS 中生效，但会受到 C++ 代码中 `QLayout` 的制约。如果 QSS 设置了尺寸但不生效，检查 Layout 的 `setContentsMargins` 或 Widget 的 `sizePolicy`。

### 3.4 高 DPI / 小屏场景下的尺寸策略
- **不要把 QSS 当成完整布局系统**：QSS 适合颜色、边框、状态，但不适合长期承载整套尺寸体系。
- **避免混用点字号和固定像素盒子**：例如 `font: 12pt` 配 `height: 20px`，在高 DPI 下很容易出现文本拥挤和裁剪。
- **少写全局固定高度**：`min-height: 60px`、`max-height: 60px` 这类值在 100% 小屏可能偏小，在 150% 下又可能过大。更稳妥的做法是使用代码侧统一计算后的尺寸 token。
- **固定宽高只用于少量强约束控件**：主窗口、大对话框、复杂表单不应长期依赖固定 `width/height`，应更多根据内容和当前屏幕工作区决定。
- **优先用分档尺寸而不是单值尺寸**：例如 compact / normal / large 三档控件高度，以便后续接入应用内缩放或 DPI 分级。
- **需要缩放的样式应支持生成**：如果项目依赖大量 QSS，建议从“手写固定值”逐步过渡到“基础样式 + 尺寸 token + 按倍率生成最终样式”。

### 3.5 高 DPI 场景下的 QSS 组织建议
- **颜色和状态样式保留在 QSS**：例如 hover、checked、disabled、边框和背景色。
- **关键尺寸由代码统一提供**：例如按钮高度、标题栏高度、图标尺寸、表格行高。
- **减少重复尺寸常量**：同一类控件不要在多个选择器里分别写一份 `50px`、`60px`、`62px`。
- **主题与尺寸解耦**：颜色主题文件与尺寸策略分开，避免未来做应用内 UI 缩放时必须复制多套完整样式文件。

## 4. 复杂控件定制实战

### 4.1 QComboBox (下拉框)
这是最复杂的控件之一。
```css
/* 主体 */
QComboBox {
    padding-right: 20px; /* 给下拉箭头留位置 */
}
/* 下拉按钮区域 */
QComboBox::drop-down {
    subcontrol-origin: padding;
    subcontrol-position: top right;
    width: 20px;
}
/* 下拉箭头图标 */
QComboBox::down-arrow {
    image: url(:/arrow.png);
}
/* 弹出的下拉列表视图 */
QComboBox QAbstractItemView {
    border: 1px solid gray;
    selection-background-color: blue;
}
/* 列表项的高度/颜色只能在这里设置，QComboBox本身管不了弹出层内部 */
```

### 4.2 QSlider (滑块)
需要分别定制轨道 (`groove`) 和 滑块 (`handle`)。
```css
QSlider::groove:horizontal {
    height: 8px;
    background: #e0e0e0;
}
QSlider::handle:horizontal {
    background: blue;
    width: 18px;
    margin: -5px 0; /* 让把手比轨道高，通过负 margin 居中 */
    border-radius: 9px;
}
```

## 5. 调试技巧
- **Ctrl+T 热加载**：你刚刚实现的功能。
- **背景色法**：不确定哪个控件占了位置？给它加 `background-color: red;`。
- **Inspector**：VS Code 或 Qt Creator 本身没有内置运行时 CSS 调试器（像 Chrome 开发者工具那样），通常需要依靠经验和试错。

### 5.1 High DPI 调试补充
- **优先在混合缩放双屏上测试**：例如 100% + 150%，这比单屏更容易暴露问题。
- **重点看文本截断和点击热区**：高 DPI 问题不一定表现为模糊，也可能表现为文字被裁、图标偏移、点击区域错位。
- **对比应用启动屏和关闭屏**：restoreGeometry 在不同 DPI 屏之间最容易引入隐藏问题。
- **在样式调试时区分“视觉问题”和“窗口系统问题”**：白边、重影、拖一下才恢复，往往不只是 QSS 问题，还可能与 frameless、backing store、NCA 刷新时机有关。

## 6. 常见问题 (FAQ)
- **Q: 为什么 QWidget 设置了背景图不显示？**
  - A: 原生 `QWidget` 默认只支持 `background-color`，不支持 `background-image` 等高级绘图，除非重写 `paintEvent`。**但在 QSS 中**，通过样式表强制设置它可以生效，但有时需要加 `attribute(Qt::WA_StyledBackground)`。
  - 也可以直接用 `QFrame` 代替 `QWidget` 作为容器，`QFrame` 对 QSS 支持更好。

- **Q: 样式冲突谁说了算？**
  - A: 优先级：`qApp->setStyleSheet` < `parentWidget->setStyleSheet` < `widget->setStyleSheet`。
  - 选择器特异性（Specificity）原则与 Web 相同：`ID` > `Class` > `Type`。

- **Q: `qproperty-xxx` 能设置什么？**
  - A: 任何有 `Q_PROPERTY` 宏且带有 `WRITE` 方法的属性。
  - 常用：`qproperty-alignment` (QLabel), `qproperty-iconSize` (QAbstractButton), `qproperty-text` (测试用)。
  - `qproperty-xxx` 是“从样式表写属性”；而 `[xxx="value"]` 是“按当前属性值做选择器匹配”，两者不要混淆。

- **Q: 为什么我的 QSS 在高 DPI 下看起来“一会太小一会太大”？**
  - A: 通常不是 QSS 本身“失效”，而是样式里写死了大量固定像素尺寸，同时应用又运行在不同的系统缩放或 DPI 环境中。需要把关键尺寸从固定值改为可按 DPI 或应用内缩放重算的值。

- **Q: 只改 QSS，能彻底解决高 DPI 小屏问题吗？**
  - A: 通常不能。QSS 只能解决视觉层的一部分问题。真正完整的方案还需要统一尺寸体系、多倍率图标资源、窗口几何恢复策略，以及应用内 UI 缩放。
