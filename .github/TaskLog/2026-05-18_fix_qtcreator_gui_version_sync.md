# Qt Creator GUI 版本不同步修复

## 问题
- 根 CMake 中把 `SGS_VERSION` 作为 cache 变量维护。
- Qt Creator 复用既有 build tree 时，旧的 `SGS_VERSION` cache 会覆盖源码里的新默认值。
- 结果是：即使把源码改到 `2.6.0`，Qt Creator 重新构建后编译期宏仍可能保留旧值，`DeviceInfoWidget` / `AboutDialog` 显示旧 GUI 版本。

## 已确认事实
- GUI 版本文本来自编译期宏 `SGS_VERSION`，不是运行时配置。
- Qt Creator 当前使用独立 build tree：`build/Qt_5_15_9_msvc2022_64-Debug` / `Release`。
- 仓库已有打包脚本与任务围绕 `SGS_PROJECT_VERSION`、`SGS_PACKAGE_VERSION_LABEL`、`SGS_PACKAGE_TIMESTAMP` 设计，但当前顶层 CMake 未接入这条变量链。

## 修复目标
- 默认软件版本跟源码走，而不是被旧 cache 粘住。
- 显式传入 `SGS_PROJECT_VERSION` 时仍可覆盖编译期 GUI 版本，兼容现有 stage / interactive workflow。
- 让顶层 CMake 的版本入口与既有脚本、文档保持一致。

## 计划
1. 在顶层 CMake 引入单一默认版本常量，并将 `project(... VERSION ...)` 与 GUI 编译期版本统一到该来源。
2. 增加 `SGS_PROJECT_VERSION` / `SGS_PACKAGE_VERSION_LABEL` / `SGS_PACKAGE_TIMESTAMP` cache 入口。
3. 用“effective software version”生成 `SGS_VERSION` 编译宏，不再直接依赖旧 `SGS_VERSION` cache。
4. 保留其他现有打包/运行时变量不动，避免扩大改动面。
