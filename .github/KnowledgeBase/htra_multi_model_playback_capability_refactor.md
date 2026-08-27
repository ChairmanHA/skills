# HTRA 多型号 Playback 能力与热插拔重构设计

日期：2026-07-15

状态：Phase A / B / C 已实现；Phase D 已完成 Digital、DSSS、OFDM 以及 HTRA AM/FM/PM/Pulse/Multitone/Ramp/AWGN，Phase E 尚未实现。`4e62cc8` 的临时连续 400 MHz 逻辑已回退；各业务 active 参数行为以 [Waveform_Parameters_Constraints.md](Waveform_Parameters_Constraints.md) 为准。

## 1. 结论

这次多型号支持不能继续通过修改全局 `DATA_SAMPLE_RATE_MAX` 或新增某个业务私有 `DIGITAL_SAMPLE_RATE_MAX` 完成。目标型号的合法采样率本身是不连续集合：

$$
[195.3125\text{ kHz}, 200\text{ MHz}] \cup \{400\text{ MHz}\}
$$

因此目标架构必须同时完成四件事：

1. `IDevice` 在 open 成功后暴露当前物理设备的不可变能力值，DeviceManager 再包装 current snapshot。
2. Core 在业务启动和 runtime request 重建前，把同一份能力快照同步分发给所有 business。
3. Business 根据能力重新计算参数和文件兼容性，并回写 Property value/status；`PropertyMetadata` 只保留最基础的输入约束，UI 不理解采样率空洞，也不直接查询设备。
4. Phase B 已让 Playback request 携带设备 UID/capability revision 并阻止旧 request 重放；Phase C 已把 request payload 改为共享不可变 handle，并迁移 Quick Waveform 与 ARB Ordinary/IQS 文件路径。

Streaming 继续使用独立的 62.5 MSPS 上限。它是当前软件/传输链路约束，不随 Playback 提升到 200/400 MSPS。

Phase A 已把 `STREAMING_SAMPLE_RATE_MAX` 从 `DATA_SAMPLE_RATE_MAX / 2.0` 的派生关系中拆开并显式固定为 62.5 MSPS。未来 API 返回设备流能力后，再与该软件链路上限求交集。

## 2. 设备能力基线

所有容量均按二进制 MiB 计算；最大值采用包含边界的语义，即 `payloadBytes <= maxPlaybackBytes` 合法。model 122、132 及其他 HTRA 型号使用同一判据，型号本身不再决定 Playback 档位。

| `device_query_options()` 结果 | Playback 合法采样率 | 最大 Playback 数据量 | Streaming 合法采样率 |
| --- | --- | ---: | --- |
| 不含 `OPTION_BW_320M_TX` | `[195.3125 kHz, 125 MHz]` | `125 * 1024 * 1024` Bytes | `[195.3125 kHz, 62.5 MHz]` |
| 含 `OPTION_BW_320M_TX` | `[195.3125 kHz, 200 MHz] U {400 MHz}` | `1000 * 1024 * 1024` Bytes | `[195.3125 kHz, 62.5 MHz]` |

当前能力来源已经从 HTRA 插件内的 model 表切换为设备 open 后的 `device_query_options()`。`FancyDevice` 一次查询并同时识别 `OPTION_MEDIUM_POWER` 与 `OPTION_BW_320M_TX`，然后把带宽选件结果交给 HTRA resolver；同时把 API 返回的完整选件号集合、H2 命名空间和查询成功状态作为不透明 `DeviceOptionSnapshot` 发布到 Core capability snapshot。普通业务继续消费解析后的 Playback/Streaming 能力，不解释原始选件号；Updater 只用这份快照匹配包内固件 Profile。

选件查询成功但未返回 `OPTION_BW_320M_TX` 时使用125 MSPS/125 MiB基线。查询失败时同样发布保守基线并记录 warning，不推测扩展能力，但 `DeviceOptionSnapshot.known` 保持 `false`，因此固件 UI 能区分“明确无选件”和“选件查询失败”。任何由 H2 API 成功打开的 HTRA model 都按这两档之一发布 supported snapshot；断开设备时仍发布新的 unavailable revision。

## 4. Core 能力模型

### 4.1 采样率必须是 domain，不是 min/max

建议在 Core 公共边界新增值类型，例如：

```cpp
struct ClosedRateRange {
    double minimumHz = 0;
    double maximumHz = 0;
};

class SampleRateDomain {
public:
    QVector<ClosedRateRange> continuousRanges;
    QVector<double> isolatedPoints;

    bool contains(double valueHz) const;
    std::optional<double> highestAtOrBelow(double valueHz) const;
    std::optional<double> lowestAtOrAbove(double valueHz) const;
    double minimum() const;
    double maximum() const;
};
```

