# ListMode UI 恢复与 H2 API 契约审查

## 目标

恢复 `StepSweepPanel` 中已经实现但被隐藏的 ListMode 页面，并以当前工作区的
`3rdParty/h2_api/include/h2_api.h` 及现有 TX 业务下发链路为准，确认 ListMode
是否能形成完整、可执行的 MSCAN 流水线；对确定阻塞正常工作的契约问题做最小修改。

## 已知事实与假设

- 当前工作区的 H2 头文件、DLL、LIB 已由用户更新且未提交，视为本任务的 API/ABI 基线；本任务不修改这些第三方文件。
- `src/plugins/core/CMakeLists.txt` 已纳入 `stepsweeppanel.*`、`listmodepanel.*`，相关源码属于当前工程。
- 当前构造函数已经创建 `ListModePanel`，但向 `gridLayoutList` 添加控件的语句被注释，因此选择 List 类型时页面为空。
- 默认验证级别为 `static`；除非用户另行要求，本任务不编译、不启动设备。

## 范围

- `src/plugins/core/stepsweeppanel.cpp`：恢复 ListMode 页面挂载。
- ListMode profile、carrier context、TX executor、HTRA device adapter：静态核对数据、触发、API 调用和查询回写契约。
- 若发现确定会阻止正常工作的局部问题，只修改直接相关源码。
- 不改动其他 Sweep 模式、第三方 H2 文件或无关业务。

## 成功标准

1. 选择 Sweep Type = List 时，`ListModePanel` 实际显示在 `pageListScan` 中并随布局伸缩。
2. UI 选定的有效行范围能够按顺序生成频率、电平、驻留时间三组等长 MSCAN 点。
3. 业务执行最终调用当前头文件声明的 `tx_config_mscan(...)`，并按当前触发 API 配置、启动和触发。
4. API warning/error、点数上限、空列表、查询容量与设备归一化回写具有明确处理，不出现静默越界或明显 ABI 不一致。
5. 静态检查确认修改文件和调用符号一致；不把运行于真实硬件上的结论伪装成已验证事实。

## 计划

1. 核对 H2 MSCAN、trigger、CW/Playback/Stream 相关声明及现有二进制导入符号。
2. 核对 `StepSweepPanel -> TxSessionService/TxPipelineExecutor -> FancyDevice` 的 ListMode 数据流。
3. 恢复 UI 挂载，并修复审查中确认的最小阻塞项。
4. 做静态差异、符号与 CMake 纳入检查，记录结论和剩余实机验证项。

## 验证级别

`static`

## 风险与剩余验证

- 静态分析可确认源代码/API 声明/导入库符号的一致性，但无法代替真实设备固件对 MSCAN、触发动作和 dwell 范围的实机验证。
- DLL/LIB 与头文件均为用户工作区改动，应额外检查导入库实际导出符号，避免只看声明得出错误结论。

## 实施结果

- 已恢复 `Sweep Type = List` 枚举选项，并把 `ListModePanel` 挂入 `pageListScan` 布局。
- 已修复 Global dwell 模式下手工编辑已有行时，频率/功率只更新显示模型、未更新 profile/downlink 模型的问题。
- 已补齐 MSCAN 执行后点表回写：`tx_query_mscan` 返回的频率、电平、驻留时间会写回当前选中区间。
- 已按当前 `h2_api.h` 把 `channel_bus_trigger` 的 `stream_mask` 从 `1U` 改为唯一允许的 `0U`。
- 已同步更新 H2 API KnowledgeBase 中 BUS trigger 的函数名、签名和零 mask 约束。

## 静态核验结果

- `src/plugins/core/CMakeLists.txt` 已纳入 `coreruntimeservices.cpp`、`listmodepanel.cpp`、`stepsweeppanel.cpp`；`src/plugins/htra/CMakeLists.txt` 已纳入 `fancydevice.cpp`。
- 当前 H2 头文件声明与业务调用一致：`tx_config_mscan`、`tx_query_mscan`、`channel_config_trigger`、`channel_query_trigger`、`channel_bus_trigger`。
- 使用 MSVC `dumpbin` 检查用户更新后的 `h2_api.lib` 与 `h2_api.dll`，上述 MSCAN/trigger 符号均实际存在。
- `git diff --check` 通过，仅报告仓库既有的 LF/CRLF 转换提示，无空白错误。
- 未执行编译和实机运行，符合本任务 `static` 验证边界。

## 结论

按当前源码和 API/ABI 静态证据，修复上述四个阻塞/闭环问题后，ListMode 已具备正常形成 MSCAN CW、MSCAN Playback 和桥接 MSCAN Streaming 下发的代码条件。最终产品结论仍需在真实设备上至少验证 BUS + SWEEP、BUS + HOP，以及可选的 External/XPPS 等待触发语义。
