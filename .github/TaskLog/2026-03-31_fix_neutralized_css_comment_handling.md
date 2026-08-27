# Neutralized CSS 注释清理修复

## 问题
- neutralized 打包阶段会对 `configuration/theme.css` 和 `configuration/theme_light.css` 做注释清理。
- 当前源码中存在多处非标准 CSS 块注释写法，例如同一行重复 `/*`、跨行未闭合注释、半注释掉的规则块。
- 当前脚本在 Windows PowerShell 5.1 下使用默认编码读取 CSS，会把 UTF-8 中文按本地码页错误解码。
- 两个问题叠加后，neutralized 产物中的 QSS 会出现规则缺失、花括号丢失、残留乱码注释，最终导致运行时样式异常。

## 根因
1. 源码注释不满足标准 `/* ... */` 结构，正则清理时会跨规则吞掉正常样式。
2. `cmake-stage-release-neutralized.ps1` 没有显式使用 UTF-8 读写 CSS，处理中文注释时不稳定。

## 修复策略
1. 先把两个源 CSS 中所有异常块注释改成标准 CSS 注释。
2. 保留 neutralized 阶段的注释清理逻辑，但显式改为 UTF-8 读写。
3. 重新执行 neutralized 打包，检查产物中的 CSS 是否还存在注释残片或规则缺失。
4. 运行 neutralized 产物，确认样式恢复正常。