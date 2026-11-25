# H2 实时状态查询与活动 warning 重构方案

## 2026-11-13 周期观测保留频率编辑值

- `TxSessionService::onDeviceRealTimeStatusUpdated()` 在参考源未变时保留已配置频率，源和 RefOut 均未变化则跳过 runtime / CommonDeviceProfile 回写；仅 RefOut 变化时仍同步开关。正常 External 的周期频率变化不再覆盖频率属性或已打开的软键盘。
- 实际切源仍沿用完整同步，失锁回退 Internal 的有效频率及外参考频率记忆保持原语义。原始设备状态快照、SDK 输出、告警/锁定位、轮询和恢复逻辑不变；配置完成及页面显式查询继续完整回写。
- 本节取代下文“每轮频率变化同步 runtime/UI”的规则。仅静态核对，未构建/设备验收；见[TaskLog](../TaskLog/2026-11-13_reference-clock-polling-input-writeback.md)。

## 2026-11-09 API 告警码与锁定位独立

按用户要求，程序忠实保留 SDK 的告警码与输出状态，不根据输出字段合成、替换或撤销 SDK 告警。本节取代下文历史实现中通过锁定位增删 SYSCLK 15 的规则。

- `device_query_clock` 的 `queryStatus` 是告警来源；`is_locked_o` 独立回写 `ReferenceClockState::locked`。18 + locked=0 只记录实际返回的18；0 + locked=0 不补报15；15 + locked=1 仍保留实际返回的15。
- 删除 `locked=false` 时补入15，以及 `locked=true` 时跨来源移除15的推导。每个来源返回0时只清自己的活动项，负错误/未采样保留既有未更新语义。
- 锁定位仍参与已明确要求的外参考恢复调度；500 ms×10次确认、SDK调用顺序、RefOut、低功耗及Streaming边界不变。
- 静态审查其他H2调用的告警来源，未发现其他根据输出字段合成SDK warning的路径；未构建或实机验证。修复及审查记录见[TaskLog](../TaskLog/2026-11-09_preserve-h2-api-warning-status.md)。

## 2026-10-30 当前参考时钟确认规则

同日展示调整：移除滚动 warning 的 `(status not updated)` 后缀和 RLO 同义 tooltip；内部 Confirmed/NotUpdated 状态、采样/清警和确认轮询不变。下文历史的“未更新展示”以此调整为准。

本节取代下文历史方案中的“100 ms 门限、下一状态周期先切 Internal”规则；仅静态实施，未构建或实机验证。

- 原位置的即时 clock 查询得到 External + 15 且未锁定后开始确认，即时查询不计次数。原 I/O worker 下增加 500 ms 精确定时检查，FancyDevice 每次执行一个查询，十次均为 External + 15 且未锁才单次回退 Internal；任意有效锁定立即结束等待。首次失锁后至少约 5 秒才可回退，SDK/worker 忙会延后，不补发查询。
- 普通状态、温度/供电/GNSS 和 UI 发布仍为原 1000 ms；确认期间普通状态周期不重复查询 clock。两周期之间的 clock 观测缓存到下一状态快照，保持 warning 的实际确认状态；告警按SDK返回码更新，锁定位不增删告警，不新增 UI 清警计时器。
- DeviceManager 的全局原子挂起状态跟随 TxSessionService::setUpdatesSuspended，加载配置期间禁止旧配置重新启动确认；挂起和恢复两端都使当前设备参考时钟代次失效。停状态轮询、解绑当前设备及完整时钟配置也使旧代次失效。设备切换保留旧设备打开亦适用。
- IDevice 的时钟代次只负责失效；FancyDevice 持有实际时钟配置代次及计数。查询后和回退前重新核对代次与挂起状态，排队的时钟观测也带代次，由 TxSessionService 在 UID/capability revision 过滤之后核对。同设备旧结果不能回写新配置。
- 完整公共配置沿用原先重发时钟并重启等待的行为；FFM-only、RefOut SDK 行为不改。查询错误不算失锁，不据此回退；负错误及时发布，-8进入原断联处理；回退失败不重复写 Internal。低功耗不被唤醒。
- Streaming 仅等待确认期间增加有界 clock 查询，结束后仍无常态周期 clock 查询；与 sender 共用原 SDK 锁，吞吐/欠载影响待设备验证。已进入 SDK 的单次调用不能抢占，新配置按既有串行顺序执行。
- `[RefClock]` 日志记录确认开始、每次确认查询、失效、取消、锁定结束、错误和单次 Internal 回退结果。显式查询、配置回读和回退后回读继续记录完整结果。2026-11-18 起，常态周期查询仅在返回负错误时写入详细查询日志。确认查询的 attempt 为 1..10，其余查询为 0。
- GNSS 日志记录会话首个有效锁定状态、锁定位变化、查询失败码变化和查询恢复。卫星数量和时间字段的变化继续发布到实时状态。

