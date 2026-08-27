# Knowledge Base Index

> Current device-startup policy (2026-08-19): normal `Default`/`User` startup does not restore a remembered manual ETH endpoint; USB scanner keeps automatic selection. `Last`, application continuity restart (`APP/Reboot=True`), and `--UpdateCompleted` may restore the last successful ETH endpoint. The startup ETH Connect dialog is a separate compile-time feature and defaults to `OFF`.

## Recent Additions

| Document | Description |
| :--- | :--- |
| [linux_build_package_unified_entry.md](linux_build_package_unified_entry.md) | Linux 两入口构建模型：250 的 `raspberry-pi` profile 生成 Raspberry Pi Wayland + RK3588 X11 共用归档并直接运行 `bin/<application>`；108 保留 `build_pi.sh` 原生防火墙。 |
| [raspberry_pi_132_build_host_250_compatibility.md](raspberry_pi_132_build_host_250_compatibility.md) | 250 AArch64 交叉构建与 108/RK3588 双目标的兼容依据：覆盖旧 ABI 门禁、闭源库窄例外、Qt/LayerShellQt 隔离、Wayland/xcb 自动选择和直接启动合同。 |
| [linux_x86_64_x11_and_future_labwc_wayland.md](linux_x86_64_x11_and_future_labwc_wayland.md) | Linux x86_64 当前固定 X11/xcb 的构建与启动边界，以及未来以独立 artifact 恢复 labwc/wlroots Wayland、LayerShellQt、Qt private ABI、运行时打包和 UI 验收的迁移步骤。 |
| [scpi_review_and_integration_plan.md](scpi_review_and_integration_plan.md) | SCPI 当前 TX/runtime/UI 回写依据、必须立即修正的问题，以及从 UI 自动化式 SCPI 迁移到窄 SCPI intent service 的阶段方案。 |
| [minibar_wayland_layershell_debug_guide.md](minibar_wayland_layershell_debug_guide.md) | Minibar 在 Raspberry Pi Wayland 使用 LayerShellQt 的长期 Debug 总入口：统一 surface/host/input/geometry/focus/lifetime 模型，汇总 helper 当前实现与已删除 in-process 路径的历史证据、失败回退、取证流程和双平台回归矩阵。 |
| [minibar_helper_layershell_parity_gaps.md](minibar_helper_layershell_parity_gaps.md) | `SGStudioMiniBar` 的 Wayland base surface、managed keyboard、Sweep/MOD/menu overlay 与 Win32/Wayland 平台分流现状；记录 enum popup 首次映射前配置、ownership、outside-click 和单调时间 close guard。 |
| [minibar_cs_helper_scpi_architecture.md](minibar_cs_helper_scpi_architecture.md) | 双进程 minibar 唯一产品架构：main 独占设备/runtime，helper 使用 typed snapshot/request、乐观 UI intent 与差分回写接入 RF/Center/Level/Sweep/MOD；同时定义隐藏 MainWindow 期间的 prompt suppression、结构化错误反馈与“不转发 MessageDialog”边界。 |

本文档是 `.github/KnowledgeBase/` 的索引（以本仓库实际存在的文件为准）。

## 核心架构与生命周期

当前主业务架构已收敛到 `TxApplyRequest -> TxPipelineRuntime` 的 core-managed 模型，并进一步把共享发射 owner 下沉到 `TxSessionService`。主进程只创建 `MainWindow`；minibar 由独立 `SGStudioMiniBar` helper 通过 typed IPC 接入同一套 main owner，legacy in-process UI 已删除。`MSCAN` 代码链路虽然已经并入统一 request/runtime 设计，但当前 UI 入口仍因设备联调未完成而暂时隐藏。现阶段真正仍保持特殊路径的主要是 `Streaming` 与尚未接入 runtime 的 `ProgrammedArb`；Ordinary/IQS ARB 已进入统一 Playback request/runtime。

