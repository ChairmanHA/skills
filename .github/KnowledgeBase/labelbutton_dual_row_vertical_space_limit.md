# LabelButton 双行按钮下方空间为什么“塞不进去”

本文档解释一个在 minibar 上反复出现、但在 main-mode `CommonPanel` 上不那么明显的问题：

- 为什么 `LabelButton` 的下行文字看起来总是贴近底部；
- 为什么单纯继续调 `padding-bottom` / `labelMargins` 往往“没有用”；
- 为什么 `CommonPanel` 看起来没这个问题，但其实并不是因为它的 `LabelButton` 结构不同。

## 1. 先看真实结构

`LabelButton` 并不是一个“按钮里画两行字”的单层控件，而是沿用了 `InfoButton` 的两段式布局：

- 上半行：`QLabel#textLabel`
- 下半行：`infoWidget`
- 对 `LabelButton` 而言，`infoWidget` 被替换成了 `QLabel#infoLabel`

关键代码在 [src/libs/controls/infobutton.cpp](../../src/libs/controls/infobutton.cpp)：

```cpp
m_gridLayout = new QGridLayout(this);
m_gridLayout->setContentsMargins(0, 0, 0, 0);
m_gridLayout->setSpacing(0);

m_gridLayout->addWidget(m_textLabel, 0, 0, 1, 1);
m_gridLayout->addWidget(m_infoWidget, 1, 0, 1, 1);
m_gridLayout->setRowStretch(0, 1);
m_gridLayout->setRowStretch(1, 1);
```

这几行决定了三个非常硬的事实：

1. 按钮内部没有额外的第三行 spacer。
2. 上下两行之间没有 layout spacing。
3. 两行按 `1:1` 平分可用高度。

所以，按钮的总高度一旦固定，下半行天然就一直铺到按钮底边。

## 2. `LabelButton` 自己并没有再包一层“可腾挪底部空间”的容器

`LabelButton` 只是把 `InfoButton` 的 `infoWidget` 替换成了一个普通 `QLabel`，代码在 [src/libs/controls/labelbutton.cpp](../../src/libs/controls/labelbutton.cpp)：

```cpp
m_label = new QLabel(this);
m_label->setObjectName("infoLabel");
m_label->setAlignment(m_labelAlignment);
setInfoWidget(m_label);
```

这意味着下半行不是“`QWidget + 内部布局 + label + spacer`”，而是“直接一个 QLabel 填满第二行”。

因此：

- `padding-bottom`
- `setLabelMargins(...)`
- `qproperty-alignment`

这些手段都只能在“第二行已有的那块矩形里”重新分配文本位置，不能凭空创造“第二行下面的新空间”。

## 3. 为什么会产生“下方完全塞不进去空间”的感觉

因为你真正想要的，通常不是“把文本在第二行里稍微上移 1~2px”，而是：

- 在 `infoLabel` 下方看见一块稳定的留白；
- 或者让第二行整体不要一直贴着按钮底边。

但当前结构里：

- 第二行本身就已经是按钮最底部那一半；
- `QLabel` 的 `padding/margins` 只影响它自己的 contents rect；
- 不会改变 `QGridLayout` 第二行的起止位置；
- 更不会在按钮底部再长出一块额外空白。

所以继续加 `padding-bottom`，经常只会看到：

- 文本在第二行内部略微上移；
- 但视觉上仍然觉得“下面没空间”；
- 或者因为按钮总高太紧，效果几乎不可见。

这不是 QSS 失效，而是改动目标超出了这一级 QSS 能控制的边界。

## 4. 那为什么 main-mode 的 CommonPanel 看起来没这个问题

答案不是“CommonPanel 用了另一套 LabelButton 布局”，而是它的垂直预算更宽松，而且它做的是“把两行往中间收”，不是“给底部造空间”。

### 4.1 CommonPanel 用的还是同一个基类结构

`CommonPanel` 创建的也是普通 `LabelButton`，代码在 [src/plugins/core/commonpanel.cpp](../../src/plugins/core/commonpanel.cpp)：

```cpp
LabelButton *button = new LabelButton(parent);
button->setObjectName(QString::fromLatin1(objectName));
button->setText(text);
button->setCheckable(true);
```

并没有换掉 `InfoButton` 这套两行布局。

### 4.2 CommonPanel 的按钮高度更大

`CommonPanel` 所在行的高度是 60px，见 [src/plugins/core/commonpanel.cpp](../../src/plugins/core/commonpanel.cpp)：

```cpp
constexpr int kCommonPanelWideHeight = 60;
```

