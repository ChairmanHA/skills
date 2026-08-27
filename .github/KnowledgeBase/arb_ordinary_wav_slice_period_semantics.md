# Arb OrdinaryWav 截取与周期语义

本文档只描述 `HTRA -> Arb -> OrdinaryWav` 路径下，`sampleOffset / samplesToUse / period` 三个参数的业务语义、联动规则和当前代码实现。

不适用范围：

- `ProgrammedArb`（信号编辑器生成的分段 `.arb` / 带自定义 chunk 的 WAV 容器）
- `Streaming`
- core runtime 对最终 payload 的通用播放逻辑


## 1. 用户心智

对普通 WAV 回放，当前业务语义应按下面三步理解：

1. `sampleOffset` 决定“从文件的哪个样本开始取”。
2. `samplesToUse` 决定“从该起点向后实际取多少个文件样本”。
3. `period` 决定“一个最终播放周期总共有多少个样本”；如果 `period > samplesToUse`，则在有效波形后面补零。

换句话说：

- `sampleOffset` 是起点。
- `samplesToUse` 是文件切片长度。
- `period` 是最终周期长度。

后面补出来的零，属于“最终周期输出样本”，不属于“从文件里取出的样本”。因此它们计入 `period`，但不计入 `samplesToUse`。


## 2. 约束关系

对于 OrdinaryWav，当前语义对应的约束是：

- `0 <= sampleOffset <= samplesInFile`
- `0 <= samplesToUse <= samplesInFile - sampleOffset`
- `1 <= period <= MAXDOWNLOADSIZE / 4`
- `samplesToUse <= period`

合并后，`samplesToUse` 的实际最大值为：

`min(samplesInFile - sampleOffset, period)`

这意味着：

- `period` 可以小于整文件长度。
- `period` 变小时，不应该把 `sampleOffset` 往回改；应该缩小 `samplesToUse`。
- `sampleOffset` 变大导致文件剩余长度不足时，也不应该把 `sampleOffset` 自动改回去；应该缩小 `samplesToUse`。


## 3. 典型例子

假设：

- `samplesInFile = 1000`
- `sampleOffset = 200`
- 则文件里还剩 `800` 个可取样本。

### 例 1：正常截取 + 补零

- `samplesToUse = 500`
- `period = 700`

结果：

- 从文件中取 `[200, 700)` 这 `500` 个样本。
- 周期总长要求为 `700`，因此在末尾补 `200` 个零。

### 例 2：整段剩余样本 + 更多补零

- `samplesToUse = 800`
- `period = 1200`

结果：

- 从文件中取 `[200, 1000)` 这 `800` 个样本。
- 周期总长要求为 `1200`，因此在末尾补 `400` 个零。

### 例 3：非法的 samplesToUse

- `samplesToUse = 900`
- `period = 1200`

结果：

- 该输入不应按“取 900 个样本”理解，因为文件里实际只剩 `800` 个样本可取。
- 当前实现会把 `samplesToUse` 收口到 `800`，然后再按 `period` 决定补零。


## 4. 当前代码如何实现

当前主链路由 `src/plugins/htra/arbdatagenerator.cpp` 内部收口完成。

### 4.1 普通 WAV 数据生成

`ArbDataGenerator::handleData()` 中，OrdinaryWav 分支按以下顺序处理：

1. 计算 `availableSamples = IQ_handling.size() / 2`
2. 将 `sampleOffset` 收口到 `[0, availableSamples]`
3. 将 `period` 收口到 `[1, MAXDOWNLOADSIZE / 4]`
4. 将 `samplesToUse` 收口到 `min(params.samplesToUse, availableSamples - sampleOffset, period)`
5. 创建长度为 `period` 的输出缓冲区并清零
6. 将 `samplesToUse` 对应的文件片段拷贝到输出缓冲区开头
7. 保持剩余位置为零，即完成补零

因此最终 payload 的语义就是：

- 前半段来自文件切片
- 后半段来自周期补零


### 4.2 参数联动入口

`ArbDataGenerator::setSampleOffset()`

- 只修改起点 `m_sampleOffset`
- 若起点右移后导致剩余文件样本减少，则缩小 `m_samplesToUse`
- 不反向修改 `m_period`

`ArbDataGenerator::setSamplesToUse()`

- 只修改 `m_samplesToUse`
- 按 `min(samplesInFile - sampleOffset, period)` 做上限收口
- 不反向修改 `sampleOffset`

`ArbDataGenerator::setPeriod()`

- 只修改 `m_period`
- 若 `period` 缩小到小于当前 `samplesToUse`，则缩小 `m_samplesToUse`
- 不反向修改 `sampleOffset`


## 5. 为什么不允许 samplesToUse 超过文件剩余长度

这是为了避免 `samplesToUse` 与 `period` 的职责混淆。

如果允许：

- `samplesToUse > samplesInFile - sampleOffset`

就意味着 `samplesToUse` 一部分在表示“从文件里取多少样本”，另一部分又在隐式表示“超出文件尾后自动补零多少样本”。这样会与 `period` 的“周期补零”职责重叠，用户也无法稳定判断：

- 究竟是 `samplesToUse` 在补零，还是 `period` 在补零。

当前实现选择更清晰的职责分离：

- `samplesToUse` 只描述文件切片长度。
- `period` 只描述最终周期长度。


## 6. 边界与已知说明

### 6.1 ProgrammedArb 不适用本语义

`ProgrammedArb` 走的是文件内部定义的分段/点表语义，不允许 UI 的 `sampleOffset / samplesToUse / period` 截取逻辑介入。修改这三个字段时，不应把本文件的规则套到 `ProgrammedArb` 上。

### 6.2 restoreSettings 是旁路赋值

当前 `restoreSettings()` 会直接写入 `m_sampleOffset / m_samplesToUse / m_period`，然后依赖 `handleData()` 做最终防御性收口。

这意味着：

- 就“最终生成的 payload”而言，语义仍然是正确的。
- 但就“成员值是否在 restore 后立即正规化”而言，这条路径目前不是通过 setter 收口的。

如果未来要强化 profile 恢复的一致性，可考虑把 restore 路径也改成复用统一的参数正规化逻辑。


## 7. 修改建议

后续如果需要继续调整这块逻辑，建议优先守住下面这条原则：

- 先定义“从文件里取多少”，再定义“最终一个周期多长”。

只要这个原则不变，`sampleOffset / samplesToUse / period` 的职责边界就不会重新缠在一起。