| 文档 | 说明 |
| :--- | :--- |
| [tx_execution_context_phase1_and_provider_migration.md](tx_execution_context_phase1_and_provider_migration.md) | 当前主业务架构文档：说明 `TxSessionService` 如何成为共享 resolve/build owner，`TxPipelineRuntime` 的串行异步 latest-intent 协调与 generation writeback 过滤，`TxPipelineExecutor` / core-managed / legacy-managed 的边界，以及 Playback immutable payload、同步下载释放与设备驻留复用语义；文内 in-process minibar 部分保留为历史迁移证据。 |
| [scpi_review_and_integration_plan.md](scpi_review_and_integration_plan.md) | SCPI 接入边界审阅与当前收口方案：说明为什么现有 TX apply/writeback 足够支撑 CW 类 UI 同步，记录已修 P0 后仍存在的 metadata、UI 自动化、错误/完成语义、安全配置、参数校验和第三方 vendoring 卫生问题，并给出向 `ScpiIntentService` 迁移的阶段路径。 |
| [htra_h2_api_v2_0_usage.md](htra_h2_api_v2_0_usage.md) | HTRA H2 API v2.0 设备能力参考：覆盖 CW/Playback/Stream/MSCAN/GNSS 及推荐调用顺序；当前主要用于理解设备层语义，以及 `Streaming` / `MSCAN` / `Arb` 等剩余特殊路径。 |
| [streaming_dataflow.md](streaming_dataflow.md) | `Streaming` 当前独立 legacy 路径的“双线程 + 队列”总览：UI/Property/Business/Device 的交互、中断、重启与数据发送语义。 |
| [streaming_bridge_rearm_reboot_refactor_plan.md](streaming_bridge_rearm_reboot_refactor_plan.md) | `Streaming` 当前桥接实现与下一步重构基线：总结 `FixedStream / SweepStream` 如何桥接到 `StreamingBussiness`，以及运行期参数如何区分为在线热切、RearmSession、HardReboot 与 Pipeline 切换。 |
| [device_discovery_architecture.md](device_discovery_architecture.md) | 设备发现/枚举架构与职责边界。 |
| [multi_instance_usb_ownership_and_startup_gate.md](multi_instance_usb_ownership_and_startup_gate.md) | 当前多实例 USB owner / startup gate 的真实实现说明：覆盖 `InstanceStateRegistry`、startup free USB 选择、runtime auto-attach 经 profile coordinator 恢复目标 UID、手工 ETH 边界，以及仍未收口到设计的几个实现差异。 |
| [device_open_ui_config_flow.md](device_open_ui_config_flow.md) | 设备切换 → UI 更新 → orchestrator / runtime / legacy business 驱动配置的完整调用链与已知坑位。 |
| [device_model_display_name_rules.md](device_model_display_name_rules.md) | `configuration/device_info.xml` 的设备型号显示规则：兼容旧的 `code -> name` 映射，并支持按 `hardwareType`、`os`、`optionCodes` 的有序条件别名，无需重新编译即可调整 UI 显示名。 |
| [pga_runtime_realtime_status_chain.md](pga_runtime_realtime_status_chain.md) | PGA 平台状态栏新增实时链路说明：`PgaRuntimeStatusService` 平台态支路、与 `DeviceManager -> MainWindowDeviceController -> DeviceInfoWidget` 既有设备态链路的复用边界，以及 CPU 温度/电池解析与退化语义。 |
| [ui_independent_runtime_and_minibar_design.md](ui_independent_runtime_and_minibar_design.md) | UI 无关运行时设计与 in-process minibar 迁移史：说明 `CoreRuntimeServices`、`DeviceRuntimeBridge`、`IBusinessEntryHost + BusinessManager(entry facade)`、`TxSessionService` 的保留边界，以及删除 legacy UI 后由 helper/IPC 接管的职责。 |
| [minibar_cs_helper_scpi_architecture.md](minibar_cs_helper_scpi_architecture.md) | 双进程 minibar 的已实现边界：lifecycle、typed snapshot/request、RF/Center/Level 值/单位/步进闭环、Sweep/MOD 回写，以及 helper 不承载 MessageDialog、MainWindow 隐藏期统一抑制 popup prompt 的错误反馈策略。 |
| [plugin_metadata_and_loading_architecture.md](plugin_metadata_and_loading_architecture.md) | Qt plugin metadata、json 字段、依赖解析与 PluginManager 加载顺序说明。 |
| [app_shutdown_exit_flow.md](app_shutdown_exit_flow.md) | 退出/关机流程：PluginManager shutdown 两阶段与线程资源释放注意事项。 |
| [updater_firmware_update_mechanism.md](updater_firmware_update_mechanism.md) | Updater 当前活跃实现说明：覆盖下载复用与清理、包结构、新旧 `version.json` 兼容、按设备选件精确匹配固件 Profile、updater / maintenance 交接边界，以及 Win32 外部文件占用隐患与延后处理方案。 |
| [standard_cn_remote_manual_update_test_guide.md](standard_cn_remote_manual_update_test_guide.md) | Standard CN 在 Windows x86_64 与 Linux aarch64 上的远程手动下载更新发布/联测指南：精确 URL、运维上传文件、归档结构、手动下载交互、客户端准备、验收与回滚边界。 |
| [updater_mechanism_gap_and_remediation.md](updater_mechanism_gap_and_remediation.md) | Updater 当前机制与过去机制的差异分析、严重问题分级，以及建议的整改目标架构与分阶段落地顺序。 |
## 设备/信号/性能专题

