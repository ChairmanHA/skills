# Neutralized CSS 注释清理执行记录

## 目标
- 检查 `configuration_files/standard_cn/theme.css` 与 `configuration_files/standard_cn/theme_light.css` 中的注释是否符合标准 CSS 块注释语法。
- 仅在发现非标准注释时做最小修正。
- 运行 `.vscode/cmake-stage-release-neutralized.ps1`，让 neutralized stage 产物中的主题文件删除全部注释。

## 本地假设
- 当前两个源主题文件中的注释已经满足标准 `/* ... */` 语法，脚本的 `Remove-CssComments` 不会因注释结构异常而误吞样式规则。

## 判别检查
- 机械扫描两个文件，确认：
  - 不存在 `//` 注释；
  - `/*` 与 `*/` 数量成对；
  - 不存在含额外 `/*` 或 `*/` 的异常块注释结构。

## 执行策略
1. 若发现非标准注释，先在源文件中修正为标准 CSS 块注释。
2. 若未发现异常，不改源文件。
3. 运行 neutralized stage 脚本。
4. 验证 stage 产物中的 `theme.css` 与 `theme_light.css` 已无注释残留。
