# 数字调制三方库波形生成意图接口提案

## 目标

- 给三方库提供一套更清晰的数字调制波形生成接口，显式区分三种调用意图：
  - 完整生成
  - 周期生成
  - 内存截顶生成
- 保留现有低层 `GenerateDigitalModWaveform(..., SymbolLength, ...)` 接口，避免 ABI 破坏。
- 让上层在真正生成前就能查询“本次会生成多长、多大、是否周期安全”，避免 UI 和下载链路只能靠估算值猜测。
- 消除当前接口里“长度单位不清”“调用方自己拼策略”“生成后再截断不保证周期边界”的问题。

## 背景问题

当前数字调制外部接口为：
数字调制界面 PN完整长度，内存长度比较，内存不够，
```cpp
DLLEXPORT_API int GenerateDigitalModWaveform(
    int objId,
    const DigitalModParams *paramsIn,
    double sampleRate,
    int32_t SymbolLength,
    short **iq,
    int32_t *lenOut);
```

该接口本质上是“低层原始接口”，只接受一个 `SymbolLength`，但业务侧实际已经有三种完全不同的意图：

- 为保存文件或离线分析，需要完整生成。
- 为发射机循环播放，需要一个最小可循环周期。
- 为满足设备下载内存上限，需要一个不超过指定字节数的结果。

当前问题：

- `SymbolLength` 只表达“生成多少符号”，不能表达“为什么是这个长度”。
- 上层必须自己把 PN、调制类型、映射方式、滤波器、发射机内存限制混合推导成一个 `SymbolLength`，职责过重。
- 当前业务里的 Trim 是“先完整生成，再按 sample 直接截断”，这不保证周期边界安全。
- `lenOut` 当前语义接近“int16 元素个数”，但估算常用“复采样点数/字节数”，单位容易混淆。
- 若未来库内部将 PN 完整周期从 `2^PN` 改为 `2^PN - 1`，或引入更复杂的差分编码/滤波周期处理，调用方都需要同步改逻辑，耦合过重。

## 设计原则

- 兼容优先：旧接口保留，旧调用方无需改动即可继续工作。
- 语义优先：把“调用意图”作为一等公民，而不是让调用方塞进一个长度参数里。
- 两段式优先：先查询计划，再执行生成。上层可在生成前拿到明确长度、大小、边界安全信息。
- 单位明确：区分“符号数”“复采样点数”“IQ short 个数”“字节数”。
- 不做静默降级：周期生成失败、内存不足、无法保证周期连续时，应返回显式状态，而不是偷偷改成别的模式。

## 推荐接口形态

### 1. 保留现有低层原始接口

保留现有接口作为 Raw API：

```cpp
DLLEXPORT_API int GenerateDigitalModWaveform(
    int objId,
    const DigitalModParams *paramsIn,
    double sampleRate,
    int32_t symbolLength,
    short **iq,
    int32_t *lenOut);
```

语义定义为：

- 这是一个“按调用方指定符号数直接生成”的低层接口。
- 库不对调用方的高层意图做推断。
- `symbolLength` 单位固定为“符号数”。
- `lenOut` 继续保持兼容语义，但新接口中不再只暴露这个字段。

不建议直接在该接口上追加 `expectedLen = -1` 之类参数，原因是：

- 这是 `extern "C"` 导出接口，直接改参数列表会破坏 ABI。
- `expectedLen` 的单位不清，容易和 `symbolLength`、sample 数、字节数混淆。
- 一个参数无法同时表达“完整生成 / 周期生成 / 内存截顶生成”三种意图。

### 2. 新增意图查询接口

推荐新增一组“先查询，再生成”的意图式接口。