| 文档 | 说明 |
| :--- | :--- |
| [n9040b_pulse_detection_122_narrow_pulse_test_guide.md](n9040b_pulse_detection_122_narrow_pulse_test_guide.md) | N9040B 脉冲检测实机教程：122含带宽选件设备的48/96 ns回归、30/40 ns连续档边界、25/50 ns的400 MSPS切换点和15/20 ns极限探索；固定Pulse Detection/Regions口径并按当前半幅边沿算法给出Width/PRI预期。 |
| [arb_mode_summary.md](arb_mode_summary.md) | HTRA ARB 当前模式边界：Ordinary/IQS WAV 已使用单一 immutable payload、按设备能力校验并进入统一 runtime；ProgrammedArb 仍仅保留解析与路由语义。 |
| [htra_multi_device_stageA_design_and_debug.md](htra_multi_device_stageA_design_and_debug.md) | HTRA 多设备（阶段A）设计与调试手册：枚举快照、对账、唯一 current、通用 retain-open、运行期 USB fallback 的 UID profile 恢复、启动连接意图与 ETH 恢复、不可中途取消的模态 ETH Connect、watchdog abort 真实回切终态、retained manual ETH 的异步 ping P0 防护，以及同 IP 5000/5001 共享机箱失联时的整组注销语义。 |
| [htra_reference_clock_integration.md](htra_reference_clock_integration.md) | HTRA 参考时钟接入现状：Profile/FeatureSpec 设计取舍、配置流程、行业语义与前端联动约束。 |
| [device_settings_panel_and_trigger_split_design.md](device_settings_panel_and_trigger_split_design.md) | 集中设备设置页的当前设计说明：入口迁移、standalone page 承载、Reference Clock / Trigger In / Trigger Out / RF Hardware 四组布局，记录新版 `device_config_trigger_out()` 的 UI 动态映射与 write-after 边界，以及独立直通设备的 RefOut、Fan Mode 与 Low Power 控制。 |
| [common_capability_id_and_hw_version_strategy.md](common_capability_id_and_hw_version_strategy.md) | 公共 capability ID 与硬件版本分档策略：说明为什么应把 TriggerSource / RefClockSource 迁移到公共语义 ID，并在设备 open 后基于 model + hardware_version 生成最终 capability profile。 |
| [htra_multi_model_playback_capability_refactor.md](htra_multi_model_playback_capability_refactor.md) | HTRA 多型号 Playback 能力重构：Phase A/B/C 已落地能力域、request revision 与文件 immutable payload；Phase D 已完成 Digital、DSSS、OFDM 的 domain/capacity、静默参数收口、生成前租约和 external payload 迁移，后续继续覆盖其他生成型业务与完整热插拔验收。 |
| [playback_waveform_parameter_capability_policy.md](playback_waveform_parameter_capability_policy.md) | 当前启动 Playback business 的设备能力约束目标：按 `OPTION_BW_320M_TX` 对比无/有带宽选件档的采样率、容量和设备切换策略，统一 400 MSPS 单点语义，并详细定义 Pulse、AM/FM/PM、Ramp/AWGN 等多候选参数决策。 |
| [playback_parameter_product_decision_matrix.md](playback_parameter_product_decision_matrix.md) | 面向产品经理的无/有 `OPTION_BW_320M_TX` Playback 参数范围对比表：列出各 business 的 UI 有效范围和采样率，区分能力已确定、推荐待实现与产品待确认项，并提供集中决策清单。 |
| [manual_eth_connect_temporary_design.md](manual_eth_connect_temporary_design.md) | 手工 ETH 连接的临时设计说明：涵盖 Default/Last/重启/更新后的启动连接策略、默认关闭的启动 ETH Connect 编译选项、manual-managed device 的接入方式、共享 IP 下 5000/5001 顺序独立尝试与部分成功保留、异步模态且不可中途取消的连接交互、watchdog abort 终态、按 `IP + Port` 复用已打开 endpoint、普通 retain 与共享机箱失联清理语义，以及未来统一设备发现/打开抽象时的重构边界。 |
| [analog_device_license_gating.md](analog_device_license_gating.md) | Analog 插件基于设备参数的许可证校验与剩余数字类波形生成门控说明：覆盖 `FixedLic=true/false` 语义、`currentDeviceOpenStateChanged(false) -> deviceConnected` 时序、USB/ETH 差异、UI 禁用与失败弹窗。 |
| [analog_htra_provider_lifecycle_and_duplicate_panel_guard.md](analog_htra_provider_lifecycle_and_duplicate_panel_guard.md) | Analog / HTRA 基础调制 ownership 当前边界：说明 HTRA 已成为 AM/FM/Pulse/Digital Ramp/AWGN 的唯一 provider owner，Analog 侧同名 provider 切换机制与重复 panel 防护已拆除。 |
| [gnss_plugin_integration_summary.md](gnss_plugin_integration_summary.md) | GNSS 插件从独立菜单、设备配置通路、实时状态发布到 Streaming 禁配策略的实现总结与剩余问题清单。 |
| [large_waveform_streaming_plan.md](large_waveform_streaming_plan.md) | 大波形内存、125/1000 MiB 单大 payload 驻留合同、文件 Playback 直接 `int16_t` 物化/下载后释放，以及 Digital/DSSS/OFDM 的生成前租约与合作方指针直接 adoption。 |
| [digital_modulation_user_interaction_flow.md](digital_modulation_user_interaction_flow.md) | 数字调制用户交互流程：覆盖设备能力驱动的 SPS 筛选、参数异步生成、125/1000 MiB 大波形截断确认、设备切换静默整组 reset，以及 Save IQ 完整保存流程。 |
| [waveform_wav_export_format.md](waveform_wav_export_format.md) | SGStudio `Save IQ Data` 导出 WAV 的代码级格式说明：RIFF/PCM16 双通道头、I/Q 交织顺序、`prof` JSON chunk、动态偏移与文件上限，并区分 IQS-WAV。 |
| [Waveform_Parameters_Constraints.md](Waveform_Parameters_Constraints.md) | 各类模拟/数字波形的默认值、参数意义、限制范围、IQ 带宽语义与 UI/业务联动总入口。 |
| [htra_multitone_current_algorithm_and_vsg60_boundaries.md](htra_multitone_current_algorithm_and_vsg60_boundaries.md) | HTRA multitone 当前实现总览：汇总参数语义、tone lattice/notch/phase mode、采样率与 exact-period 点数策略、AutoScale 等价量化、preview 频谱、generation revision 取消语义、前端 table 同步边界，以及与 VSG60 的允许差异。 |