同时全局 `InfoButton` 在 dark theme 下也是 60px 高，见 [configuration/theme.css](theme.css)：

```css
InfoButton {
    min-height: 60px;
    max-height: 60px;
}
```

这意味着在 `CommonPanel` 里，两行大致可以拿到各自 30px 的高度预算。

### 4.3 CommonPanel 的 QSS 只是把两行往中间挤

`CommonPanel` 的关键规则在 [configuration/theme.css](theme.css)：

```css
Core--Internal--CommonPanel LabelButton#btnRf QLabel#textLabel,
Core--Internal--CommonPanel LabelButton#btnMod QLabel#textLabel,
Core--Internal--CommonPanel LabelButton#sweep QLabel#textLabel,
Core--Internal--CommonPanel LabelButton#deviceSettings QLabel#textLabel {
    padding-top: 5px;
}

Core--Internal--CommonPanel LabelButton#btnRf QLabel#infoLabel,
Core--Internal--CommonPanel LabelButton#btnMod QLabel#infoLabel,
Core--Internal--CommonPanel LabelButton#sweep QLabel#infoLabel,
Core--Internal--CommonPanel LabelButton#deviceSettings QLabel#infoLabel {
    padding-bottom: 5px;
}
```

注意这个规则的真实效果：

- `textLabel` 加 `padding-top: 5px`，是把上行文字往下推；
- `infoLabel` 加 `padding-bottom: 5px`，是把下行文字往上推；
- 两行文字因此更靠近按钮中线；
- 但它没有制造额外底部空间，只是因为 60px 总高度足够，所以视觉上看起来仍然舒服。

换句话说，`CommonPanel` 的“没问题”其实是：

- 同样的结构限制仍然存在；
- 只是高度预算更大；
- 所以这种“往中间收”的做法看起来成立。

## 5. minibar 为什么更容易暴露这个问题

minibar 当前在 [src/plugins/core/minibarwindow.cpp](../../src/plugins/core/minibarwindow.cpp) 里给按钮定了更紧的垂直预算：

```cpp
constexpr int kPrimaryButtonHeight = 54;
```

并且还会叠加一些 minibar 本地约束，例如：

- 本地的上下 gutter / border 视觉切割；
- 本地 `setTextMargins(...)` / `setLabelMargins(...)`；
- 更紧凑的视觉密度目标。

这时同样的两行等分结构就更容易暴露问题：

- 总高更小；
- 每行可用高度更少；
- 再去做 `padding` 或 `contentsMargins`，只能在原本就紧张的那一行里继续挤；
- 于是用户会感觉“怎么调都还是贴着底部”。

## 6. 结论：QSS 能做什么，不能做什么

### QSS / QLabel margins 能做到的

- 改变行内文本位置；
- 让上下两行更靠中间或更靠边；
- 调整文字之间的视觉距离；
- 修正轻微的视觉漂移。

### QSS / QLabel margins 做不到的

- 改变 `InfoButton` 两行 `1:1` 的结构分配；
- 在第二行外部新增真实底部空间；
- 让下半行“不再贴着按钮底边”；
- 在固定总高度不变的前提下凭空增加垂直预算。

## 7. 如果真的要“在下方塞出空间”，正确改法是什么

如果目标是真正给 `infoLabel` 下方留白，必须改结构，而不是继续堆 `padding-bottom`。

可行方案只有这几类：

1. 增加按钮总高度。
2. 修改 `InfoButton` 的 row 分配，不再固定上下 `1:1`。
3. 给下半行换成一个容器：`QWidget + QVBoxLayout + QLabel + spacer`。
4. 减少 minibar 本地额外消耗的垂直预算，比如去掉不必要的 top/bottom gutter。
5. 直接用自绘/自定义布局，不再复用当前 `InfoButton` 双行骨架。

如果不改这些结构边界，只在现有 `QLabel` 上继续调 padding / margin，通常都只是在同一块固定区域里挪字，不会得到真正想要的“底部空间”。

## 8. 本仓库里的实战判断规则

以后再遇到“`LabelButton` 下行文字为什么总贴底”这类问题，先按下面顺序判断：

1. 先看是不是 `InfoButton` 两行等分结构带来的自然结果。
2. 再看按钮总高度是否足够。
3. 再看是不是本地又叠加了额外 border / gutter / contentsMargins。
4. 只有在确认结构预算足够时，才值得继续调 `padding` / `alignment`。
5. 如果用户要的是“真实留白”，直接进入结构改法，不要继续堆 QSS 试错。