实施与人工验收清单见 [TaskLog](../TaskLog/2026-10-30_external-reference-lock-grace-design.md)。下文保留原活动 warning 架构及历史实施依据。

日期：2026-10-20；2026-10-21已实施，并按用户补充要求将时钟恢复简化为延迟单次配置、普通查询自然清警。仅静态检查，尚未编译或调用设备；实际落点和验收限制见第9节。

2026-10-28 展示规则修订：用户要求全部失锁增加滚动 warning。15–19 统一归为 LockLoss，复用原活动消息差异维护并接收本次一次失锁；15 源文案改为系统时钟失锁。随后按现场反馈，RLO 改为跟随当前失锁文案轮播，支持 15–19，普通 warning 时隐藏。详细文案与联动规则见[设备状态UI反馈](device_status_ui_feedback.md#511-全部失锁-warning-与参考时钟恢复)。本次不改变 SDK 查询或自动恢复，仍仅静态核对。

目标是保留当前专有机制，增量支持多个持续告警和观测未更新状态。

## 1. 本次确定的范围

- 不新增 tx_query_fscan/lscan/mscan 周期调用；当前配置后及显式扫描回读继续保留。
- 不新增周期 tx_query_ffm；保留当前FFM配置、回读、缓存、FFM-only及输出重新arm顺序。
- device_query_clock获得External+15后保留告警，恢复等待门限为100ms；既有状态线程在下一合法周期先单次切Internal，再普通查询自然清警，不在查询助手内立即配置并二次确认。
- 保留UnLevel的现有状态所有者和Level局部提示；不把99提升为新的statusbar滚动提示，不改变其清除条件。
- 新增活动告警集合，解决单个warnningCode覆盖多个异常、以及无法表达未更新的问题。
- 现有SDK、工作线程、UID过滤、关闭/重连、低功耗和Streaming边界继续沿用；不创建全局warning manager，不增加阻塞等待、盲重试或为了采样而唤醒设备。仅外参考失锁按用户要求延迟恢复。

依据：当前CMake纳入的FancyDevice、IDevice、DeviceManager、MainWindowDeviceController、DeviceInfoWidget、MainWindow、CommonDeviceProfile、TxPipelineExecutor/Runtime；当前h2_api.h。现有行为见 [参考时钟接入](htra_reference_clock_integration.md)、[设备状态UI反馈](device_status_ui_feedback.md)、[Streaming数据流](streaming_dataflow.md)。

## 2. 本次保留和新增的 query

沿用现有约1000 ms状态调度。周期硬件采样只在原本允许访问SDK的场景执行，不能用新容器改变硬件访问权限。

| 查询/路径 | 本次方案 | 保持的语义 |
| --- | --- | --- |
| device_query_temperature | 保留现有周期调用 | 温度、现有通信/断联处理；不拿其返回0清时钟告警 |
| device_query_power_supply(RF_RAIL/USB_RAIL) | 保留现有周期调用 | 两个电源轨独立采集，保留现有显示与错误边界 |
| device_query_gnss_data、吞吐率更新 | 保留现有路径与条件 | 不为了告警容器重构扩大GNSS或Streaming访问 |
| device_query_clock | 非Streaming等当前允许的采样场景增量加入周期检测 | 查询维护活动告警；External+15安排延迟单次Internal配置，后续普通查询提供结果 |
| tx_query_ffm | 保留当前配置前/后、FFM-only和其他已有调用点 | 不新增每秒调用；UnLevel处理继续由已有机制负责 |
| tx_query_fscan/lscan/mscan | 不增加周期调用 | 现有配置后/显式回读保留，扫描参数查询不被当作本次健康轮询 |
| device_query_power_state及LO/输出/stream/trigger/port等query | 保留已有按需调用 | 本次不新增每轮电源探测或其余硬件查询 |

不再为轮询新增CarrierPlanKind字段、扫描参数缓存或大MScan查询缓冲区。使用已有电源控制/低功耗状态约束时钟采样；若当前状态不能证明查询合法，保留原观测并标未更新，不猜正常也不强制上电。

现有配置后时钟查询位置保持不变。周期时钟采样在原有状态采样流程基础上增加，不穿插进未完成的配置事务。必须避免新增clock成功覆盖本轮已有通信错误；新增查询失败按现有错误契约发布，任一-8终止后续访问并进入既有断联处理。

## 3. 必须保持的参考时钟行为

所有入口共用FancyDevice的queryReferenceClockLocked采样规则。持有m_mutex的状态更新路径调用Locked入口，不能再次调用会加锁的public queryReferenceClock。恢复配置放在updateRealTimeStatus，不在查询助手中执行。

处理顺序为：

1. device_query_clock，取得返回码、源、频率、RefOut、锁定位。
2. 命中“读回源为External且返回H2_WARNING_SYSCLK_UNLOCKED(15)”时，15进入普通活动集合和原warnningCode；启动设备内单调计时。查询返回实际External，不提前伪装为Internal。
3. 延迟门限为100ms，updateRealTimeStatus在已过门限的下一合法SDK访问周期先单次device_config_clock(REF_INT,100 MHz,当前RefOut)，使既有Playback apply snapshot失效。复用约1秒状态周期，不是100ms主动回调；不再因1000ms门限而多等一轮。Streaming期间状态线程不做周期查询，但到期的单次恢复仍在同一状态周期执行（见第7节）；低功耗或查询错误会延后，不阻塞等待。
4. 配置成功后继续原本这一轮的普通clock查询，不增加专门确认查询。普通查询维护活动集合，返回0时清除Clock来源的活动项。配置失败保留错误和未更新告警，后续轮询不重复配置。
5. 发布普通查询得到的实际时钟，通过已有结果链路同步UI/Profile及运行时；同值不重复发布应用变化。不扩展为任意warning或任意locked=0都自动切源。

没有独立历史失锁事件、恢复成功提示或用于手工清警的定时器。延迟状态只控制一次硬件动作；报警持续多久由后续普通查询决定。

- 恢复且普通查询正常：活动集合不保留过期15，状态栏按原码/活动集合差异自然清除，不新增“已切回内部”事件。
- 已改为Internal后继续保留查询实际返回的告警与独立锁定位；不根据未锁状态补报SDK未返回的码，也不增加第二轮恢复。
- 配置/查询失败：沿用失败返回及错误提示，同时保留最后已确认异常并标明未更新；不能用一次短暂提示代替未解决状态。
- Internal自身出现15：属于当前系统时钟异常，不满足External恢复条件，不反复配置Internal。
- 再次轮询Internal正常不重复生成15；用户再次主动配置External是新的请求。新clock配置、已离开External或有效锁定会取消旧待执行动作，查询本身不重置单次尝试。

周期恢复新增了一条结果发布入口，但不改变现有同步规则：设备缓存、CommonDeviceProfile参考源、RefOut和运行时已应用clock必须一致，用户外参考频率记忆保留。CommonDeviceProfile::setReferenceClock及现有配置完成回写语义继续复用，不能用一个默认/不完整Profile覆盖其他参数。

周期普通查询成功得到的ReferenceClockState连同现有设备身份从I/O结果链路交给UI/运行时适配，仅在真实有效且值变化时同步。失败不发布伪Internal；本轮查询失败时，后续成功查询仍可同步实际Internal。切换设备后的结果按既有身份过滤。恢复同步不能自动重发整条配置，也不能让下一次请求又把已回退的源改回External；同时不能覆盖更新中的用户请求。

## 4. UnLevel保持现状

当前configureFfmIfNeededLocked/applyPlaybackFfmOnlyLocked根据FFM配置结果维护m_txLevelUnlevelActive，MainWindow通过txLevelUnlevelActive更新CommonPanel Level的UnLevel提示。本版完整保留该规则、调用顺序、配置回读与FFM-only重新arm过程。

- 不新增FFM周期查询，不自动降低Level、不修改中心频率或RF端口。
- 不把一个普通query返回0当作新增的UnLevel清除条件，不移动或重定义已有状态所有者。
- 99继续遵循现有专有分类与局部显示，不进入新的通用statusbar滚动队列。
- 活动集合支持未来携带99，但本期不要求将99迁入其中。若未来需要统一快照，由已有UnLevel状态投影，不能另建独立99持久状态并形成两套更新/清除逻辑。

普通query接入集合，不意味着原有专有告警都必须迁移成通用滚动提示。

## 5. 活动告警集合

DeviceRealTimeStatus增加活动告警集合。每个元素至少包含：

| 字段 | 含义 |
| --- | --- |
| warningCode | 告警码；容器可同时携带多个不同码，不取“最后一个”作为全部状态 |
| confirmation | 当前确认；或上次确认存在、但本轮未得到有效更新 |

来源及每个来源的更新结果由FancyDevice在内部持有。字段属于设备API观测边界，公共UI接收合并快照，不复制硬件判断或恢复规则。集合从真实的持续告警状态生成，不能由旧warnningCode派生，也不将所有正数返回一律永久锁存。

更新规则：

1. 当前来源明确报告持续warning，建立/更新对应活动项。
2. 对应来源提供明确恢复证据后移除该项；温度/供电正常不能清除clock项。
3. 查询失败、跳过或没有有效结果：保留旧活动项，confirmation标为本轮未更新，不能悄悄清除或伪造新的当前确认。
4. 某次返回另一个warning，不足以证明旧warning已恢复；沿用现有保留规则，不假定SDK优先级，也不根据独立输出字段合成或清除告警。
5. 同一码被多个来源支持时合并去重，一个来源恢复不抹去其他仍异常/未确认来源。
6. getRealTimeStatus读取不消费活动项，读后清零仅继续用于原有短暂提示。
7. 明确不适用和会话结束按既有生命周期处理；不会因周期数到了而自动恢复告警。

集合为空只代表当前没有可展示的已知活动项，不宣称SDK全部异常都已被检查。本次不新增另一套全局健康评分或假定缺省正常的状态。

## 6. 现有提示与活动集合共存

保留warnningCode的现有短暂提示语义，尤其保护原始15不被后续普通warning覆盖的行为。不再为15增加另一份历史事件字段。它与活动集合分工不同：

- 活动集合保存尚未恢复的持续异常、确认状态，以及当前 Streaming 会话记录的发送超时。
- 原warnningCode负责既有一次提示；15–19 文案由原码或同码活动项维持，状态快照不再含该码时清除。

UI对通用活动项按集合差异维护队列：新增加入、仍存在保持、确认移除才删除；不每秒重复入队，不清空其他业务消息。同码活动项和短暂提示的文案相同时由活动项负责显示，Streaming 发送超时使用单独文案。

展示准入由设备层 warningCategory 统一定义：General 保留普通一次/活动提示，LockLoss（15–19）进入随状态快照维护的滚动提示，Ignorable（98/99）不进入队列。LockLoss 本次码与活动项同码时优先活动项，无活动项的一次失锁只加入本轮受管消息集合，避免恢复后永久轮播。文案统一来自 FancyDevice::translateShortStrOfWarningCode，不再保留 UI 固定外参考文案或仅针对 15 的额外布尔状态。controller 将失锁 code/Confirmation 随文案入队，RLO 小指示从当前轮播项派生并支持 15–19；普通文案、空队列或断开时隐藏，不再有独立原码选取。UnLevel 局部机制保持。

配置和其他操作的 42、44 及 10 沿用普通一次提示。编辑态 preview 的 98 归一化继续由 preview 处理。

Streaming 发送返回 `+10` 时写入独立活动来源，状态轮询和后续一块发送成功都保留它。发送线程确认停流后，业务通知设备清除该来源，主窗口随下一份快照移除文案。发送返回值、日志及首块失败的 SCPI 详细原因沿用现有处理。[实施记录](../TaskLog/2026-11-25_streaming-timeout-warning-lifetime.md)

未来新增持续warning，只需在设备层接入其来源、有效结果及恢复判据，复用活动集合与按分类展示的流程。容器本身不硬编码“只支持15”，不增加未需要的SDK配置、轮询入口或通用恢复策略。

## 7. 并发、生命周期和覆盖限制

沿用DeviceStatusUpdateThread和已有SDK互斥/设备生命周期。SDK采样和恢复持有现有锁；集合/快照跨线程读取需在短临界区内一致复制。若新增缓存锁，只保护状态读写，不包裹SDK I/O，顺序为SDK锁→缓存锁，禁止反向持锁。发送线程通过同一设备状态所有者报告warning，不再对新集合做无锁并发读写。

Streaming当前有独立发送线程并跳过周期硬件query。本次不因为扩充集合就强行在Streaming期间新增clock、FFM或scan SDK调用，也不暂停/重启发送来查询。配置/显式查询及sender的既有warning路径仍保留；未更新活动项如实标记。若未来希望增加Streaming时钟监测与同一自动恢复规则，应另行验证SDK访问、发送停顿、吞吐、欠载以及clock切换对会话的影响，不能默默改变现有会话行为。

例外（2026-10-21修订）：Streaming配置读回得到External+15时，恢复计时同样启动。updateRealTimeStatus的Streaming分支在持m_mutex、m_errorCode为0（sender无粘滞错误）且计时已过100ms时，调用与非Streaming共用的sampleReferenceClockLocked：单次切Internal后普通查询并发布referenceClock。无到期动作时Streaming仍不访问SDK；运行中外参考丢失仍无法检测。恢复失败写入m_errorCode会停止当前流，与此前在Streaming配置内恢复失败的结果一致。setReferenceClock不发profileChanged，不会触发Streaming重配。数据已开始发送后切时钟对流的影响需设备验证。见[TaskLog](../TaskLog/2026-10-21_streaming-external-ref-recovery.md)。

低功耗不被采样唤醒，原有电源策略不变。设备切换/关闭/重连按既有UID与生命周期清理，旧活动项不污染新设备；不增加持有裸指针的后台任务。

头文件未公开16–19等所有warning的查询映射、优先级或输出有效性。新集合能够同时保存应用已观察到的多个warning，不等于SDK已能一次返回全部warning；本期不把补充全部16–19查询入口、扫描或Streaming全覆盖列为上线承诺。

## 8. 实施边界与验收

预计实现重点：FancyDevice（活动观测、非Streaming合法场景clock查询/恢复接入）、IDevice状态快照（集合与确认状态）、状态发布/消费路径（集合快照与专有行为保持），以及周期恢复的时钟结果同步接缝。MainWindow的UnLevel业务规则、FFM/扫描配置流程、SDK、SCPI、Minibar协议均不重构。不新增载波计划字段或全局warning管理器。

验收要求：

- 无外参考选择External：仍得到原15、单次切Internal、保留RefOut和外参考频率记忆；原提示仍约1秒后清除。
- 合法周期监测场景下External运行中失锁：复用同一恢复；硬件、UI/Profile和运行时clock同步；不反复切换、不重发整条配置。
- 已切Internal但API仍返回warning、或恢复失败：保留真实活动异常/未更新状态，不能因一次提示消费消失；未锁字段本身不生成告警。
- 后续正常轮询不重复生成旧15；用户重新选External可再次尝试。
- UnLevel出现、清除、Level局部展示和FFM-only行为与修改前一致；没有新增99滚动提示或周期FFM。
- 至少两个独立的普通持续warning同时存在、分别恢复/更新失败：互不覆盖；本轮未更新可区分；短暂15不吞掉其他活动项。
- 一个query返回0不清其他来源；活动项读后不清零；周期clock结果不覆盖本轮错误；任一-8沿用断联处理。
- 三个scan query仅保留原调用点；查询调用计数证明未新增周期扫描/FFM/power_state/LO/output查询。
- Streaming、低功耗、切换设备及关闭保持既有行为；未采样不伪装已检查，也不为了新容器新增等待、重试或日志时延。

共享状态结构的完整Debug构建与上述设备回归仍需执行，不能将静态检查视为硬件验收。

## 9. 2026-10-21实施落点

- Core::DeviceWarning通过code/Confirmation（Confirmed、NotUpdated）表达活动项；DeviceRealTimeStatus新增activeWarnings，比较与Qt metatype一并更新。ReferenceClockState作为Core值类型复用，IDevice::ReferenceClockState保留别名。
- FancyDevice按Clock、Temperature、RfSupply、UsbSupply、Gnss保存观测，每轮先标未更新，各来源独立报告/清除，快照按码合并；任一来源当前确认则该码为Confirmed，仅剩旧观测时为NotUpdated。某来源0不抹去其他来源的贡献；2026-11-09已删除通过Clock锁定位合成或跨来源清除SYSCLK15的历史实现。10/42/44/98/99仍走原有专有路径。
- 原m_warnningCode继续是一次提示，活动集合不消费。不再有externalReferenceClockUnlock历史事件；原15文案由一次提示码或活动15直接维持，普通查询正常后自然清除。
- 新增m_warningMutex只保护观测/提示/有效时钟结果；SDK调用不在该短锁内。持有两个锁时保持m_mutex→m_warningMutex，Streaming发送与状态快照不会无锁访问新集合。
- 非Streaming且AssumedPowerState为PoweredOn、本轮m_errorCode为0时，在原有采样之后执行到期的单次恢复，然后调用普通queryReferenceClockLocked。查询助手只有一个device_query_clock调用点，没有config或恢复确认query；device_config_clock仍为四个调用点，恢复调用从查询助手移至状态线程。
- QElapsedTimer值成员检查100ms恢复门限，实际动作仍随状态轮询触发；m_clockRecoveryAttempted在执行前置位，失败不每秒自动重试。显式clock配置/会话清理及有效锁定恢复允许新尝试；显式查询或RefOut开关本身不重置尝试次数。当前RefOut在执行时保留，无新QObject定时器或异步闭包。
- 每轮有效普通clock查询均附入快照referenceClock，DeviceManager附capabilityRevision。TxSessionService检查UID/revision、待刷新/在途/切换状态，同值跳过同步；有变化再由TxPipelineRuntime更新硬件、已应用、稳定desired缓存，并以setReferenceClock同步原属性。不重发配置，不覆盖用户待处理的新意图；后续成功查询可补上此前失败/被拦下的结果。
- MainWindowDeviceController对General活动消息按集合差异维护，同文案的多个活动贡献不会因删除一项误清。2026-10-28 将 15–19 分类为 LockLoss 并合并到此路径，接收原一次失锁码，替代仅针对 15 的 UI 分支；统一系统时钟/ADDA/DLREF/RFLO/IFLO 文案。随后 DeviceInfoWidget 将文案与可选 DeviceWarning 存为同一轮播项，RLO15–19 与当前失锁文案同步，未更新项的 tooltip 随当前码切换，普通 warning 时隐藏。99/Level及扫描/FFM业务函数保持。
- FFM/FFM-only、模式、三种scan回读、Streaming发送及电源转换保持原实现；FFM/scan/power_state/LO/output SDK调用点数量和配置后查询位置保持。未增加诊断日志、QObject定时器或后台任务。延迟恢复的最新范围和静态结果见[简化TaskLog](../TaskLog/2026-10-21_simplify-delayed-reference-clock-recovery.md)。
- 未执行构建/设备测试。SDK逐warning覆盖、Streaming实时硬件监测仍非本次实现承诺；完整Debug构建及无外参考/运行中拔线、恢复失败、多个来源、低功耗、快速编辑/切换设备等场景待验收。
