# 恢复 Local Default 仅固件更新

## 范围

- 恢复 UpdateDialog 中位于 `Latest Online` 与 `Local File` 之间的 `Local Default` 选项。
- 复用当前安装目录自带的 updater、maintenance 和 `version.json`。
- Local Default 只运行固件 updater，关闭并重启 SGStudio，不进入软件包目录备份或复制。
- 不改变在线手动下载、本地归档更新、下载缓存清理或 CopyThread 的现有行为。

验证级别：`static`

## 历史证据

- `e8d85d48` 明确删除了 `radioLocalDefault`，并把来源 ID 从历史的
  `1=Online / 2=Local Default / 3=Local File` 压缩为两个选项。
- 删除前的 `selectedPacketSpec()` 在 ID 2 返回 `m_nativelFile`。
- 自 `6c1c987a` 起，`buildMaintenanceArguments()` 仅在
  `spec != m_nativelFile` 时附加 `folderPath + currentFolderPath`。
- maintenance 只有收到这两个额外路径时才设置 `m_needCopyFile=true` 并创建 `CopyThread`；
  未收到时，固件 updater 成功后直接在当前 `bin/` 中查找 SGStudio 并重启。
- `c11f847a` 后增加了下载/解压工作区 handoff。`m_nativelFile` 没有 scratch workspace，
  因此 Local Default 必须跳过该缓存交接步骤。

## 成功标准

1. 来源顺序固定为 Online、Local Default、Local File，ID 分别为 1、2、3。
2. 选择 Local Default 时隐藏本地文件路径和 Download 按钮，显示运行时
   `version.json` 解析出的目标固件信息。
3. Local Default 的 maintenance 参数不包含包源目录和安装目标目录。
4. Local Default 不调用 `preserveScratchDataForHandoff()`，但在线与本地归档仍保持原交接行为。
5. maintenance 因参数不足 8 项而保持 `m_needCopyFile=false`，不会创建 `CopyThread`、
   不会备份或覆盖安装目录；固件 updater 成功后重启当前 SGStudio。
6. UI XML 可解析，Git diff 静态检查通过。

## 实施结果

- 已恢复 `radioLocalDefault`，来源顺序和 ID 为 Online=1、Local Default=2、Local File=3。
- Local Default 重新选择 `m_nativelFile`，隐藏本地文件路径并显示安装根元数据。
- native 源继续由既有 `spec != m_nativelFile` 条件排除复制源/目标参数。
- Local Default 跳过 scratch handoff；在线和本地归档路径未改变。
- 已静态确认 maintenance 仍只在收到两个可选目录参数时创建 `CopyThread`。
- UI XML、5 份语言工作簿中的既有 `Local Default` 文案和 `git diff --check` 均通过检查。
- 按仓库默认规则未执行编译或运行验证。