不要提供语义含糊的通用 `clamp()`。domain 可以提供双向查询原语，但业务默认应优先选择较小合法采样率：

- 用户直接输入 Playback 采样率落入空洞时，通常使用 `highestAtOrBelow()`，例如 `250 MHz -> 200 MHz`，避免静默提高数据率和带宽。
- FSK 等组合参数算出的最低需求落入空洞时，不应机械调用 `lowestAtOrAbove()` 跳到 400 MHz；business 应先降低或联动调整 `Rb / MaxDF / sps`，在连续低档内寻找合法解并回写参数。
- 400 MHz 孤立点不参与默认的“就近”或“向上满足”选择；只有用户明确请求 400 MHz，或某项业务另有明确的 400 MHz 档位语义时才使用。
- `contains()` 仍作为 business 最终合法性判断；输入不合法时由 business 选择较小可行值并回写，而不是让 UI 复制 domain 规则。

孤立点判定应采用集中定义的小浮点容差，只吸收计算误差，不能把接近 400 MHz 的宽区间误认为 400 MHz 档位。

### 4.2 物理能力与 current snapshot 分离

建议新增独立公共头文件 `devicecapabilities.h`，避免继续膨胀 `idevice.h`。核心字段至少包括：

```cpp
struct PlaybackCapabilities {
    SampleRateDomain sampleRates;
    quint64 maxWaveformBytes = 0;
    quint32 bytesPerComplexSample = 4;
};

struct StreamingCapabilities {
    SampleRateDomain sampleRates;
};

struct DeviceCapabilities {
    bool supported = false;
    PlaybackCapabilities playback;
    StreamingCapabilities streaming;
    QString unsupportedReason;
};

struct CurrentDeviceCapabilitySnapshot {
    bool available = false;
    quint64 deviceUid = 0;
    quint16 model = 0;
    quint16 hardwareVersion = 0;
    quint64 revision = 0;
    DeviceCapabilities device;
};
```

约束：

- `DeviceCapabilities` 只描述物理/有效能力，不承担 current device 生命周期代际。
- `CurrentDeviceCapabilitySnapshot` 由 DeviceManager 包装并发布，发布后不可变。
- `revision` 在 current target 改变、断开和重新 open 时递增，用于异步任务和 runtime request 反陈旧。
- `maxWaveformBytes` 使用 `quint64`；不要继续用散落的 `uint32_t MAXDOWNLOADSIZE` 作为业务协议。
- `bytesPerComplexSample` 当前固定为 4，但容量换算统一通过该字段完成，避免各业务重复写 `/ 4`、`/ sizeof(short)`。

### 4.3 `IDevice` 与 HTRA resolver

`IDevice` 新增只读虚接口：

```cpp
virtual Core::DeviceCapabilities capabilities() const;
```

默认实现返回 unsupported。`FancyDevice` 在 open 成功后查询设备选件，并在写入最终 `openedDeviceInfo.model/hardware_version` 后调用 HTRA resolver，缓存选件决定的最终能力。UID/model/hardwareVersion 和 revision 由 DeviceManager 包装进 current snapshot，不写回物理能力对象。

HTRA 插件内建议增加单一 resolver：

```text
HtraDeviceCapabilityResolver
    ├─ FancyDevice：device_query_options(handle)
    └─ resolver：resolve(has OPTION_BW_320M_TX)
```

option ID 只在 HTRA 设备层解释，不得复制到 Analog、ARB、Quick Waveform 或 UI。

## 5. 能力发布与层间职责

### 5.1 目标职责

- `FancyDevice`：从硬件/API 得到原始能力，并通过 `IDevice` 返回设备自己的能力。
- `DeviceManager`：维护 current device 的最终能力快照和 revision；不解释具体 model。
- `BusinessManager`：把同一快照同步应用给所有已注册 business；新注册 business 立即补发当前快照。
- `IBusiness`：保存当前快照并提供统一 hook；各业务负责自己的参数收口、数据失效和 Property 更新。
- `TxSessionService/TxPipelineRuntime`：只消费已经带 capability revision 的执行 request，并做最后一道设备能力校验。
- UI：继续通过 PropertySystem value、基础 metadata、readOnly 和业务状态信号刷新，不直接读取 `DeviceManager::currentDevice()`。

### 5.2 建议的 Business 接口

`IBusiness` 增加非虚入口和可覆写 hook：

```cpp
void setDeviceCapabilities(const CurrentDeviceCapabilitySnapshot &snapshot);
const CurrentDeviceCapabilitySnapshot &deviceCapabilities() const;

protected:
virtual void applyDeviceCapabilities(const CurrentDeviceCapabilitySnapshot &snapshot);
```

非虚入口负责保存快照和去重，hook 只做业务本地处理。`BusinessManager::registerBusiness()` 在注册完成后立即调用一次，解决插件晚于设备连接注册时的状态缺失。

