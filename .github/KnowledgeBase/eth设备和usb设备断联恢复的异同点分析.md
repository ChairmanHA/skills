
## 结论

除"ETH 无发现源"外，USB / manual ETH / A-B 同机箱在**最终行为**上已经收敛（失联 → markConnectionLost → unregister → 异步销毁 → UI 投影同步 → current 空缺走 coordinator fallback）。但在**结构**上还剩 2 处真正会阻碍未来 API 整合的不一致，其余 3 处是可延后的。

---

### 影响最大的两处

#### 1. 失联判定的"发起权"被切到了 coordinator，且 coordinator 越权改 registry

- USB：判定发起权全部在模型层——`htradevicescanner.cpp:116` 每秒全量快照 → `devicemanager.cpp:1041` `onDevicesDiscovered()` 对**整个 registry（含 non-current）**对账；current 另有 `updateRealTimeStatus()` 的 `-8`。两条路都汇入 `unregisterDevice()`。
- ETH：只有 current 有周期判定（`devicemanager.cpp:1029` → `handleDeviceDisconnected()` → `unregisterManualEthGroup(ip, true)`）。non-current retained ETH 的判定被放在了 `deviceruntimeprofilecoordinator.cpp:257` 的切换流程里，并且失败分支直接调用 `deviceruntimeprofilecoordinator.cpp:375`。

问题：**"设备是否还活着"这个模型层事实，一半由 DeviceManager 拥有，一半由交互层的 coordinator 拥有。** 未来 ETH scanner 上线后，registry 对账会与 ping guard 产生双写竞争（scanner 说"还在"，ping 说"删掉整组"）。

建议现在就做的最小解耦：coordinator 只发 `switchPreflightFailed(target, reason=Unreachable)`，注销决策由 `DeviceManager` 承担。这一步几乎零成本，却把未来接入 ETH scanner 的改动面从"coordinator + DeviceManager 双改"缩到"只改 DeviceManager 的判定源"。

#### 2. `managedByScannerSnapshot()` 一个布尔量承担三重正交语义

`idevice.h:269` 的这个默认 `true` 的虚函数目前同时表示：

1. **是否参与快照对账**（`onDevicesDiscovered()` 里 `continue`）
2. **是不是 manual ETH**（`devicemanager.cpp:66` 和 coordinator 的 `deviceruntimeprofilecoordinator.cpp:50` 第一行都靠它取反）
3. **是否进入 auto-select / fallback 候选池**（`devicemanager.cpp:124`）

未来 ETH 有发现机制 → ETH 必须让 (1) 为 true 才能参与对账，于是 (2) **静默翻转**：ping guard、组注销、"ETH 不 retry-open"、"ETH 不享受 `-8` 宽限"四条策略同时失效；同时 (3) 会把 ETH 拉进 USB 的候选池，与 `preferredReconnectUsbKey` 语义直接冲突。

这是整个架构里**最可能因为一次改动引发连锁行为漂移**的点。建议拆成三个独立查询：`isAutoDiscovered()` / `transportKind()` / `isAutoSelectable()`，并把 `manualEthEndpoint()` 改用 `ethEndpoint()` 有效性判断，不再依赖发现方式。

---

### 可延后但需登记的三处

**3. 重连/回退策略按 transport 硬编码，键类型只有 USB UID（中）**

- `devicemanager.cpp:345` 对 ETH 强制置 0，但 `runtimeFallbackSelectionPending` 仍置 true → 当前语义是"ETH 掉线后系统自动接管一台 USB"，而 USB 掉线是"优先重连同一台"。这个不对称在 ETH 可发现后会成为明确的错误行为。
- 同一条策略在三处硬编码：retry-open 分支、轮询重启条件 `devicemanager.cpp:1272`、`-8` 宽限排除 `devicemanager.cpp:1220`。

建议：把 key 泛化为 opaque reconnect key（USB=UID，ETH=ip:port），三处判断统一替换为 `deviceHasDiscoverySource(device)`。ETH scanner 上线时只需让它返回 true，三条策略自动同构。

**4. 同机箱分组键：ETH 显式硬编码，USB 隐式（中低）**

`ip 相同 + port ∈ {5000,5001}` 散布在三处独立实现：`devicemanager.cpp:390`、`mainwindow.cpp:1273`、coordinator 的失败提示文案。USB 的同机箱两路是两个独立 UID，靠快照同时消失隐式一致。行为结果已一致，但常量重复三份。建议收敛为 `IDevice::chassisGroupKey()`。

**5. 句柄释放路径差异（低，无需改）**

USB 断联进 `DisconnectedPendingClose` 保留句柄待 `close()`；ETH 在 `updateRealTimeStatus()` 里同步 `device_close()`。这个差异有真实理由（USB 需重新枚举 dnum），不构成整合障碍。

---

### 一句话

**行为已一致；结构上只有"失联判定权被 coordinator 分走"和"`managedByScannerSnapshot()` 语义过载"这两点会在未来 ETH 发现机制落地时造成连锁改动，建议现在收敛。** 其余可等 API 明确后随之泛化。