## UI / QSS / 窗口系统

| 文档 | 说明 |
| :--- | :--- |
| [device_status_ui_feedback.md](device_status_ui_feedback.md) | 设备状态前端反馈机制：MainWindow error 弹窗、C/S Minibar 抑制例外、warning 滚动播放与各链路去重规则。 |
| [messagedialog_design.md](messagedialog_design.md) | MessageDialog 交互与组件设计约定；包含 MainWindow Wayland hosted 层级和 C/S Minibar 不展示、不转发、不重放提示的边界。 |
| [auto_mod_user_intent_boundary.md](auto_mod_user_intent_boundary.md) | Auto Mod 的用户意图边界、FancyTabWidget 信号职责，以及 `MOD` 按钮 availability 收口规则。 |
| [mainwindow_panel_minimum_height_wayland_contract.md](mainwindow_panel_minimum_height_wayland_contract.md) | MainWindow 业务 Panel 最小高度与 Raspberry Pi Wayland 1280x800 全屏边界：记录 Quick Waveform 隐藏页经 QStackedLayout 把窗口抬到 805px 的完整链路、Qt 5 spacer 陷阱，以及新增任何 Panel 时必须执行的尺寸检查清单。 |
| [fancytabwidget_modulation_list_responsive_layout.md](fancytabwidget_modulation_list_responsive_layout.md) | FancyTabWidget 右侧调制列表的响应式布局设计：解释主窗两列结构、单列/双列切换、显式列模式、滚动条参与的动态单列宽度，以及修改这块 UI 时的边界与检查清单。 |
| [titlebar_menubar_outputmode_and_overflow_behavior.md](titlebar_menubar_outputmode_and_overflow_behavior.md) | TitleBar 当前的菜单组、Preset/Single/Continue/Screenshot/MiniBar 工具组、视觉分隔符与 menubar overflow 实现说明，覆盖布局结构、主题样式、宽度压缩行为，以及 Raspberry Pi Wayland 下顶级菜单重复触摸的事件时序与处理边界。 |
| [controls_notification_popup.md](controls_notification_popup.md) | `Controls::NotificationPopup` 的非模态通知语义、动画、悬停暂停自动关闭、可复制文本和调用方定位边界。 |
| [high_dpi_development_practices.md](high_dpi_development_practices.md) | Windows / Qt 5 下 High DPI、多屏拖动、QSS 尺寸体系、资源倍率与应用内 UI 缩放的实践总结。 |
| [multiscreen_popup_geometry_and_screen_topology.md](multiscreen_popup_geometry_and_screen_topology.md) | 外接屏断开、DPI/屏幕拓扑变化后 `EnumTextButton`、`Controls::ComboBox`、`PopupWidget`、TitleBar `QMenuBar/QMenu` popup 几何、首次映射、item 高度及 mouse/touch 二次点击收起行为。 |
| [QSS_Best_Practices.md](QSS_Best_Practices.md) | QSS 编写与工程化最佳实践。 |
| [labelbutton_style_state_workflow.md](labelbutton_style_state_workflow.md) | `LabelButton / InfoButton` 双行按钮的状态载体、样式刷新链与推荐修改流程，覆盖 `RF / General Settings / Sweep` 三种典型用法，并说明 instruction、skill、KnowledgeBase 的分工。 |
| [commonpanel_level_unlevel_badge_ui.md](commonpanel_level_unlevel_badge_ui.md) | `CommonPanel` 中 `Level` 按钮的 `UNLEVEL` 局部 badge 设计说明：为何只影响启用 badge 的特定 `LabelButton`。 |
| [soft_keyboard_architecture.md](soft_keyboard_architecture.md) | 当前软键盘体系总览：TouchNumKeyboard、BaseUnitAdapter、步长编辑、currentUnit/displayText 同步、业务层修正后的单位恢复，以及 C/S helper-local 键盘、typed IPC 和 managed overlay 边界。 |
| [wayland_raspberry_pi_system_keyboard_integration.md](wayland_raspberry_pi_system_keyboard_integration.md) | 树莓派 Wayland 桌面下把系统键盘整合进普通 `QLineEdit` 输入流程的实现说明：覆盖 `QInputMethod` 请求为何不足、为何要回退到 `sm.puri.OSK0.SetVisible(true/false)`、`Controls::Keyboard` 的共享收口方式，以及 `EthConnectDialog / SaveFileDlg` 的接入与排障步骤。 |
| [minibar_wayland_layershell_debug_guide.md](minibar_wayland_layershell_debug_guide.md) | Helper-only Minibar LayerShellQt 交互与 Debug 的权威总入口：surface 角色、visual geometry、overlay outside-click、popup 首次映射前配置、Win32 回归根因与双平台回归矩阵。 |
| [minibar_wayland_layer_shell_qt_integration.md](minibar_wayland_layer_shell_qt_integration.md) | Linux / Raspberry Pi Wayland 下 helper 引入 vendored Qt5 `layer-shell-qt` 的实现说明、构建依赖、运行时验证和常见错误排查，并保留已删除 in-process host 的历史设计。 |
| [minibar_helper_layershell_parity_gaps.md](minibar_helper_layershell_parity_gaps.md) | `SGStudioMiniBar` 当前 layer-shell base/keyboard、Sweep/MOD/menu overlay、平台分流、popup ownership 与首次映射前配置契约。 |
| [minibar_popup_host_style_and_focus_status.md](minibar_popup_host_style_and_focus_status.md) | 已删除 in-process popup host 的精简历史结论：保留 host 视觉、Wayland overlay、native ownership 与关闭顺序约束。 |
| [WAYLAND_FRAMELESS_OVERLAY_PATTERN.md](WAYLAND_FRAMELESS_OVERLAY_PATTERN.md) | Wayland 下无边框弹窗：overlay + 遮罩模拟模态的推荐模式。 |
| [frameless_multimon_dpi_white_border_issue.md](frameless_multimon_dpi_white_border_issue.md) | 多屏/高 DPI 下 frameless 白边问题与处理经验。 |