### 5.3 设备切换时序

```mermaid
sequenceDiagram
    participant DM as DeviceManager
    participant IO as Device I/O Thread
    participant FD as FancyDevice
    participant BM as BusinessManager
    participant B as All Businesses
    participant TX as TxSessionService
    participant UI as Property-bound UI

    DM->>BM: unavailable capability (new revision)
    BM->>TX: capability change begins; invalidate old request
    BM->>B: invalidate old capability/in-flight work
    DM-->>UI: currentDeviceOpenStateChanged(false)
    DM->>IO: close old / open new
    IO->>FD: open()
    FD->>FD: resolve model capability
    IO-->>DM: opened + requestId
    DM->>BM: publish final capability snapshot
    BM->>B: synchronous reconcile
    B-->>UI: property value/basic metadata/status changed
    BM-->>TX: all businesses reconciled; keep request gated
    DM->>BM: currentDeviceOpenStateChanged(true)
    BM->>B: start active business when capability is usable
    BM->>TX: capability ready
    TX->>TX: build fresh request with capability revision
    DM-->>UI: continue currentDeviceOpenStateChanged(true) delivery
```

实现时必须保证以下顺序：

1. 切走旧设备时先发布 unavailable revision，旧生成任务和旧 request 立即失效。
2. open 成功后先发布最终能力并同步完成 business reconcile。
3. `currentDeviceOpenStateChanged(true)` 到达 BusinessManager 后，先恢复当前 legacy business，再发 capability-ready 让 TxSession 构建新 request；runtime 不再提供 cached reapply。

不能依赖多个对象对 `currentDeviceOpenStateChanged(true)` 的偶然 connect 顺序。

## 6. Runtime 与异步任务的反陈旧

### 6.1 request 必须绑定设备能力代际

`TxApplyRequest` 至少增加：

- `deviceUid`
- `capabilityRevision`

`TxPipelineRuntime::requestApply()` 在执行配置和下载前校验：

- current device 仍 open；
- UID 与 request 一致；
- capability revision 一致；
- sample rate 属于 current Playback domain；
- payload 字节数为正、IQ 对齐且不超过 `maxWaveformBytes`。

任一条件失败都应拒绝执行并请求 `TxSessionService` 重建，而不是钳位后继续下发。

### 6.2 移除无条件 cached reapply

Phase B 之前，`TxPipelineRuntime` 在任何设备 open 时直接 `reapply()` 缓存 request；这可能把 1000 MiB/400 MHz request 重放到不含带宽选件的设备。

Phase B 当前实现是：

- 设备 open 触发能力发布和 business reconcile。
- `TxSessionService` 重新 snapshot 当前 selected business，生成带新 revision 的 request。
- runtime 不在 open signal 上无条件重放旧 request。

如果未来需要优化“同一设备短暂重连”，也只能在 UID、model、capability fingerprint 和 revision 策略明确一致时走受控快路径；首轮实现不需要这条优化。

### 6.3 波形生成任务也要带 revision

Digital/DSSS/OFDM/Ramp/ARB/Quick Waveform 等异步任务启动时记录：

- capability revision
- 参数 generation id
- 文件 source revision（文件业务）

结果回到主线程时若任一 revision 已变化，丢弃结果，不更新 `dataReady`，也不触发旧 payload apply。第三方算法不能取消时只需丢弃结果，不需要增加复杂中断机制。

## 7. 1000 MiB Playback payload 当前实现

### 7.1 已删除 request 按值模型

Phase C 之前的链路会在多个边界复制完整 IQ：

```text
generator/file reader
  -> std::vector<float/short> or QVector<float/int16>
  -> TxProviderExecutionContext QVector<int16_t>
  -> TxApplyRequest cached copy/equality
  -> TxPipelineRuntime
  -> tx_download_waveform(pointer)
```

Phase C 已删除 `TxProviderExecutionContext` 的按值 `QVector<int16_t>` 语义。当前 request/session/runtime 复制只复制 payload handle；request equality 比较 identity/view，不再扫描 IQ 内容。

### 7.2 共享不可变 payload、唯一大分配与外部 owner

当前 `Core::PlaybackPayload` 保存共享 storage、word view 和 payload identity。owned 路径通过 `PlaybackPayloadBuilder` 在唯一可写阶段精确分配一个 `QVector<int16_t>`，`publish()` 后只暴露 `const int16_t *`；外部生成器可通过 `adoptExternal(pointer, wordCount, deleter)` 移交所有权，不需要先复制进 Qt 容器。

当前内存硬约束是：