```cpp
typedef enum DigitalWaveformIntent {
    DIGITAL_WAVEFORM_INTENT_FULL = 0,
    DIGITAL_WAVEFORM_INTENT_PERIOD = 1,
    DIGITAL_WAVEFORM_INTENT_MEMORY_CAPPED = 2
} DigitalWaveformIntent;

typedef enum DigitalWaveformCapPolicy {
    DIGITAL_WAVEFORM_CAP_FLOOR_TO_SAFE_PERIOD = 0,
    DIGITAL_WAVEFORM_CAP_HARD_TRUNCATE = 1
} DigitalWaveformCapPolicy;

typedef struct DigitalWaveformIntentRequest {
    uint32_t structSize;
    const DigitalModParams *paramsIn;
    double sampleRate;
    DigitalWaveformIntent intent;

    // 仅 MEMORY_CAPPED 使用，单位为字节。
    int64_t maxOutputBytes;

    // 仅 MEMORY_CAPPED 使用。
    DigitalWaveformCapPolicy capPolicy;

    // 预留，便于未来扩展。
    uint32_t flags;
    uint32_t reserved[4];
} DigitalWaveformIntentRequest;

typedef enum DigitalWaveformPlanFlags {
    DIGITAL_WAVEFORM_PLAN_SEAM_SAFE = 1 << 0,
    DIGITAL_WAVEFORM_PLAN_CAP_APPLIED = 1 << 1,
    DIGITAL_WAVEFORM_PLAN_MAPPING_ROUNDED = 1 << 2,
    DIGITAL_WAVEFORM_PLAN_FILTER_TAIL_INCLUDED = 1 << 3
} DigitalWaveformPlanFlags;

typedef struct DigitalWaveformPlan {
    uint32_t structSize;

    // 调用方请求的模式。
    DigitalWaveformIntent requestedIntent;

    // 库最终按什么语义规划。正常情况下应与 requestedIntent 一致；
    // 不允许无提示 silent fallback。
    DigitalWaveformIntent resolvedIntent;

    // 库内部认定的原生比特周期。
    // 例如 2^PN 或 2^PN-1，由库负责定义。
    int64_t nativeBitPeriodBits;

    // 最小可循环符号周期；若不适用则为 -1。
    int64_t symbolPeriod;

    // 本次实际建议生成的符号长度。
    int64_t symbolLength;

    // 本次生成的复采样点数估算。
    int64_t complexSampleCount;

    // 本次生成的 IQ short 元素个数估算。
    int64_t iqValueCount;

    // 本次生成的字节数估算。
    int64_t byteCount;

    // 计划附加信息。
    uint32_t planFlags;
    uint32_t reserved[4];
} DigitalWaveformPlan;

DLLEXPORT_API int QueryDigitalModWaveformPlan(
    const DigitalWaveformIntentRequest *request,
    DigitalWaveformPlan *planOut);
```


## 三种意图的明确语义

### 1. 完整生成 `DIGITAL_WAVEFORM_INTENT_FULL`

含义：
- 生成库内部定义的“完整波形”。
- 库负责决定完整长度到底是 `2^PN`、`2^PN-1`，还是考虑编码/成形后的更高层定义。
- 上层调用方不再自己把 `pow(2, PN)` 当作事实写死。

适用场景：

- 保存完整文件。
- 离线分析。
- 开发调试，需要拿到底层完整输出。

### 2. 周期生成 `DIGITAL_WAVEFORM_INTENT_PERIOD`

含义：

- 生成“最小可无缝循环的重复单元”。
- 该周期由库内部根据比特周期、调制映射、差分编码、OQPSK 偏移、滤波器记忆等规则计算。
- 调用方不传符号长度，只表达“我要一个最小可循环周期”。

适用场景：

- 发射机循环播放。
- 希望在不破坏首尾连续性的前提下尽可能节省下载内存。

行为要求：

- 若库无法保证该模式下的首尾无缝，应返回显式错误，而不是偷偷退化成完整生成或硬截断。

### 3. 内存截顶生成 `DIGITAL_WAVEFORM_INTENT_MEMORY_CAPPED`

含义：

- 生成一个不超过 `maxOutputBytes` 的结果。
- 默认建议使用 `DIGITAL_WAVEFORM_CAP_FLOOR_TO_SAFE_PERIOD`：
  - 在不超过上限的前提下，向下取整到一个周期安全边界。
- 仅在调用方明确指定 `DIGITAL_WAVEFORM_CAP_HARD_TRUNCATE` 时，才允许直接按样点或符号硬截。

适用场景：

- 设备内存固定上限，例如 100MB。
- 调用方不关心是否完整，只关心“能装进设备”。

行为要求：

- 若 `FLOOR_TO_SAFE_PERIOD` 下连一个安全周期都放不下，应返回明确错误，例如“cap too small for one safe period”。
- 不建议在该模式下默认做“生成完整波形后再截断”的低效实现。