## 部署与工程

| 文档 | 说明 |
| :--- | :--- |
| [linux_build_package_unified_entry.md](linux_build_package_unified_entry.md) | Linux 发布总入口与运行指南：250 的三目标、Raspberry Pi/RK3588 共用 AArch64 归档、应用内 QPA 自动选择和无需 launcher 的直接执行方式。 |
| [raspberry_pi_132_build_host_250_compatibility.md](raspberry_pi_132_build_host_250_compatibility.md) | 250 共用交叉包对 108 Raspberry Pi 与 RK3588 的 ABI、Qt、Wayland/xcb、运行库、直接启动和双端验收边界。 |
| [RPATH_MECHANISM.md](RPATH_MECHANISM.md) | Linux 部署：基于 `$ORIGIN` 的 RPATH 相对路径查找机制。 |
| [cmake_thirdparty_module_best_practices.md](cmake_thirdparty_module_best_practices.md) | 第三方依赖独立 CMake 模块的标准写法：`IMPORTED + INTERFACE + ALIAS`、Win32 快路径、跨平台惰性分支与部署边界。 |
| [cmake_build_output_clean_run_workflow.md](cmake_build_output_clean_run_workflow.md) | 本仓库 CMake 的 configure、build、运行目录布局、插件同步、清理边界与 VS Code / Qt Creator 使用流程总览。 |
| [runtime_layout_repo_root_vs_build_tree.md](runtime_layout_repo_root_vs_build_tree.md) | 正式说明 `SGS_RUNTIME_LAYOUT` 的双布局约束：为什么默认值保留给 Qt Creator 的 `build-tree`、为什么 VS Code 必须显式用 `repo-root`，以及 `plugin-runtime` staging 与 launch 前置保护的边界。 |
| [windows_build_bat_updater_packaging_hygiene.md](windows_build_bat_updater_packaging_hygiene.md) | Windows 下 `scripts/build.bat` 的 updater 打包依赖收敛、3rdParty 增量构建修复、当前验证结论，以及未来应优先修改的 CMake / 打包入口文件。 |
| [build_script_packaging_and_watermark_guide.md](build_script_packaging_and_watermark_guide.md) | 统一说明 Windows/Linux 打包脚本的职责、参数、`--rebuild` 缓存边界、水印开关，以及 `QuickWaveFormData` 与 Linux 启动脚本的归档布局。 |
| [vectorcore_bnc_en_branding_profile.md](vectorcore_bnc_en_branding_profile.md) | VectorCore `BNC_en` 定制版的 CMake profile、应用/更新包命名、九分辨率 Windows ICO 与 aarch64 PNG 资源、dark theme 色彩边界、在线更新选项的 UI 隐藏规则及三套打包命令。 |
| [windows_vscode_environment_pitfalls.md](windows_vscode_environment_pitfalls.md) | Windows / VS Code 环境下的常见非业务代码问题：`pwsh.exe` 提示与 Qt includePath / IntelliSense 缺失。 |
| [windows_qt_runtime_mixing_pitfall.md](windows_qt_runtime_mixing_pitfall.md) | Windows / CMake / vcpkg 混合环境下 Qt 运行时混装导致 SVG 图标失效的排查、根因与修复策略。 |