- 可以假设第一次 `1000 * 1024 * 1024` Bytes 连续分配成功。
- 超过 125 MiB 的 payload 必须先取得 Core 进程级独占租约；旧大 payload 未释放时，新大 payload 直接返回可恢复错误，绝不尝试第二个 1000 MiB 分配。
- 租约同时覆盖 Core-owned `QVector<int16_t>` 和 external owner/deleter 路径。
- `QVector` 只在 builder 唯一持有时写入；发布后禁止任何会触发 detach 的操作。

设备接口已收敛为 const 输入：

```cpp
downloadDataSequence(const int16_t *data, size_t samples, ...)
```

Core/runtime 全程只读；仅 HTRA adapter 为兼容 vendor API 的非 const 签名执行一次 `const_cast<int16_t *>`。

已确认 `tx_download_waveform()` 同步读取调用方 buffer，内部只申请小型分块传输内存，不会完整复制波形。runtime 因此在该 API 成功或失败返回后立即调用共享 storage 的 `releaseStorage()`；所有 business/request/cache handle 同时失去底层大内存，但仍保留 payload identity/count 供轻量比较。

设备端 waveform residency 与主机 storage 分离：同一 UID/revision/payload identity 后续只修改频率、功率、触发或采样率时，runtime 设置 `Profile::preservePlaybackWaveforms`，HTRA 不再执行 `tx_clear_waveform`，继续使用设备端已下载波形；出现新 payload 时仍按“清空 -> 下载 -> 触发”执行。

### 7.3 文件变换只物化一次（已实现）

`Utils::probePlaybackWav()` 当前为 Quick Waveform 与 ARB Ordinary/IQS 提供统一 header probe；`readPlaybackWavIqData()` 把普通 WAV 连续区间或 IQS-WAV 逐包有效区间直接写入最终 `int16_t` payload。当前规则是：

- AutoScale：第一遍找峰值，第二遍写目标 buffer。
- IQScale：直接一次写目标 buffer。
- SampleOffset/SamplesToUse/Period 补零：按最终 period 一次分配并写入。
- IQS-WAV packet trimming：按有效字节总数一次分配，再逐包写入有效部分。

禁止先建立同尺寸 `float` 副本再转回 `int16`。Quick Waveform 已删除完整 `QVector<float>`；ARB Ordinary/IQS 已删除原始 short、float handling list 和最终 short 三份同时驻留的路径。power metrics 直接从最终 buffer 计算。

ProgrammedArb 是多段序列且尚未接入正式 runtime，本阶段明确保留 legacy 125 MiB 解析/展开边界，不把它错误扩展到 1000 MiB；后续必须结合正式 sequence payload/runtime 模型单独迁移。

### 7.4 容量与 API 数值边界

含带宽选件档最大 complex samples 为：

$$
\frac{1000 \times 1024 \times 1024}{4} = 262144000
$$

该值能放入当前 `tx_download_waveform(..., uint32_t samples, ...)` 的点数参数，但实现仍应在转换前显式校验 `points <= UINT32_MAX`。

文件头探测、size 估算和容量判断必须使用 `quint64`。内存分配失败应转为可恢复业务错误，不允许异常穿过 worker 线程边界。

## 8. Business 收口与 UI 更新协议

### 8.1 一次 capability reconcile 的原子步骤

每个 business 在 `applyDeviceCapabilities()` 内按同一顺序处理：

1. 停止发布旧 `dataReady`，递增本地 generation id。
2. 更新内部 capability snapshot。
3. 计算收口后的单一 profile 和文件兼容状态。
4. 批量回写 Property value、基础 metadata、业务状态和 readOnly。
5. 只触发一次必要的异步重算。
6. 发出一次 `providerExecutionContextChanged()`。

需要本地 update guard，避免 Property 回写再次被当成用户编辑，造成递归或多次重算。

### 8.2 单一有效值与永久回写

Playback 参数保持单一有效值，不引入 requested/effective 双状态：

- 用户编辑后，business 按当前 capability 归一化并回写 Property。
- 设备切换后，business 再次按新 capability 归一化；这次回写永久覆盖旧值。
- 对直接采样率型业务，例如含带宽选件设备上的 400 MHz 切到无带宽选件设备后可回写为 125 MHz；即使之后换回含带宽选件设备，也保持 125 MHz。Digital 属于明确例外：组合参数越界时整组 reset 默认配置，而不是只把采样率钳到 125 MHz。

business 仍需要 update guard，避免 Property 回写再次被当成用户编辑造成递归或重复生成；该 guard 只控制通知，不保存被覆盖的旧意图。文件无法通过数值回写变小，超过当前容量时仍按 8.3 的 incompatible 语义保留路径并禁止 apply。

### 8.3 不能统一使用同一种“调整”

