# SGStudio 知识库索引
本页是文档清单与检索入口；执行规则统一见项目 AGENTS.md。

## 快速定位

先读本页前 35 行，按任务选一个分区，再读该区最相关的 1–2 篇。跨域问题按实际依赖补读；无需顺序通读目录或全库。分区内标有“入口”的文档适合先建立背景，已知具体问题可直接选专题。

| 任务 / 搜索词 | 分区 |
| --- | --- |
| TX、RF/MOD、runtime、provider、插件加载、退出死锁 | [A-runtime](#a-runtime) |
| 设备发现、切换、USB/ETH、UID、多实例、时钟、Trigger、许可证 | [B-device](#b-device) |
| Playback、ARB、WAV/IQS、采样率、带宽、容量、大波形 | [C-playback](#c-playback) |
| AM/FM/Ramp、Multitone、RMS、Digital/16QAM、频谱仪验证 | [D-waveform](#d-waveform) |
| SCPI、程序级远程控制、命令接入、ListMode 命令兼容 | [E-scpi](#e-scpi) |
| Sweep/FScan、扫描业务、CW 模式切换、预校验 | [F-sweep](#f-sweep) |
| Minibar、helper、typed IPC、LayerShellQt、浮窗 | [G-minibar](#g-minibar) |
| MainWindow、QSS、LabelButton、DPI、多屏、布局、Wayland 全屏、延迟 show、消息弹窗 | [H-ui](#h-ui) |
| 软键盘、单位/步长、焦点、重复绑定、QSettings、路径误判 | [I-input-pitfalls](#i-input-pitfalls) |
| CMake、Debug/Release、运行目录、Windows DLL、stage、品牌打包 | [J-build-windows](#j-build-windows) |
| Linux、树莓派、RK3588、250/108、ABI、X11/Wayland、VMware | [K-linux](#k-linux) |
| Updater、固件、maintenance、更新包、共享机箱 A/B | [L-updater](#l-updater) |
| 版本日志、Git、分支/标签、TaskLog、prompt、协作 | [M-collaboration](#m-collaboration) |

按需读取（PowerShell；后续命令复用 `$kb`）：

```powershell
$kb = 'D:\development\skills\.github\KnowledgeBase'
Get-Content -Encoding UTF8 -LiteralPath "$kb\Index.md" -TotalCount 35
rg -n -A 20 '^## E-scpi$' "$kb\Index.md" # 换成所选分区；读取标题及后 20 行
rg -n '^#{1,3} ' "$kb\listmode_scpi调试指南.md" # 先定位目标文档章节
Get-Content -Encoding UTF8 -LiteralPath "$kb\listmode_scpi调试指南.md" | Select-Object -Skip 20 -First 40 # 按命中行号调整
```

未命中时先 `rg -n -i '关键词|类名' "$kb\Index.md"`，再用 `rg --files "$kb"` 查文件名，最后仅搜索候选文档正文。以下每篇文档只登记一次；标签“方案 / 历史 / 分析 / 参考”表示用途，不代表方案已落地或推断已验证。混合文档按正文区分现状与计划；当前架构优先于历史记录，代码现状仍须结合 CMake 纳入范围核实。

## A-runtime

- [入口：TX 请求、执行与回写](tx_execution_context_phase1_and_provider_migration.md)：TxSessionService / TxPipelineRuntime / executor / latest-intent。
- [RF / Mod 业务概览](leader_rf_mod_business_overview.md)：公共设置、载波、基带、触发。
- [单通道 H2 信号流 Home 设计依据](h2_single_channel_signal_flow_home_rationale.md)：仅依据当前 h2_api.h；USG 风格基带/调制/射频分工与状态证据边界，尚未实施。
- [UI 无关运行时与迁移史](ui_independent_runtime_and_minibar_design.md)：CoreRuntimeServices / DeviceRuntimeBridge；旧 in-process 部分为历史。
- [Provider ownership](analog_htra_provider_lifecycle_and_duplicate_panel_guard.md)：Analog / HTRA 调制归属与已删除的重复 panel 防护。
- [Streaming 数据流](streaming_dataflow.md)：legacy 双线程、队列、中断与重启。
- [Streaming 桥接与双通道方案](streaming_bridge_rearm_reboot_refactor_plan.md)：session / endpoint / rearm / reboot / quiesce。
- [GNSS 接入](gnss_plugin_integration_summary.md)：菜单、配置、实时状态与 Streaming 禁配。
- [插件加载](plugin_metadata_and_loading_architecture.md)：metadata / JSON 依赖 / PluginManager 顺序。
- [退出与关机](app_shutdown_exit_flow.md)：shutdown 两阶段、线程释放、死锁。

## B-device

- [入口：设备发现](device_discovery_architecture.md)：枚举与职责边界。
- [设备切换与 UI 配置链](device_open_ui_config_flow.md)：open → UI → orchestrator / runtime。
- [多设备生命周期](htra_multi_device_stageA_design_and_debug.md)：current / retain-open / fallback / watchdog / A-B 机箱。
- [多实例 USB 占用](multi_instance_usb_ownership_and_startup_gate.md)：InstanceStateRegistry / startup gate / UID 恢复。
- [手工 ETH 连接](manual_eth_connect_temporary_design.md)：临时设计、启动策略、IP:Port、模态连接。
- [USB / ETH 断联恢复分析](eth设备和usb设备断联恢复的异同点分析.md)：失联判定、注销、coordinator 差异。
- [H2 API v2.0 参考](htra_h2_api_v2_0_usage.md)：设备能力与 API 调用顺序。
- [设备设置与 Trigger 拆分](device_settings_panel_and_trigger_split_design.md)：Reference Clock / Trigger In-Out / RF Hardware。
- [参考时钟接入](htra_reference_clock_integration.md)：Profile / FeatureSpec 与前端联动。
- [Capability ID 分档方案](common_capability_id_and_hw_version_strategy.md)：model / hardware_version / 公共语义 ID。
- [RF Port 能力与绑定](rfport_open_query_and_ui_binding.md)：open 查询、EnumTextButton / property。
- [型号显示名](device_model_display_name_rules.md)：device_info.xml 条件别名。
- [Analog 许可证](analog_device_license_gating.md)：FixedLic、USB/ETH 时序与 UI 门控。
- [PGA 平台状态](pga_runtime_realtime_status_chain.md)：CPU 温度、电池、状态栏链路。
- [GPIO 位掩码分析](GPIO_interface.md)：setbits / resetbits 语义推断，需核实 API 契约。

## C-playback

- [入口：波形参数约束](Waveform_Parameters_Constraints.md)：默认值、采样率、IQ 带宽、UI 联动；含 AWGN 单遍/分块 FIR 优化、实测耗时及频谱/RMS验收。
- [多型号 Playback 能力迁移](htra_multi_model_playback_capability_refactor.md)：能力域、revision、immutable payload、分阶段进度。
- [设备能力参数策略](playback_waveform_parameter_capability_policy.md)：OPTION_BW_320M_TX、400 MSPS 与参数决策目标。
- [产品参数决策矩阵](playback_parameter_product_decision_matrix.md)：无/有选件对比、待实现与待确认项。
- [ARB 模式入口](arb_mode_summary.md)：Ordinary / IQS / ProgrammedArb 解析与 runtime 边界。
- [AWG、LF 链路与模型 B0](awg_model_b0_lf_chain_and_waveform_processing.md)：AWG 本质、RF/LF 边界、固定采样缓冲契约、全波形处理及 B0 参数界面归属。
- [OrdinaryWav 截取与周期](arb_ordinary_wav_slice_period_semantics.md)：sampleOffset / samplesToUse / period。
- [大波形内存与下载](large_waveform_streaming_plan.md)：payload 驻留、125/1000 MiB、租约与下载后释放。
- [Digital 大波形汇报](digital_large_waveform_status_summary.md)：截断播放与完整保存的阶段总结。
- [Save IQ 导出 WAV](waveform_wav_export_format.md)：PCM16、I/Q 交织、prof JSON。
- [IQS-WAV 格式参考](IQS-WAV文件格式说明.md)：录制文件、trig / prof MsgPack，区别于 Save IQ。

## D-waveform
- [Digital 交互入口](digital_modulation_user_interaction_flow.md)：SPS、异步生成、截断、reset、保存。
- [Multitone 算法入口](htra_multitone_current_algorithm_and_vsg60_boundaries.md)：tone / notch / phase / AutoScale / preview。
- [Multitone 与 VSG60 对比](multitone_vsg60_three_case_comparison_and_playback_consistency.md)：三组样本与 Playback 一致性。
- [iqtone.m 借鉴候选](htra_multitone_iqtone_reuse_candidates.md)：历史参考、后续优化建议。
- [Multitone / Pulse RMS 原理](htra_rms_power_multitone_pulse_principle.md)：PEP、占空比与满幅参考。
- [AM 自研对齐方案](am_baseband_self_implementation_alignment.md)：复基带参数、离散 IQ 与参考行为。
- [FM 自研对齐方案](fm_baseband_self_implementation_alignment.md)：复基带参数与参考行为。
- [Ramp 自研实现](ramp_self_implementation_alignment.md)：波形原理与 HTRA 实现边界。
- [标准星座与 VSG60 坐标](digital_standard_constellation_vs_vsg60_custom_iq.md)：理论预览与自定义符号映射。
- [16QAM raw cloud 原理](digital_16qam_raw_cloud_vs_symbol_constellation.md)：过采样、RRC、匹配滤波。
- [16QAM 星座复现](digital_16qam_wav_constellation_reproduction_guide.md)：离线 WAV 分析与出图。
- [N9040B AM/FM/PM 测试](n9040b_am_fm_pm_demod_test_plan.md)：射频解调验证方案。
- [N9040B 窄脉冲测试](n9040b_pulse_detection_122_narrow_pulse_test_guide.md)：122 设备、Pulse Detection、Width / PRI。
- [Ramp 频谱仪测试](htra_ramp_spectrum_analyzer_test_plan.md)：SWP / IQS / DET / RTA 验证方案。

## E-scpi

- [入口：SCPI 接入审阅与方案](scpi_review_and_integration_plan.md)：TX/UI 回写、错误语义、ScpiIntentService 迁移。
- [ListMode 命令调试入口](listmode_scpi调试指南.md)：TCP 5025、命令表、查询响应、R&S 兼容实测流程。
- [ListMode 接口设计](listmode接口实现.md)：MScan、真实执行与兼容映射边界。
- [R&S List Mode 参考](rs_list_mode_scpi_reference.md)：R&S 的 listmode scpi命令、触发、索引、学习与文件容器。

## F-sweep

- [FixedCw / SweepCw 切换](fixedcw_sweepfscan_fixedcw_api_summary.md)：FScan 往返 API 调用链。
- [Sweep 预校验与能力](sweep_preview_validation_boundary.md)：编辑态 preview、归一化与发射边界、F60 ListMode 限制。

## G-minibar

- [入口：双进程 Minibar](minibar_cs_helper_scpi_architecture.md)：main 设备 owner、helper typed IPC、乐观 UI、隐藏主窗提示抑制。
- [Wayland 交互排障入口](minibar_wayland_layershell_debug_guide.md)：surface / geometry / focus / lifetime，含历史证据。
- [Helper LayerShell 现状](minibar_helper_layershell_parity_gaps.md)：base / keyboard / overlay、ownership、首次映射。
- [LayerShellQt 集成](minibar_wayland_layer_shell_qt_integration.md)：Qt5 依赖、构建与排障，含旧 host 历史。
- [旧 popup host 历史](minibar_popup_host_style_and_focus_status.md)：已删除的 in-process 路径；仅供视觉、焦点与关闭顺序回溯。

## H-ui

- [QSS 入口](QSS_Best_Practices.md)：样式编写与工程约定。
- [LabelButton 状态样式](labelbutton_style_state_workflow.md)：InfoButton、刷新链、RF / Settings / Sweep。
- [双行按钮空间限制](labelbutton_dual_row_vertical_space_limit.md)：padding / labelMargins；[5 寸字号与物理换算](<5 寸 1024×600 的物理换算（“模拟”基线）.md>)：instrument 24px / 64px 参数按钮与真机验收边界。
- [Level UNLEVEL badge](commonpanel_level_unlevel_badge_ui.md)：CommonPanel 局部标记。
- [Auto Mod 用户意图](auto_mod_user_intent_boundary.md)：FancyTabWidget 信号与 MOD availability。
- [调制列表响应式布局](fancytabwidget_modulation_list_responsive_layout.md)：单/双列、宽度与滚动条。
- [TitleBar 菜单与溢出](titlebar_menubar_outputmode_and_overflow_behavior.md)：central grid 接入、输出模式、工具组、重复触摸。
- [Instrument 窗口平台行为](instrument_ui_mode_platform_window_behavior.md)：QWindowKit 归属、Linux x86_64 非目标范围与 aarch64 Wayland 1024×600 非全屏合同。
- [Windows 主窗最小尺寸](mainwindow_frameless_minimum_size_contract.md)：QWindowKit 迁移后暴露的 List Sweep 隐藏布局缓存、标题栏宽度汇总与 native resize 合约。
- [Wayland Panel 高度与启动时序坑](mainwindow_panel_minimum_height_wayland_contract.md)：延迟 show、隐藏窗口临时几何、发行版 Qt 与自带标准 Qt 的补丁差异、Wayland 全屏状态栏裁切。
- [High DPI 入口](high_dpi_development_practices.md)：Qt5 缩放、资源倍率、多屏拖动。
- [多屏 popup 几何](multiscreen_popup_geometry_and_screen_topology.md)：屏幕断开、DPI、EnumTextButton / ComboBox / QMenu。
- [Frameless 白边排障](frameless_multimon_dpi_white_border_issue.md)：多屏与高 DPI。
- [设备状态 UI 反馈](device_status_ui_feedback.md)：error / warning 去重与 Minibar 例外。
- [MessageDialog](messagedialog_design.md)：交互、hosted 层级与提示边界。
- [NotificationPopup](controls_notification_popup.md)：非模态通知、动画、定位与自动关闭。
- [Wayland 无边框弹窗](WAYLAND_FRAMELESS_OVERLAY_PATTERN.md)：overlay 与遮罩模式、List Mode 数字键盘与全局外部点击边界。
- [Wayland 嵌套模态排障](wayland_nested_modal_dialog_issue_record.md)：弹窗消失、输入阻塞与工程决策。

## I-input-pitfalls

- [入口：软键盘体系](soft_keyboard_architecture.md)：TouchNumKeyboard / BaseUnitAdapter / 单位同步 / helper-local / instrument 键盘与 Panel QSS 边界。
- [数值步长策略](numeric_step_strategy_guidelines.md)：可编辑 / 125 / 固定步长选择。
- [树莓派系统键盘](wayland_raspberry_pi_system_keyboard_integration.md)：QLineEdit / QInputMethod / OSK0 / DBus。
- [重复软键盘](Pitfalls/Property_Binding_Duplicate_SoftKeyboard.md)：隐藏 panel、多控件绑定、beginEditing 来源。
- [焦点与步长编辑](Pitfalls/Qt_FocusProxy_SoftKeyboard_StepEdit.md)：focusProxy / overlay 路由。
- [提交重入与生命周期](Pitfalls/SoftKeyboard_MessageDialog_Reentrancy_And_Lifetime.md)：MessageDialog / focusWidget。
- [INI General 组](Pitfalls/Qt_QSettings_INI_General_Group.md)：QSettings 解析陷阱。
- [IPC 路径误判越界](Pitfalls/Qt_CleanPath_IPC_Path_Separator.md)：cleanPath 与 separator 混用。

## J-build-windows

- [入口：CMake 工作流](cmake_build_output_clean_run_workflow.md)：configure / build / 输出 / 清理。
- [运行布局](runtime_layout_repo_root_vs_build_tree.md)：SGS_RUNTIME_LAYOUT、repo-root / build-tree、插件 staging。
- [Windows clone 到 stage](windows_cmake_clone_build_stage_guide.md)：环境准备、依赖归属与交付包。
- [打包脚本与水印](build_script_packaging_and_watermark_guide.md)：Windows/Linux 参数、rebuild 与归档。
- [build.bat 打包依赖](windows_build_bat_updater_packaging_hygiene.md)：updater / 3rdParty 增量构建。
- [MSVC 运行库打包](windows_stage_msvc_runtime_packaging_strategy.md)：runtime / UCRT / vc_redist 边界。
- [Qt 混装与 SVG 失效](windows_qt_runtime_mixing_pitfall.md)：DLL 来源与 Debug/Release 差异。
- [VS Code 环境排障](windows_vscode_environment_pitfalls.md)：pwsh、includePath、IntelliSense。
- [Neutralized 品牌](windows_neutralized_branding_workflow.md)：build / stage 的品牌一致性。
- [VectorCore BNC_en 品牌](vectorcore_bnc_en_branding_profile.md)：profile、图标、主题与更新包命名。

## K-linux

- [入口：Linux 构建发布](linux_build_package_unified_entry.md)：250 / 108 两入口、Raspberry Pi / RK3588 共用包、直接启动。
- [AArch64 兼容性](raspberry_pi_132_build_host_250_compatibility.md)：ABI、Qt、Wayland/xcb 与双目标验收。
- [x86_64 X11 与 Wayland 迁移方案](linux_x86_64_x11_and_future_labwc_wayland.md)：当前 xcb/QWindowKit 主窗边界、未来 labwc artifact。
- [RPATH](RPATH_MECHANISM.md)：$ORIGIN 相对库路径。
- [VMware 直连树莓派](ubuntu18_vmware_raspberry_pi_bridge_setup.md)：Ubuntu18、NAT + 桥接、静态地址与回滚。

## L-updater

- [入口：Updater 当前实现](updater_firmware_update_mechanism.md)：下载、version.json、固件 Profile、H2 命名空间过渡、maintenance。
- [Updater 整改重点](updater_mechanism_gap_and_remediation.md)：已收敛行为、剩余问题与验收。
- [Standard CN 远程更新联测](standard_cn_remote_manual_update_test_guide.md)：发布 URL、包结构、手工下载与回滚。
- [A/B 顺序固件更新分析](shared_chassis_ab_sequential_updater_analysis.md)：共享 IP 概率失败、待取证假设。
- [SAStudioEx 迁移参考](updaterFromSAStudioEx-更新全流程迁移说明.md)：外部项目更新链，非 SGStudio 当前实现入口。

## M-collaboration

- [GUI 2.6 版本日志](gui_update_log_2.6.md)：2.6.1–2.6.3.3 变更记录。
- [Git 基础约定](Git_Best_Practices.md)：日常操作与检查。
- [Git 分支工作流](GIT_WORKFLOW_GUIDE.md)：合并与发布。
- [重建标签工作流](git_retag_force_push_and_checkout_workflow.md)：目标提交、远端标签覆盖与切回分支。
- [Agent Loop Harness 与轻量协作](copilot_prompt_workflow_notes.md)：instruction / prompt / TaskLog / KnowledgeBase 分工，以及执行、观察、验证、证据持久化闭环。

## 索引维护

- 新增、重命名或删除知识文档时同步本页；每篇只放入一个最相关分区，使用“短标题 + 区分性关键词”，避免重复文件名和长摘要。
- 每区不超过 18 个条目，以便一次定长读取；扩展时同步首页路由。保留文件原名，链接相对知识库根目录；不设 Recent Additions 或另一份全量清单。
- 只索引现存知识文档；任务过程放 `D:\development\skills\.github\TaskLog`，复用流程到 `D:\development\skills\.github\skills` 按文件夹/前言选 SKILL.md，具体规则留在各自真源。
- 维护后检查链接存在、无重复、无漏收；缩短描述时保留适用平台、历史/方案属性和容易混淆的边界。
