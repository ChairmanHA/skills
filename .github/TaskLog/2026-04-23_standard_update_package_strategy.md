# 2026-04-23 标准版 Install Update 包内容与覆盖策略

> 过时说明
>
> - 本文记录的是 2026-04-23 时点的方案判断，已不代表当前 CMake 活跃实现。
> - 当前代码事实请以 `.github/TaskLog/2026-04-24_updater_current_impl_doc_sync.md` 与 `.github/KnowledgeBase/updater_firmware_update_mechanism.md` 为准。
> - 若本文与当前源码冲突，应视为历史方案而非当前行为说明。

## 目的

在当前链路已经完成：

`主程序安全退出 -> maintenance 提权 -> 固件 updater 成功 -> 首次重启动提示`

之后，补齐最后一步：

`固件 updater 成功 -> maintenance 执行 install update -> 更新主程序/dll/plugin -> 写 update session -> 重启主程序`

本文只讨论标准版，不考虑俄版、neutralized 等衍生包。

## 当前仓库已知事实

### 1. 当前 update 包根目录契约

现有 `PacketSpec` 仍支持以下根目录约定：

- `version.json`
- `releasenote.txt`
- `updater/Updater*.exe`
- `bin/maintenance.exe`

当前已确认：下一步标准版 install update 可以直接由 maintenance C++ 实现，不必再保留“包内 bat 再反调安装逻辑”的机制。

### 2. 当前 stage 产物并不等于“可直接用于 install update 的包”

当前 CMake stage 会整体安装：

- `bin/`
- `plugin/`
- `configuration/`
- `fonts/`
- `bin/CalFile/`

其中：

- `data/` 目录是用户可写目录，当前不适合整目录覆盖更新。
- `configuration/Settings.ini` 既有产品默认值，也有用户可能改过的内容，不能直接覆盖。
- `configuration/Profile.json`、`configuration/DeviceHistory.json` 是用户运行期数据，不能直接覆盖。
- `bin/**/*.lic` 是用户现场许可证，不能碰。
- `bin/CalFile/132.ini` 属于产品随版本演进的可更新文件，应保留并覆盖。
- `bin/CalFile/132_ff00aa00_txcal_*` 属于通用校准文件，本阶段不应随包分发，也不应在安装时碰用户现场文件。

结论：标准 install update 包必须是“从正式 stage 再裁剪/重组出来的更新包”，不能直接拿 stage zip 原样下发。

## 推荐的标准更新包内容

推荐标准更新包根目录如下：

```text
<packageRoot>/
  version.json
  releasenote.txt
  updater/
    Updater_Win.exe
    ...
  bin/
    SGStudio.exe
    maintenance.exe
    *.dll
    qt runtime...
    CalFile/
      132.ini
  plugin/
    *.dll
  configuration/
    device_info.xml
    Language.xlsx
    theme.css
    theme_light.css
    branding/
  fonts/
    ...
  install-manifest.json
```

### 关键约束

- 包内保留 `bin/maintenance.exe`，保证未来 maintenance 可随包演进。
- 包内 `bin/maintenance.exe` 只作为本次更新流程的执行器存在，不属于客户安装目录的产品文件；install phase 不应把它复制到安装目录。
- 包内不再要求 `install.bat`。
- 包内不分发 `configuration/Settings.ini`，也不再分发 `Settings.defaults.ini`。
- 包内不包含 `data/`。
- 包内不包含任何 `*.lic`。
- 包内不包含 `132_ff00aa00_txcal_*`。
- 包内保留 `bin/CalFile/132.ini`，它属于产品随版本演进的可更新文件。

## 文件所有权与更新策略

### 一. 直接同名覆盖的产品文件

这些文件由产品版本拥有，应以新包内容覆盖安装目录中的同名文件：

- `bin/SGStudio.exe`
- `bin/*.dll`
- `bin/plugins/**`
- `plugin/*.dll`
- `fonts/**`
- `configuration/device_info.xml`
- `configuration/Language.xlsx`
- `configuration/theme.css`
- `configuration/theme_light.css`
- `configuration/branding/**`
- `bin/CalFile/132.ini`

这里推荐的策略是：

- 以“同名覆盖”为主。
- 不要先删整个 `bin/`、`plugin/`、`configuration/` 再整体复制。

原因：

- 整目录先删会误删用户侧保留文件。
- 现有运行目录里已经混有用户现场文件和产品文件，粗暴删除风险过高。
- 只要 maintenance 已经等待主程序彻底退出，绝大多数 exe/dll 可以安全同名替换。

### 二. 只允许按 manifest 定点删除的废弃文件

单纯“同名覆盖”无法处理历史遗留的废弃 dll/plugin。为避免旧文件长期残留，推荐增加：

- `install-manifest.json` 中的 `remove` 列表

这里的“manifest 驱动覆盖”需要收敛理解，不是第一版就把所有要复制的文件逐条写进 manifest。

第一版更合适的定义是：

- 同名覆盖的来源，仍然直接来自更新包里的 `bin/`、`plugin/`、`configuration/`、`fonts/`
- `install-manifest.json` 只负责描述安装决策里那些“无法靠目录拷贝自然表达”的规则
- 当前第一版最需要的就是 `remove`，用于清理已经废弃、但包内不会再出现的旧文件

