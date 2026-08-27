# Streaming interface-aware sample-rate guard plan

## 目标

为 Streaming 增加“接口带宽不足以支撑当前采样率”的前置交互保护，覆盖两个用户入口：

1. 用户打开 Streaming Enabled 开关时。
2. Streaming 已经处于 Enabled 状态后，用户把 Sample Rate 改高到超出当前接口可持续范围时。

目标不是在底层发送失败后被动报错，而是在 UI 入口处做同步确认，避免用户在明知当前接口不满足实时流约束的情况下继续工作而得到错误频谱。

## 结论摘要

### 1. 触发策略

这次保护应当在以下两处都触发：

1. Streaming Enabled 从 Off 切到 On。
2. Streaming 已使能时，Sample Rate 编辑完成后进入更高且超限的值。

原因：

- 只在使能时提示，无法覆盖“先开低采样率，再把采样率调高”的同等错误场景。
- 这类问题从信号语义上属于硬错误前提，不是普通 warning；已使能后再改高，本质上同样把系统推进到非法工作区。

### 2. 最优雅的 interface 获取方式

对“仅 Streaming 自己需要知道当前接口”这个需求，最优雅且最小侵入的做法是：

- 直接在 StreamingPanel 内通过 `Core::DeviceManager::currentDevice()` 读取当前设备。
- 再通过 `device->getDeviceInfo()` 读取 `PhysicalInterface` 和 `BusSpeed`。

推荐理由：

1. 当前弹窗决策是 UI 同步交互，天然发生在 panel 层，而不是 sender/generator 线程层。
2. 仓库里 `DeviceManager::currentDevice()` 是现成的全局设备真相源，已经被多个 UI / controller / dialog 直接使用。
3. HTRA 设备 open 后已经把所需字段填好：
   - `PhysicalInterface = "USB" / "ETH"`
   - `BusSpeed = 3 或 2`（USB3 / USB2）
4. 这样不需要新增 Core 抽象、不需要在 `IBusiness` 或 `DeviceOperator` 上加“查询当前接口”接口，也不需要把接口状态再桥接成 property。

## 当前代码事实

### 1. 设备接口字段来源已经具备

HTRA 设备在 open 成功后会填充：

- `src/plugins/htra/fancydevice.cpp`
  - `m_deviceInfo.BusSpeed = ... ? 3 : 2`
  - `m_deviceInfo.PhysicalInterface = ETH 或 USB`

也就是说，Streaming 不需要自己识别 USB2 / USB3 / ETH，只需要消费已经存在的 `DeviceInfo`。

### 2. Enabled 开关的真实控制点在 panel 信号

当前 StreamingPanel 直接把：

- `SwitchButton::statusChanged`
- 连接到 `Panel::enabledChanged`

上层再据此选择业务并触发 `startBusiness()` / `stopBusiness()`。

因此如果要“用户取消则不使能”，最佳落点是在 panel 内拦截 `statusChanged(true)`，而不是等 business 已经启动后再回滚。

### 3. 设备切换时必须刷新 interface

这个判断不能只在构造时缓存一次，因为用户切换设备后，Streaming 看到的接口类型可能从 USB3 / USB2 变成 ETH，或者反过来。

推荐监听 `Core::DeviceManager::currentDeviceOpenStateChanged(bool)` 作为主刷新点：

- `true` 时，读取当前 `currentDevice()->getDeviceInfo()`，刷新 Streaming 的当前接口缓存。
- `false` 时，清空或失效当前接口缓存，避免在设备关闭后还沿用旧接口结论。

如果还想更快地清掉旧状态，可以辅以 `currentDeviceChanged(IDevice *)` 做“先失效、后在 open 成功时再恢复”的双阶段刷新，但真正决定可否放行的仍应以 `currentDeviceOpenStateChanged(true)` 为准。

这里的信号名应写成仓库里的真实名字 `currentDeviceOpenStateChanged`，不是别名或口误。

### 3. Sample Rate 编辑完成目前走 business 槽

当前链路：

- panel 中编辑 `Streaming_SampleRate`
- property `editingFinished`
- `StreamingBussiness::onSampleRateEditingFinished()`
- clamp + 回写 property + 更新 generator + 置 `m_profileChanged`