### 4. 预览生成（简化接口，独立于下载语义）

该接口用于 UI 波形预览和 FFT 预览，不参与下载、保存、周期语义。

推荐新增：

```cpp
DLLEXPORT_API int GenerateDigitalModPreviewWaveform(
    int objId,
    const DigitalModParams *paramsIn,
    double sampleRate,
    int32_t expectedComplexSamples,
    short **iq,
    int32_t *lenOut);
```

语义约定：

- `expectedComplexSamples` 单位为 complex sample（一个 I/Q 对为 1 点），不是字节数。
- 输出 `iq` 仍为 I/Q 交织的 `short` 数组。
- `lenOut` 单位建议明确为 `int16` 元素个数；在正常情况下 `lenOut = 2 * expectedComplexSamples`。
- 若算法实际可生成点数不足 `expectedComplexSamples`，尾部自动补 0 到目标长度。
- 该接口不要求完整周期，不保证首尾连续，仅用于预览。

设计动机：

- 当前预览链路对点数需求远小于完整下载波形。
- 固定 FFT（例如 1024）和有限平均次数场景，不需要等待完整波形生成完成。
- 把预览接口从完整生成接口中分离，可显著降低 UI 等待时间和内存峰值。

建议默认值：

- `expectedComplexSamples` 默认可取 `16384`（覆盖 1024 点 FFT、16 次平均）。
- 若仅时域波形预览，也可取 `1024`。

## 建议的错误码语义

建议在现有 `0 / -1` 基础上扩展可枚举错误码，至少覆盖：

- `DIGITAL_WAVEFORM_OK`
- `DIGITAL_WAVEFORM_ERR_INVALID_ARGUMENT`
- `DIGITAL_WAVEFORM_ERR_UNSUPPORTED_INTENT`
- `DIGITAL_WAVEFORM_ERR_NO_SAFE_PERIOD`
- `DIGITAL_WAVEFORM_ERR_CAP_TOO_SMALL_FOR_ONE_SAFE_PERIOD`
- `DIGITAL_WAVEFORM_ERR_GENERATION_FAILED`

至少对新接口应返回更明确的错误原因，便于上层区分：

- 参数非法
- 周期不可解
- 周期可解但内存上限不足
- 真正的生成失败

## 与现有项目的对接方式

### 1. 项目内现状

- 当前数字调制上层常把 `pow(2, PN)` 直接作为 `SymbolLength`。
- 当前大波形 Trim 是生成后再按样点截断，不保证周期边界。

### 2. 建议迁移策略

- 第一阶段：三方库保留原始接口，新增 Query/Plan/GenerateByPlan。
- 第二阶段：项目中“保存文件”路径改用 FULL。
- 第三阶段：项目中“发射下载”路径改用 PERIOD 或 MEMORY_CAPPED。
- 第四阶段：UI 上的大波形提示不再依赖本地粗略估算，而是优先调用 `QueryDigitalModWaveformPlan` 获取真实计划信息。

### 3. 兼容策略

- 所有旧调用方继续使用 `GenerateDigitalModWaveform(..., symbolLength, ...)`。
- 新调用方逐步切到意图式接口。
- 旧接口不改语义、不改参数列表、不改内存释放方式。

## 建议补充给三方库开发方确认的问题

- 对数字调制而言，库内部认定的原生 PN 完整周期到底是 `2^PN` 还是 `2^PN-1`？
- 差分编码、OQPSK、Pi/4 DQPSK、Half Sine 等情况下，最小可循环周期如何定义？
- RRC/RC/Gaussian 的滤波器瞬态是否要求“周期生成”包含额外过渡区，还是库内部可以直接生成循环连续版本？
- `lenOut` 在旧接口中究竟定义为“int16 元素个数”还是“复采样点数”，建议文档明确。

## 结论

- 保留现有 `GenerateDigitalModWaveform(..., symbolLength, ...)` 作为低层 raw API。
- 新增 `QueryDigitalModWaveformPlan(...)` 负责解释 FULL / PERIOD / MEMORY_CAPPED 三种调用意图。
- 新增 `GenerateDigitalModWaveformByPlan(...)` 负责按计划执行真正生成。

这样可以把“完整生成 / 周期生成 / 内存截顶生成”三种意图彻底拆开，同时兼顾 ABI 稳定性、语义清晰度和上层可控性。