也就是说，第一版不是“manifest 枚举所有覆盖动作”，而是：

- 包内容决定正常覆盖
- manifest 决定额外删除和后续可扩展的例外规则

例如：

```json
{
  "formatVersion": 1,
  "remove": [
    "plugin/LegacyFoo.dll",
    "bin/obsolete_runtime.dll"
  ]
}
```

因此 `install-manifest.json` 在第一版里的角色，可以理解成：

- 一个安装策略补充文件
- 当前最小只需要 `formatVersion + remove`
- 后续若真有必要，再扩展 `preserve`、`migrate` 等更复杂规则

处理原则：

- 只删除 manifest 明确列出的产品文件。
- 不做目录级清空。
- 不做通配符删除用户目录。

这能同时满足：

- 常规升级走同名覆盖
- 需要退场的旧 dll/plugin 能被有控制地清理

### 三. 必须保留、绝不能动的用户文件

这些内容必须保留，不应由更新包覆盖，也不应被删除：

- `data/**`
- `configuration/Settings.ini`
- `configuration/Profile.json`
- `configuration/DeviceHistory.json`
- `bin/**/*.lic`
- `bin/CalFile/**/*.lic`

另外，以下文件本阶段也不应随包分发：

- `bin/CalFile/132_ff00aa00_txcal_iq.txt`
- `bin/CalFile/132_ff00aa00_txcal_rf.txt`

如果用户现场未来存在这些文件，也应保持不动。

## 对 dll 的建议结论

结论很明确：

- 普通 dll 采用同名覆盖。
- 不采用“先删整个运行目录再粘贴”的策略。
- 只对 `install-manifest.json` 中列出的 obsolete 文件做定点删除。

这是当前项目结构下风险最低、后续也最可维护的做法。

## Settings.ini 的推荐处理方式

### 结论

`Settings.ini` 不应由更新包覆盖，也不建议再走 `Settings.defaults.ini + merge` 这套机制。

推荐做法是：

1. 标准更新包不携带 `configuration/Settings.ini`
2. maintenance 在 install 阶段直接就地修正现有 `configuration/Settings.ini`
3. 只处理当前确认仍有意义、且需要规范化的少量键
4. 再继续其余文件更新与重启

### 为什么不再走 defaults merge

因为当前 `Settings.ini` 同时承载了两类信息：

- 用户偏好/现场配置，例如 `APP/Language`、`APP/Theme`、`APP/StartProfileType`、`APP/StartSetting`
- 一些只应作为运行期临时状态的键，例如 `APP/Reboot`、`APP/PendingRestart`
- 一些历史兼容或联调键，例如 `Device/FixedLic`
- 用户或现场可能自行维护的 `Update/*`
- 可能在调试版存在、正式版不应依赖的 `Debug/*`

把整份文件当成“产品默认文件”去 merge，收益已经很低，反而会引入：

- 谁拥有 `Update/*` 的语义歧义
- `Debug/*` 是否应进入正式版的歧义
- 对历史残留键做无意义迁移

因此下一步更合理的策略是：

- `Settings.ini` 视为用户现场文件
- maintenance 只做最小、明确、可解释的就地规范化

### 推荐的就地规范化原则

只处理当前已经明确的键，不做整文件 merge。

#### 一. 保留用户已有值的键

- `APP/Language`
- `APP/Theme`
- `APP/StartProfileType`
- `APP/StartSetting`

#### 二. 强制修正为稳定值的键

- `APP/Reboot=false`
- `APP/PendingRestart=false`

这两个键是运行期状态，不应作为更新后保留的用户配置。

#### 三. 明确不再写入标准发布版的键

- `Device/FixedLic`

该键当前仍会被 Analog 许可证逻辑读取，但它本质是联调/绕过证书用的历史开关，不应继续作为标准发布版配置的一部分。

#### 四. 明确不碰的键/分组

- `Update/*`
- `Debug/*`

处理原则：

- 现有用户文件里如果有，就保持原样
- 更新流程不覆盖、不重写、不删除
- 正式版后续即使不再提供 `Debug/*`，也不需要由更新流程主动清理

### 当前建议的保留范围

第一阶段建议 maintenance 只关心以下键：

- 确保存在并保留：`APP/Language`
- 确保存在并保留：`APP/Theme`
- 确保存在并保留：`APP/StartProfileType`
- 确保存在并保留：`APP/StartSetting`
- 强制写回：`APP/Reboot=false`
- 强制写回：`APP/PendingRestart=false`

其他键：

- 不作为本次 install update 的迁移目标
- 后续若某个新版本真的引入新配置需求，再加显式 migration 代码，而不是恢复整份 defaults merge

### 不建议的做法

- 不建议重新引入 `Settings.defaults.ini` 再做整文件 merge。
- 不建议让脚本逐行拼 INI。
- 不建议为了清理历史键去重写整份 `Settings.ini`。

### 推荐实现位置

这个就地规范化逻辑更适合放在 maintenance 的 C++ 中：

