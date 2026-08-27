# Win32 更新内容对话框底边蓝线修正

## 范围

- 修正更新完成后 `ReleaseNotesDialog` 的只读发布说明文本框在 Win32 下显示蓝色底边的问题。
- 保持树莓派 aarch64 当前正常的边框、滚动和文本选择行为。
- 不修改尚未接入当前 maintenance 重启路径的 `PostUpdateDialog`。

验证级别：`static`

## 观察与判断

- 截图标题和固定尺寸对应 `src/plugins/core/ReleaseNotesDialog`；当前更新完成路径由
  `MainWindow` 在收到 `--UpdateCompleted` 后创建该对话框。
- `ReleaseNotesDialog::releaseNote` 没有专用 QSS 边框规则，Win32 原生样式在文本框获得焦点时
  会使用系统蓝色强调色绘制底边。
- 现场确认树莓派上的底边与外围边框颜色一致，因此不应隐藏底部区域或关闭滚动条，只需要
  消除平台原生焦点边框的颜色差异。
- 各发布主题已经统一使用 `#626973` 作为更新相关输入控件边框色。

## 设计与成功标准

1. 在全部 10 份随包发布的深色/浅色主题中，为
   `ReleaseNotesDialog QPlainTextEdit#releaseNote` 增加 `1px solid #626973` 专用边框。
2. 选择器必须同时包含实际对话框类和文本框对象名，不依赖 `setupUi()` 所接收的
   `m_contentWidget` 对象名，也不影响 UpdateDialog 中同名的
   `releaseNote` 或其他 `QPlainTextEdit`。
3. 不增加 `Q_OS_WIN` 分支；两平台使用同一 QSS，Win32 不再暴露系统蓝色焦点底边，
   树莓派继续显示统一边框。
4. 不改变 scrollbar policy、line wrap、焦点、文本选择或发布说明加载逻辑。
5. 10 份主题均只出现一条专用规则，`git diff --check` 通过。

## 实施结果

- 全部 10 份发布主题已增加类级、对象级双重限定的发布说明边框规则。
- 未修改 `ReleaseNotesDialog` 源码、UI 布局、滚动条策略或焦点行为。
- 已静态确认选择器能够命中实际 `ReleaseNotesDialog` 类及 `releaseNote` 对象，
  且不会命中 UpdateDialog 的同名文本框。
- 主题花括号平衡、规则唯一性和 `git diff --check` 均通过。
- 按仓库默认规则未执行编译或运行验证。
