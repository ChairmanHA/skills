# HTRA Multitone 连续编辑期间取消旧生成任务

## Scope

- 分析 `Count=1000` 且上一轮 IQ 尚未生成完成时继续编辑 `FreqSpacing` 的卡死现象。
- 保持现有参数回写、设备能力解析、Save IQ Data ready 语义和最终波形算法不变。
- 让后台生成遵循真正的 latest-intent：新请求不仅阻止旧结果提交，还应尽快终止旧计算。

## Observation

- UI 的 `editingFinished -> resolveAndGenerate() -> setGenerationPlan()` 调用链没有等待 worker，也没有在计算期间长期持有 `MultitoneGenerator::m_mutex`。
- `workerLoop()` 在开始计算前清除 `m_profileChanged`，计算期间的新请求只会再次将该布尔值设为 `true`。
- 旧任务完成后才检查 `m_profileChanged` 并丢弃结果；该检查只能防止 stale result 提交，不能停止旧任务。
- 非 2 的幂 sample count 使用直接合成，复杂度为 `active tone count * sample count`，当前高耗时循环没有取消点。
- 析构流程会等待 worker；若旧任务仍在直接合成，关闭程序也必须等待该任务自然结束。
- `resolveAndGenerate()` 当前先同步重建 tone table、最后才提交 generation plan，因此 UI 刷新期间旧任务仍有效并持续占用资源。
- property 的 `editingFinished` 回调在 `resolveAndGenerate()` 已同步 table 后，又调用一次 `syncPanelFromGenerator()`；一次编辑会重复创建同一批 `QTableWidgetItem`。
- `MultitonePanel` 已用 `m_isSyncingToneTable` 阻止 `itemChanged` 递归，因此这里不是 1000 次业务重入，而是 GUI 线程上的重复同步重建。

## Inference

- 用户观察到的“继续编辑后卡死”不是主线程与 worker 的互斥锁死锁，而是旧的高成本任务不可取消，最新任务只能排在它后面；持续编辑还会不断保持 generator 为 not-ready。
- 单纯禁止编辑会掩盖问题并损害交互。正确边界是保留编辑能力，让新请求原子地使旧 revision 失效，并让旧任务在高耗时循环的取消点退出。

## Design

1. 为异步生成请求增加单调递增 revision；每次请求和析构都使旧 revision 失效。
2. worker 快照 profile、plan 和 revision，并向生成算法传入只读取消回调。
3. 在直接合成、FFT、DC 修正、统计和 IQ 量化等全尺寸循环中设置低开销取消点。
4. 被取消的任务不发布 payload、不计算后续功率/频谱、不发 ready/error 信号；worker 立即回到循环处理最新快照。
5. 最终提交同时校验 pending 标志与 revision，保留原有 stale result 防护。
6. 调整业务事务顺序：plan 解析完成后先提交新 revision、使旧任务失效并禁用 Save，再同步前端 table。
7. 移除编辑回调中 `resolveAndGenerate()` 之后的重复 property/table 同步；panel 对完全相同的 candidate snapshot 不再重建。
8. C/S helper 的 remote panel 只在 `candidateTones` snapshot 变化时重建 table，并在批量填表期间屏蔽表信号和 repaint。

## Success Criteria

- 生成未完成时再次编辑 `FreqSpacing`，旧任务能快速退出，UI 不需要等待旧任务完成。
- 多次连续编辑只允许最新 revision 的结果进入 ready 状态。
- 未被取消时，FFT/直接合成、量化、功率指标和频谱结果保持原语义。
- 析构可使正在生成的任务尽快退出，不再被一次大规模直接合成长时间阻塞。
- 同步 Save IQ 生成路径保持不可取消的现有行为。

## Verification

- Level: static
- 检查 request/revision/worker commit 的完整时序。
- 检查所有大规模循环的取消传播，以及取消路径不会发布部分 payload。
- 检查修改文件仍由 `src/plugins/htra/CMakeLists.txt` 收录。

## Verification Result

- `workerLoop()` 增加 Debug thread-affinity 断言，确认只允许在 `m_workerThread` 执行。
- request 在互斥区内递增 revision、清空 ready result 并设置 pending；worker 快照 revision，计算前后及提交锁内均复核该 revision。
- 直接合成每 1024 个 sample 检查取消；FFT、DC 修正、metrics 和量化的全尺寸循环同样传播取消。
- 取消路径在发布 payload 前返回；若 revision 在后处理阶段变化，则显式释放刚生成的 payload 并跳过所有 result/status/spectrum ready 信号。
- `resolveAndGenerate()` 当前先 `setGenerationPlan()`，再同步 property/table；所有 editing 回调不再追加第二次同步。
- main/remote table 批量填充时均屏蔽 table signal 与 repaint；相同 candidate snapshot 不再重建。
- `src/plugins/htra/CMakeLists.txt` 收录 generator/modulation/panel，`src/app/minibarhelper/CMakeLists.txt` 收录 remote editor。
- `git diff --check` 通过。
- 按仓库默认规则，本次未执行编译或运行验证。