- Qt 里已经在大量使用 `QSettings`
- 便于做键级别保留和强制写回
- 便于把修正日志直接输出到 ProgressDialog
- 失败时可以精确中止 install update，而不是只得到一个脚本退出码

## 推荐的安装执行顺序

1. firmware updater 返回 0
2. maintenance 解析 `install-manifest.json`
3. maintenance 就地规范化 `configuration/Settings.ini`
4. maintenance 执行 install update：
   - 覆盖产品拥有文件
   - 跳过 preserve 文件
   - 删除 manifest 列出的 obsolete 文件
5. install update 返回 0 后，maintenance 写 `update-session.ini`
6. maintenance 拉起更新后的 `SGStudio.exe`

## 暂不处理的问题

当前先不处理外部进程锁住以下文件导致替换失败的问题：

- `configuration/theme.css`
- `configuration/theme_light.css`
- `configuration/Language.xlsx`

原因：

- 这属于文件锁/共享语义层面的额外问题
- 当前实现目标应先收敛到 install phase 主链路跑通
- 若后续现场真的出现 sharing violation，再单独引入重试、锁检测或 Restart Manager 方案

因此下一步实施时，先按“主程序退出后正常覆盖”处理，不把这块复杂度提前带入 install update 主方案。

## 对当前工程的直接落地建议

### 打包侧

新增一个“标准 update package”组包步骤，不直接复用 stage zip 原样发布。

另外，普通 `CMake Stage Release` 产物应继续只代表主程序正式 stage，不应夹带 maintenance。

约束收敛为：

- 标准 `sgstudio_stage` / `CMake Stage Release` 不安装 `bin/maintenance.exe`
- maintenance 仅通过独立 `sgstudio_stage_maintenance` / `CMake Stage Maintenance` 产出
- 这样普通 release stage 仍可作为“主程序正式产物”来源，后续 update 包由人工或单独脚本重组时，再按需补入 maintenance 与 updater

组包时应做这些裁剪：

- 删除 `data/`
- 删除 `configuration/Settings.ini`
- 删除 `configuration/Profile.json`
- 删除 `configuration/DeviceHistory.json`
- 删除所有 `*.lic`
- 删除所有 `132_ff00aa00_txcal_*`
- 保留 `bin/CalFile/132.ini`
- 添加 `install-manifest.json`
- 添加/保留 `version.json`、`releasenote.txt`、`updater/`

### 代码侧

maintenance 增加 install phase，不再在 updater 成功后直接 `writePostUpdateSession() + restartApplication()`。

需要新增的核心能力：

- 一个统一的子进程输出转发封装，能同时用于 updater 和 install phase
- 一个 `Settings.ini` normalize helper
- 一个安装文件过滤/删除策略执行器
- 收缩 `installScript` 残留依赖：从 `UpdatePostSession`、maintenance session 写入逻辑和 install 主链路中移除，仅保留 `PacketSpec` 的旧包兼容识别

### 本次实现落点

本次代码实现按下面的最小闭环推进：

1. `maintenance/ProgressDialog` 在 firmware updater 成功后，不再直接 `writePostUpdateSession() + restartApplication()`。
2. maintenance 同进程串行执行：
  - 解析 `install-manifest.json` 的 `formatVersion + remove`
  - 就地规范化安装目录 `configuration/Settings.ini`
  - 从包根递归覆盖 `bin/`、`plugin/`、`configuration/`、`fonts/`
  - 仅覆盖允许的产品文件，显式跳过 `bin/maintenance.exe`、`Settings.ini`、`Profile.json`、`DeviceHistory.json`、所有 `*.lic`、所有 `132_ff00aa00_txcal_*`
  - 额外清理安装目录中历史残留的 `bin/maintenance.exe`，避免后续更新误起旧 maintenance
  - 定点删除 manifest `remove` 列表中的 obsolete 文件
3. install phase 成功后才写 `update-session.ini` 并拉起更新后的 `SGStudio.exe`。
4. `UpdatePostSession` 删除 `installScript` 字段，maintenance 不再探测或写入 `InstallScript`。
5. `PacketSpec` install 主链路不再消费 `install.bat`。

## 最终建议摘要

### 更新包里应该包含什么

- 包含：程序 exe、dll、plugin、fonts、产品拥有的 configuration 文件、`bin/CalFile/132.ini`、maintenance、自身 metadata、firmware updater
- 不包含：`data/`、`Profile.json`、`DeviceHistory.json`、任何 `*.lic`、任何 `132_ff00aa00_txcal_*`

### dll 怎么更新

- 走同名覆盖
- 不走整目录先删后贴
- 仅对 manifest 指定的 obsolete 文件定点删除

### Settings.ini 怎么处理

- 不直接覆盖
- 包内不提供 `Settings.ini` / `Settings.defaults.ini`
- maintenance 用 C++ 对现有文件做小范围就地规范化：保留 `Language/Theme/StartProfileType/StartSetting`，强制 `Reboot=false`、`PendingRestart=false`，不碰 `Update/*` 和 `Debug/*`

这是当前项目结构下最稳妥、最容易演进到量产更新链路的方案。