| 输入类型 | 设备切换后的策略 |
| --- | --- |
| 直接采样率落入 `200~400 MHz` 空洞 | 向下收口到 200 MHz；精确 400 MHz 保留 |
| 存在物理最低采样率的组合（如 FSK） | 先联动缩小相关参数，在连续低档内寻找合法解；除非用户明确请求，否则不自动跳到 400 MHz |
| sps/FFT 等枚举 | business 重新校验；当前值失效时优先回写较小合法项，不依赖 UI 禁用空洞选项 |
| duration/period/symbol length | 在业务已有明确 trim/动态上限语义时按当前容量缩小 |
| 生成波形超过容量 | 沿用该业务明确的提示/trim 语义，但阈值来自 capability |
| ARB Ordinary/IQS 已加载文件在设备切换后超过新容量 | 不静默截断；清空文件路径与文件参数、释放大 payload、关闭并禁用 Enabled，同时提示文件超出设备内存 |
| ARB 文件兼容、但 Period 补零后的最终 payload 超过新容量 | 保留文件；释放旧 payload、关闭 Enabled，将 Offset 复位为 0，Period/SamplesToUse 静默回写为当前设备安全加载默认值 |
| Digital 当前参数在新设备采样率域中失效 | 关闭 Enabled，整组静默 reset 到默认配置；不逐项钳位、不弹窗 |
| DSSS 当前参数在新设备采样率域中失效 | 与 Digital 一致：关闭 Enabled，整组静默 reset；参数仍合法时仅按新容量重选生成长度 |
| OFDM 当前采样率在新设备域中失效 | 关闭 Enabled，整组静默 reset；若仅预计大小超限，则优先降低 `symbolCount` 并永久回写 |
| PM 当前 Rate/PhaseDeviation 超过连续采样率能力 | 保留 PhaseDeviation，优先降低并永久回写 Rate；设备切换回写时关闭 Enabled |
| Multitone 当前组合超过设备能力 | 先把Count收口到产品范围2～1024并保证FreqSpacing至少1 kHz；之后保留Count，优先降低并永久回写FreqSpacing；只有1 kHz仍无法满足时才降低Count；设备切换回写时关闭Enabled |
| 其他文件型 Playback 超过当前设备容量 | 沿用各业务已明确的拒绝/保留语义；迁移时必须显式记录，不由 UI 猜测 |
| Streaming 采样率 | 只按 Streaming domain 收口，始终不读取 Playback 上限 |

设备切换后的 UI 提示采用单一例外策略：只有 ARB 已加载文件因新设备容量不足而被自动卸载时弹窗。Period、Digital 和其他业务的能力收口只做永久业务回写，不弹窗。ARB Ordinary/IQS 只有源文件本体超限时采用显式卸载语义，因此切换回兼容设备后需要用户重新选择文件；仅补零后的最终 payload 超限时保留文件，但复位值永久覆盖旧的大 Period。

### 8.4 PropertyMetadata 保持简单

`PropertyMetadata` 不承载设备采样率 domain，也不新增 `allowedContinuousRanges`、`allowedDiscreteValues` 一类空洞描述。它只表达简单且稳定的输入语义，例如：

- 采样率必须大于 0。
- 类型、单位、小数位等与设备型号无关的显示信息。

数值编辑器允许用户提交落入空洞或超出当前设备能力的正值。business setter 使用当前 capability 做最终归一化，优先选择较小合法值，并把归一化 value 永久回写给 Property；UI 只显示回写结果。

Digital/DSSS 的 sps 等离散参数也遵循同一边界：UI 不新增 model/domain 判断，当前值不合法时由 business 选择并回写合法项。若现有 `enumDisplayOptions` 仍用于展示，它也只能是 business 输出的辅助信息，不能取代 setter 校验。

## 9. 各 Playback 路径迁移清单

### 9.1 公共 Playback/runtime

涉及：

- `src/plugins/core/ibusiness.*`
- `src/plugins/core/iplaybackbusiness.*`
- `src/plugins/core/txpipelinestate.*`
- `src/plugins/core/txsessionservice.*`
- `src/plugins/core/txpipelineruntime.*`
- `src/plugins/core/devicemanager.*`

要求：

- capability snapshot 分发。
- request UID/revision。
- 共享 payload handle。
- runtime 最终 sample rate/bytes 校验。
- 最短 Playback payload 补齐后再次检查容量。

### 9.2 ARB Playback

涉及：

- `src/plugins/htra/arbmodulation.*`
- `src/plugins/htra/arbdatagenerator.*`
- `src/plugins/htra/arbpanel.*`
- `src/libs/utils/arbfileutils.*`
- `src/libs/utils/decoder.*`
- `src/libs/utils/iqswavreader.*`

要求：