如果要在“已使能后改高采样率”时也弹窗，第一版最稳的做法是把“是否需要确认”放在 panel 对 property 的 `editingFinished` 响应里先做，只有确认通过再让现有 business 槽继续执行已有重配语义。

## 推荐架构

### 核心原则

- 交互判断留在 StreamingPanel。
- 线程/设备配置逻辑继续留在 StreamingBussiness。
- 接口识别只依赖 `DeviceManager::currentDevice()->getDeviceInfo()`。
- 超限阈值表只在 Streaming 局部收敛，不扩散到全局常量层。

### 推荐新增的局部 helper

建议在 `streamingpanel.cpp` 的匿名命名空间内新增局部 helper，而不是改公共头：

1. `StreamingTransportKind classifyStreamingTransport(const Core::DeviceInfo &info)`
2. `double recommendedStreamingSampleRateLimit(const Core::DeviceInfo &info)`
3. `QString describeStreamingTransport(const Core::DeviceInfo &info)`
4. `bool sampleRateExceedsCurrentTransportLimit(double sampleRate, const Core::DeviceInfo &info)`

局部阈值建议：

- USB3.0: 62.5e6
- USB2.0: 10e6
- ETH: 15e6

这样做的好处：

- 规则只服务 Streaming。
- 后续如果经验阈值调整，改一处即可。
- 不污染 `utils/constants.*` 里那个“Streaming 通用理论上限 62.5M”语义。

## 详细实施步骤

## Step 1. 在 TaskLog 之外确认文案和产品语义

先固定产品语义：

- 这是“强警告确认”，不是普通 warning。
- 只要用户继续，就允许进入当前配置，不再做第二层硬拦截。
- 默认推荐动作应偏向取消。

推荐文案结构：

- 第一行：当前接口类型。
- 第二行：当前采样率与建议上限。
- 第三行：风险后果，明确会导致 waveform stream / spectrum abnormal。
- 第四行：询问是否继续。

示例：

"Current interface is USB2.0.Configured sample rate 20 Msps exceeds the recommended streaming limit 10 Msps.Waveform stream may not work normally.\nDo you want to continue?"

## Step 2. 改 StreamingPanel 开关入口，替代当前直连

文件：

- `src/plugins/htra/streamingpanel.h`
- `src/plugins/htra/streamingpanel.cpp`

改法：

1. 新增槽：`void onEnabledChanged(bool enabled);`
2. 把当前 `ui->enabled -> enabledChanged` 的直连改成连到这个槽。
3. 在槽中：
   - `enabled == false` 时，直接 `emit enabledChanged(false);`
   - `enabled == true` 时：
     - 读取当前 sample rate property。
     - 读取当前设备 interface。
     - 如果不超限，直接 `emit enabledChanged(true);`
     - 如果超限，弹同步两按钮对话框：
       - 确定：`emit enabledChanged(true);`
       - 取消：`setBtnEnabledChecked(false); return;`

这样能保证：

- 取消时 business 根本不会启动。
- 不需要在 `StreamingBussiness::startBusiness()` 里再做回滚。

## Step 3. 给 StreamingPanel 增加获取当前设备 interface 的私有 helper

文件：

- `src/plugins/htra/streamingpanel.h`
- `src/plugins/htra/streamingpanel.cpp`

1. `bool shouldConfirmSampleRate(double sampleRate, QString *message) const;`

推荐实现语义：

### `shouldConfirmSampleRate(...)`

- 若没有当前设备，返回 false。
- 若 `PhysicalInterface == "USB" && BusSpeed >= 3`，上限用 62.5M。
- 若 `PhysicalInterface == "USB" && BusSpeed < 3`，上限用 10M。
- 若 `PhysicalInterface == "ETH"`，上限用 15M。
- 若接口未知，保守起见第一版返回 false，不拦截。
message 输出格式参考前面文案结构建议。

## Step 4. 对 MessageDialog 采用同步 execMessage

文件：

- `src/plugins/htra/streamingpanel.cpp`

理由：

- 当前需求是“本次交互立即决定是否继续使能/是否接受新采样率”。
- 这是 MessageDialog 的同步接口最适合的场景。
- 如果用异步 `showMessage()`，需要把后续逻辑拆成回调，反而让 panel 的状态回滚更绕。

