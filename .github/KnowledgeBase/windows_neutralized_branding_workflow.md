# Windows Neutralized Branding 工作流

本文说明本仓库当前 `neutralized` branding 的使用边界，特别是它与现有 VS Code build/stage task 的关系。

## 1. 先说结论

如果你先运行：

1. `CMake Build Release Neutralized`

然后再运行：

2. `CMake Stage Release`

当前**并不会**自动打出 neutralized 包。

如果要打 neutralized 包，当前应直接运行：

1. `CMake Stage Release Neutralized`

原因很直接：

- `CMake Build Release Neutralized` 使用的是单独 build tree：
  - `build/cmake-win-release-neutralized/`
- 现有 `CMake Stage Release` task 和 `.vscode/cmake-stage-release.ps1` 仍然固定指向：
  - `build/cmake-win-release/`

所以这两条链路当前不是同一个 build tree。

## 2. 当前 task 的职责边界

### 2.1 `CMake Configure Release Neutralized`

职责：

- 只把 neutralized branding 配置写入 `build/cmake-win-release-neutralized/`。
- 不编译，不打包。

### 2.2 `CMake Build Release Neutralized`

职责：

- 先 configure neutralized build tree。
- 再对 `build/cmake-win-release-neutralized/` 执行 `ALL_BUILD`。
- 产物是 neutralized build tree 下的编译结果。

它的职责仍然只是：

- 生成 neutralized 可执行文件和相关构建产物。

它**不等于** stage 打包。

### 2.3 `CMake Stage Release`

职责：

- 调用 `.vscode/cmake-stage-release.ps1`
- 重新 configure `build/cmake-win-release/`
- 对 `build/cmake-win-release/` 执行 `sgstudio_stage`

也就是说，当前这个 task 打包的是：

- 默认 official branding 的 Release build tree

而不是：

- neutralized build tree

### 2.4 `CMake Stage Release Neutralized`

职责：

- 调用 `.vscode/cmake-stage-release-neutralized.ps1`
- 重新 configure `build/cmake-win-release-neutralized/`
- 显式传入 `SGS_BRANDING_PROFILE=neutralized`
- 对 `build/cmake-win-release-neutralized/` 执行 `sgstudio_stage`
- 在 neutralized stage 包生成后，自动清理 `configuration/theme.css` 与 `configuration/theme_light.css` 中的所有注释

也就是说，这条 task 打包的是：

- neutralized branding 的 Release build tree

并且 stage 目录名会自动追加 `_neutralized` 后缀，避免与 official 包混淆。

额外说明：

- CSS 去注释后处理只作用于 neutralized stage 输出目录；
- 不修改源码目录下的 `configuration/` 文件；
- official stage 脚本当前不包含这一步。

## 3. 为什么不能混用

根因是 `SGS_BRANDING_PROFILE` 是 CMake cache 变量。

本仓库之所以把 neutralized 放到单独的 build tree，就是为了避免：

1. official 和 neutralized 共用一个 build tree
2. 来回 configure 后 cache 串味
3. 最终不知道某个 stage 包到底是哪种 branding

因此：

- 使用单独 build tree 是对的；
- 但也意味着 stage task 必须显式指向对应的 neutralized build tree，才能打出 neutralized 包。

## 4. 当前正确理解方式

当前工作流应理解为：

1. `CMake Build Release Neutralized`
   - 只负责 neutralized 版本的 configure + build
2. `CMake Stage Release`
   - 只负责 official 版本的 stage 打包
3. `CMake Stage Release Neutralized`
  - 负责 neutralized 版本的 configure + stage 打包

不能把 `CMake Build Release Neutralized` 和 `CMake Stage Release` 串起来理解成“先 build neutralized，再用默认 stage task 打 neutralized 包”。

## 5. 如果后续要打 neutralized 包，正确方向是什么

当前仓库已经补齐独立链路：

1. `CMake Stage Release Neutralized` task
2. `.vscode/cmake-stage-release-neutralized.ps1`

并且这条链路明确指向：

- `build/cmake-win-release-neutralized/`

而不是复用现有 `CMake Stage Release`。

## 6. 当前仓库状态

截至当前仓库状态：

- neutralized branding 的 configure/build 开关已经存在；
- VS Code 的 neutralized configure/build task 已存在；
- VS Code 的 neutralized 专用 stage task 已存在；
- neutralized stage 目录名会自动带 `_neutralized` 后缀。
- neutralized stage 完成后会自动移除 stage 包内两个主题 CSS 文件中的注释。

因此仍然不能把：

- `CMake Build Release Neutralized`
- `CMake Stage Release`

当成一条完整 neutralized 打包链路；应改用 `CMake Stage Release Neutralized`。

## 7. 推荐给维护者的原则

后续若要继续完善 neutralized 工作流，建议坚持下面三条：

1. official 与 neutralized 必须继续使用不同 build tree。
2. stage task 必须与 build tree 一一对应，不要跨 profile 复用。
3. 文档中必须明确写清“当前哪个 task 打 official，哪个 task 打 neutralized”，避免维护者误用。
4. neutralized 包名必须显式带 neutralized 标识，避免 stage 目录混淆。

## 8. 相关文件

- `.vscode/tasks.json`
- `.vscode/cmake-build-neutralized.ps1`
- `.vscode/cmake-stage-release.ps1`
- `.vscode/cmake-stage-release-neutralized.ps1`
- `src/CMakeLists.txt`
- `src/app/CMakeLists.txt`
- `src/app/main.cpp`
- `src/libs/controls/thememanager.cpp`