- 文件选择时只做 header/chunk/有效字节探测，不立即整文件解码。
- Ordinary WAV、IQS-WAV 和 ProgrammedArb 的容量判断都显式传入当前 `maxWaveformBytes`，不再使用 utils 默认全局值。
- 文件采样率必须通过 current Playback domain；不能用连续 `clampSampleRate()` 把 250 MHz 当成合法扩展设备值。
- `Period` 上限来自 `maxComplexSamples()`。
- exactly-at-limit 文件合法，统一使用 `<=`。
- ProgrammedArb 当前仍未接入正式 runtime；本轮先迁移解析/估算接口，不能借机恢复 legacy 直配线程。

### 9.3 Quick Waveform

当前代码同时存在 plugin 侧 125 MiB 和 business 侧 100 MiB，且普通 WAV 会截取到固定阈值后继续加载。这条路径必须与 ARB 共用同一 capability 和 payload builder：

- 删除两份本地常量。
- 不静默把超容量文件当成截断成功。
- sample rate/period/file compatibility 与 ARB 使用同一规则。
- `QVector<float>` 全尺寸缓存改为 int16 immutable payload；float 只允许有界 preview。

### 9.4 Digital Modulation

当前已实现：

- 完成 `4e62cc8` 临时连续 400 MHz 逻辑的语义回退。
- Digital business 的采样率解析、FSK budget、SPS options 和容量裁剪显式消费 current `SampleRateDomain`；`DigitalModulator` 只消费设备无关 generation plan。`packing.cpp` 已拆分纯算法配置入口与 legacy 125 MHz 包装入口。
- 合作方授权 signal object 通过 business 注入的 factory 创建，三个第三方 generator 不直接依赖 `DeviceUtils` 或设备身份来源。
- 非 FSK 的 `Rb * sps` 只有 domain contains 时合法；空洞不得放行。
- FSK 的 `max(Rb*sps, 4*(Rb+MaxDF))` 先判断 domain；落入空洞时按现有业务优先级缩小 `Rb/MaxDF/sps` 到连续低档合法解，只有明确的 400 MHz 请求才使用孤立点。
- capability、symbol rate、FSK deviation 或 modulation type 变化后重新校验 oversample；设备切换导致当前整组参数失效时直接静默 reset 默认配置。
- Digital trimmed generation 的 symbol length 使用 current `maxWaveformBytes`，但 Save IQ 的完整波形目标不能被设备容量永久截断。
- 合作方 `GenerateDigitalModWaveform()` 返回的原始 `int16` 指针由 `PlaybackPayload::adoptExternal()` 接管，runtime 直接下发同一连续内存；不再复制到第二个 full-size `QVector<int16_t>`。
- Digital 在调用合作方生成 API 前先取得 `PlaybackPayloadReservation`；大 payload 租约冲突时不启动生成，避免“API 已分配第二份 1000 MiB 后才发现冲突”的时间窗口。

### 9.5 DSSS 与 OFDM

DSSS：

- `Rb * sps * (2^code - 1)` 用 current domain 判断；空洞输入优先回写连续低档，精确 400 MHz 才保留离散点。
- sps options 与 current domain 动态求交集，symbol length 上限使用 current `maxWaveformBytes`。
- 设备切换导致当前采样率失效时关闭 Enabled 并整组静默 reset；不弹窗。
- 生成前按预计字节取得 `PlaybackPayloadReservation`；`GenerateDssWaveform()` 返回的原始指针直接由 immutable payload 接管，下载后调用 `GenSignalObjRelease()`。
- Save IQ 直接读取现有 payload；host storage 已释放时，在同一 capacity/lease 门禁下重新生成并直接写文件，不建立 full-size 容器副本。
- `DsssModulation` 负责 domain/capacity、SPS options 与设备切换默认复位，并提交 sample rate/symbol length/layout；`DsssModulator` 不再包含能力类型。

OFDM：

- SampleRate property 使用非连续 domain；设备切换造成失效时整组静默 reset。
- `FFTSize / GuardInterval / symbolCount` 使用 `quint64` 保守估算；business 按 current capacity 优先降低并回写 `symbolCount`。
- 超容量组合不再弹出 trim 确认，也不进入 `GenerateOFDMWaveform()`；Playback 与 Save IQ 共用同一前置门禁，不生成或保存超容量完整波形。
- 合作方原始指针直接由 immutable payload 接管，生成前取得租约；下载后释放 external owner。保存需要重生成时也先检查容量并取得租约。
- `OfdmModulation` 负责 sample rate 与 `symbolCount` 收口并提交 layout；`OfdmModulator` 只校验 plan 与算法/第三方输出长度。

### 9.6 AM/FM/PM/Pulse/Ramp/AWGN/Multitone

HTRA AM/FM/PM/Pulse/Ramp/AWGN/Multitone 已完成迁移：