## 版本与发布

| 文档 | 说明 |
| :--- | :--- |
| [gui_update_log_2.6.md](gui_update_log_2.6.md) | SGStudio GUI 2.6.1 – 2.6.3.3 各版本更新日志：按 git 版本 bump 边界整理的 feat / fix / refactor 与依赖对齐记录。 |

## Git / 协作

| 文档 | 说明 |
| :--- | :--- |
| [Git_Best_Practices.md](Git_Best_Practices.md) | Git 基础约定与实践清单。 |
| [GIT_WORKFLOW_GUIDE.md](GIT_WORKFLOW_GUIDE.md) | 分支/合并/发布的工作流指南。 |
| [git_retag_force_push_and_checkout_workflow.md](git_retag_force_push_and_checkout_workflow.md) | 重建标签到目标提交、强制覆盖远端同名标签、切到标签核对并切回原分支的一套可复制命令。 |

## AI / Copilot 协作

| 文档 | 说明 |
| :--- | :--- |
| [copilot_prompt_workflow_notes.md](copilot_prompt_workflow_notes.md) | 自定义 prompt、仓库 instruction、TaskLog、KnowledgeBase 与 Copilot 默认分析流程的分工说明，并记录为何当前仓库更适合轻量入口 prompt，而不是直接照搬 VlppParser2 的重型多阶段体系。 |