按钮建议：

- `QMessageBox::Yes | QMessageBox::Cancel`

语义建议：

- `Yes` 表示“继续当前高风险配置”
- `Cancel` 表示“撤销本次动作”


## Step 5. 在 panel 层补上“已使能后改高采样率”确认

文件：

- `src/plugins/htra/streamingpanel.cpp`

当前已经有：

- `connect(sampleRateProperty, &IProperty::editingFinished, this, &StreamingPanel::onSampleRateChanged);`

第一版建议把这个连接改为一个新槽，例如：

- `onSampleRateEditingFinished()`

该槽内部顺序建议：

1. 先读取 property 当前值。
2. 判断当前 `ui->enabled->status()` 是否为 true。
3. 若当前未使能：
   - 直接走 `onSampleRateChanged()` 现有总时长刷新逻辑。
   - 不弹窗。
4. 若当前已使能且不超限：
   - 直接走 `onSampleRateChanged()`。
5. 若当前已使能且超限：
   - 弹同步确认。
   - 用户确认：继续，随后让 business 槽按原逻辑重配。
   - 用户取消：把 property 值恢复到上一次已接受值，再刷新总时长显示。


## Step 6. 验证步骤

本次如果只输出方案，不需要编译；真正实施后建议按下面顺序验证：

### 静态检查

1. 确认 panel 新增 signal/slot 后头源声明一致。
2. 确认不存在 old direct connection 和 new accepted signal 双重生效。
3. 确认取消时不会留下 UI 开关/采样率显示与 property 不一致。

### 手工行为验证

建议至少覆盖以下矩阵：

1. USB3 + 62.5M 以内：
   - 使能不弹。
   - 已使能后改高但仍 <= 62.5M，不弹。
2. USB2 + 10M 以上：
   - 使能时弹。
   - 取消后不开启。
   - 确认后开启。
3. ETH + 15M 以上：
   - 使能时弹。
   - 已使能后从 10M 改到 20M 时弹。
   - 取消后恢复到旧采样率。
4. 无当前设备 / 设备未 open：
   - 不应崩溃。
   - 第一版可不弹接口限制框。
5. 切换设备后：
   - panel 重新读 currentDevice，阈值跟随设备接口变化。

## 关键风险

### 风险 1：property 编辑完成顺序和重入

这是本次真正的技术风险点，不是 interface 获取本身。

如果保留 `sampleRateProperty->editingFinished -> business` 的旧直连，就可能在 panel 还没拿到用户确认前 business 已经开始重配。

所以本次必须把 sample rate 的“确认”放到 business 重配之前，而不是之后补救。

### 风险 2：取消时的 UI/属性不同步

如果只把按钮或文本改回去，但没把 property 真值恢复，后续 duration 显示和 generator 配置会跑偏。

所以“取消改高采样率”一定要回滚 property 真值，而不只是回滚界面标签。

### 风险 3：把局部规则做成全局公共抽象

这次需求只属于 Streaming，不要顺手把“接口能力限制”抽到全局 capability 体系里。那会把一个局部交互任务扩成架构任务。

## 最终推荐落点

### interface 获取

首选：

- `StreamingPanel`
- 直接 `Core::DeviceManager::currentDevice()->getDeviceInfo()`

### 超限判定 owner

首选：

- `StreamingPanel`

### 设备重配 owner

保持：

- `StreamingBussiness`

### 需要改的文件

最小集合：

1. `src/plugins/htra/streamingpanel.h`
2. `src/plugins/htra/streamingpanel.cpp`
3. `src/plugins/htra/streamingbussiness.h`
4. `src/plugins/htra/streamingbussiness.cpp`

## 推荐实施顺序

1. 先给 StreamingPanel 增加 DeviceManager 信号监听与 interface 缓存刷新。
2. 再改 panel 的 Enabled 拦截并验证取消回滚。
3. 然后改 sample rate 确认链路，把 business 的 direct editingFinished 连接切掉，改成 panel 确认后转发。
4. 最后统一文案和 helper，做一轮静态检查。

这样做能保证每一步都是局部闭环，不会一上来同时改 UI、property、business 三条链路而难以定位问题。