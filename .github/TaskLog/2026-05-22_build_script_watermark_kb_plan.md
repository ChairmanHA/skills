# 构建打包脚本文档入知识库计划

## 目标

1. 为 `scripts/build.bat`、`scripts/build_all.bat`、`scripts/build.sh` 补一篇详细知识库文档。
2. 重点说明脚本入口职责、参数语义、`--rebuild` 与缓存边界、`--watermark` 开关、典型调用示例。
3. 更新 `.github/KnowledgeBase/Index.md`，让文档进入“部署与工程”索引。

## 本地假设

1. 现有 `windows_build_bat_updater_packaging_hygiene.md` 更偏历史问题与依赖收敛，不适合继续塞入完整使用说明。
2. 新增一篇面向“日常操作手册”的文档更清晰，索引挂到“部署与工程”即可。
3. 用户要的是“针对这些编译打包脚本”的统一说明，因此应覆盖 Windows 单包、Windows 五包矩阵、Linux 单包三条路径，而不是只写某一条脚本。

## 计划

1. 新增一篇知识库文档，整理脚本职责、参数、缓存行为、水印开关和推荐调用方式。
2. 在 `Index.md` 的“部署与工程”区补一条入口说明。
3. 用静态检查确认新增 Markdown 文件没有异常诊断。