- AM/FM/PM 的周期格点搜索接收 current domain 和 capacity，并优先选择最小合法格点；PM 超过连续能力时保留 PhaseDeviation、回退 Rate，设备切换导致回写时关闭 Enabled。
- Ramp 的`Span/Period/SweepTime`、合法采样率和`maxPeriod(span)`从capability推导，已删除固定1秒上限；低Fs允许长Period，高Fs按payload自动缩短，320 MHz Span正确使用400 MHz单点，preview固定为小窗口。
- AWGN 按 domain 解析 `Bandwidth -> Fs`，空洞回写连续低档，Length 按 current capacity 收口；生成使用小型两遍流式状态。
- Multitone 的 Count/FreqSpacing、采样率格点和 sampleCount 使用 current capability；原组合无 plan 时先保留 Count、降低 FreqSpacing，只有1 kHz仍无法满足才降低Count。FFT 原地复用单工作区，非幂次路径只执行一次直接合成并保留一个完整时域工作区，不建立完整频域数组或重复合成整段波形。
- Pulse 已改为 Width 至少 6 点、Period 至少 8 点的动态 plan；无带宽选件档使用 `48 ns / 96 ns` 边界并在容量允许时优先 125 Msps，含带宽选件档连续能力为 `30/40 ns`、400 Msps 档为 `15/20 ns`。旧的 Period 8 倍点数和长周期 10 MHz 特例已删除，窄 Width 与超长 Period 冲突时保留 Width、按 current capacity 静默缩短 Period。
- 七类 execution context 和 Save IQ 直接消费 immutable payload；生成前通过 builder 取得租约，下载释放 storage 后保存可按同一门禁重生成一次。
- 设备能力决策已从算法层上移：Digital/DSSS/OFDM 与 HTRA 七类 `*Modulation` 根据 UI profile 和 current capability 解析、回写参数并提交精确 `WaveformGenerationPlan`；对应 `*Modulator` / `MultitoneGenerator` 不再引用或缓存 `PlaybackCapabilities`。

上述生成型 Playback 不再把 `STREAMING_SAMPLE_RATE_MAX` 当作优先上限；Streaming 62.5 MSPS 只服务 Streaming pipeline。

### 9.7 Step Sweep 边界

`packing.cpp` 中仍有 Step Sweep span 从 `DATA_SAMPLE_RATE_MAX` 推导的代码。迁移时先确认该 span 是 baseband Playback 约束还是 carrier sweep 约束：

- 若确实生成 Playback 组合波形，改用 Playback capability。
- 若它是纯 carrier 计划，则不应继续从 Playback sample rate 推导，需迁到对应 carrier capability。

## 10. 分阶段实施建议

### Phase A：能力基础（已实现）

- 新增 `devicecapabilities.*`；`SampleRateDomain` 的验收矩阵见 11.1。当前仓库没有 first-party test target，本轮按 static verification 未新建独立测试工程。
- 新增 HTRA model resolver。
- `FancyDevice::open()` 缓存物理能力，`IDevice` 暴露能力，DeviceManager 包装 current snapshot。
- 把 `STREAMING_SAMPLE_RATE_MAX` 从 `DATA_SAMPLE_RATE_MAX / 2` 派生关系中拆出，保持显式 62.5 MSPS。

`DIGITAL_SAMPLE_RATE_MAX` 连续 400 MHz 临时语义已在 Phase A 前单独回退。Phase A 只建立和发布能力，不同时迁移各波形业务，保持提交边界清晰。

### Phase B：Core 分发与 runtime 安全（已实现）

- DeviceManager/BusinessManager 按确定顺序分发 revision。
- `IBusiness` 增加 capability hook 和 late-registration 补发。
- request 增加 UID/revision，移除 open 后无条件 cached reapply。
- runtime 增加最后一道采样率/容量校验。

### Phase C：共享 payload 与文件 Playback

已实现：

- request payload 已改为 immutable identity handle/view，缓存不再保留或比较整段 IQ。
- 超过 125 MiB 的 payload 使用进程级独占租约，owned/external 两种 ownership 都保证同一时刻最多一个大 payload。
- Quick Waveform 与 ARB Ordinary/IQS 已统一 header probe、设备 capacity/domain 校验和一次物化路径。
- Quick Waveform 已删除完整 `QVector<float>`；ARB Ordinary/IQS 已删除多份 full-size 中间数据。
- ARB Ordinary/IQS 会保存已接受文件的有效 payload 字节数；热切换到较小容量设备后若超限，会原子卸载文件、清空文件参数并关闭播放使能。
- ARB 还会按最终补齐 payload 检查旧 Period；文件本身兼容但补零结果超限时保留文件、关闭使能并回写安全参数。
- SDK 同步下载返回后立即释放主机 storage；同一 payload 的设备端 residency 可用于参数-only reapply。
- ProgrammedArb 保持 legacy/未接 runtime 边界，不在本阶段扩展到 1000 MiB。