## 坑位 (Pitfalls)

| 文档 | 说明 |
| :--- | :--- |
| [Pitfalls/Property_Binding_Duplicate_SoftKeyboard.md](Pitfalls/Property_Binding_Duplicate_SoftKeyboard.md) | 同一个全局 property 被多个控件或隐藏 panel 绑定时，`beginEditing` 被多个订阅者响应并打开重复软键盘的根因、排查和防回归规则。 |
| [Pitfalls/Qt_QSettings_INI_General_Group.md](Pitfalls/Qt_QSettings_INI_General_Group.md) | QSettings 的 `[General]` 解析陷阱与排查方式。 |
| [Pitfalls/Qt_CleanPath_IPC_Path_Separator.md](Pitfalls/Qt_CleanPath_IPC_Path_Separator.md) | Windows 下 `QDir::cleanPath()` 的 `/` 表示与 `QDir::separator()` 返回的 `\` 混用，导致根目录子文件被误判越界；包含统一路径比较、IPC 权威校验、取证顺序和跨平台回归矩阵。 |
| [Pitfalls/Qt_FocusProxy_SoftKeyboard_StepEdit.md](Pitfalls/Qt_FocusProxy_SoftKeyboard_StepEdit.md) | overlay + 软键盘场景下的焦点路由陷阱与修复手法。 |
| [Pitfalls/SoftKeyboard_MessageDialog_Reentrancy_And_Lifetime.md](Pitfalls/SoftKeyboard_MessageDialog_Reentrancy_And_Lifetime.md) | 2026-05 软键盘提交、步进、MessageDialog 时序与 focusWidget 生命周期问题的根因、已落地修复和快速排障清单。 |
| [Pitfalls/Property_Binding_Duplicate_SoftKeyboard.md](Pitfalls/Property_Binding_Duplicate_SoftKeyboard.md) | 记录同一个 `PropertySystem::IProperty` 被多个控件或隐藏 panel 绑定时，一次 `beginEditing` 触发多个订阅者并打开重复软键盘的根因、判断方法，以及 `isEditTriggerFromWidget(...)` guard 的适用边界。 |