### Phase D：生成型 Playback 全量迁移

- Digital Modulation 已完成：sample-rate domain、SPS options、125/1000 MiB 生成上限、设备切换整组 reset 和 external payload adoption 已接入。
- DSSS/OFDM 已完成：current domain/capacity、静默参数收口、生成前租约、external payload adoption 和无 full-size 保存副本已接入；OFDM 超容量组合在业务层降低 `symbolCount`，不再先生成后 trim。
- HTRA AM/FM/PM/Pulse/Multitone/Ramp/AWGN 已完成：current domain/capacity、静默参数回写、唯一 builder/lease、immutable payload、无 full-size 保存副本均已接入。
- 七类本地算法只接收已解析 profile 与精确 generation plan，不再依赖设备能力类型；设备切换的 reset/回写/Enabled 策略统一属于 business。
- 每迁移一种业务，同时更新 `Waveform_Parameters_Constraints.md` 的当前行为，不提前把目标设计写成 active 行为。

### Phase E：热插拔和多型号验收

- 完成 capability generation token、文件 incompatible 状态和 UI 提示。
- 做 132 与每个扩展 model 的切换、拔插、同设备重连和配置恢复验证。
- 最后清理只剩 legacy/tool authoring 合理使用的固定常量。

## 11. 验收矩阵

### 11.1 Domain 单元测试

- 132：125 MHz 合法，125 MHz 以上非法。
- 含带宽选件档：200 MHz 合法，200 MHz 与 400 MHz 之间非法，400 MHz 合法，400 MHz 以上非法。
- `highestAtOrBelow(250M) == 200M`。
- `lowestAtOrAbove(250M) == 400M`。
- 400 MHz 附近只吸收预定浮点误差，不放宽成区间。

### 11.2 容量测试

- 132：恰好 125 MiB 合法，多 4 Bytes 非法。
- 含带宽选件档：恰好 1000 MiB 合法，多 4 Bytes 非法。
- period/sample count 换算始终保持 complex IQ 对齐。
- 最短 payload 补齐后仍不越过容量。

### 11.3 参数联动测试

- Digital 在含带宽选件设备上收到使 `Rb*sps` 落入空洞的输入时，business 回写较小合法组合；精确 400M 仅在明确请求时保留。
- FSK 最低需求落入空洞时优先联动缩小参数到连续低档合法解，而不是自动选择 400M。
- PM `Rate=10 MHz, PhaseDeviation=2π` 在无带宽选件档回写Rate为7.8125 MHz，在含带宽选件档保留10 MHz并使用160 MSPS。
- Multitone 132上的`Count=10/11, FreqSpacing=10 MHz`保持不变；`Count=12, FreqSpacing=10 MHz`优先回写FreqSpacing，隐藏tone不触发旧配置回滚。
- DSSS/OFDM/Ramp/AWGN 的 value、业务状态和归一化值随型号切换同步刷新；采样率 `PropertyMetadata` 仍只表达大于 0 等基础约束。
- Streaming 最大值在所有型号上保持 62.5M。

### 11.4 文件与内存测试

- 126 MiB 文件：含带宽选件档可准备，无带宽选件档标记 incompatible。
- 1000 MiB 文件：含带宽选件档可准备；1000 MiB + 4 Bytes 拒绝。
- 从含带宽选件档切到无带宽选件档：若 ARB 已加载文件本体超过 125 MiB，则自动卸载并只弹出一次容量提示；仅 Period 补零超限时保留文件并静默复位参数。
- request copy/equality 不复制或扫描完整 payload。
- 接近 1000 MiB 的路径没有同尺寸 float 副本，内存分配失败能正常报告。

### 11.5 热插拔时序测试

- 400M/大 payload 正在生成时切到 132，旧结果不得发布或下发。
- 扩展设备 open 后，business reconcile 完成前 runtime 不得 apply。
- 132 -> 扩展 -> 132 连续切换时，每次 request UID/revision 都与 current device 一致。
- 同设备断开重连也重新构建 request，不依赖旧 cached request 的偶然可用性。

## 12. 实现期间需要持续维护的文档

- 本文记录目标架构和迁移状态。
- [Waveform_Parameters_Constraints.md](Waveform_Parameters_Constraints.md) 只记录已落地 active 行为；每迁移一个业务再同步对应章节。
- [device_open_ui_config_flow.md](device_open_ui_config_flow.md) 在能力发布顺序落地后更新真实连接时序。
- [arb_mode_summary.md](arb_mode_summary.md) 在 ARB payload/file compatibility 重构后更新大小和 ownership 语义。
- [large_waveform_streaming_plan.md](large_waveform_streaming_plan.md) 在移除 Playback 对 Streaming 62.5M 的错误依赖后更新当前边界。
