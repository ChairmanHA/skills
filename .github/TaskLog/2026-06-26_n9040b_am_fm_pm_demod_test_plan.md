# N9040B AM/FM/PM 解调测试文档计划

Scope:

- 新增一份面向 Keysight N9040B 的 AM/FM/PM 解调测试文档。
- 文档目标是指导使用者用 9040B 的 AM/FM/PM demod 功能验证 SGStudio 当前 AM/FM/PM 输出语义。
- 文档覆盖接线、仪器准备、通用设置、AM/FM/PM 推荐用例、结果判读和常见误判。
- 不涉及 SCPI 自动化、仪器截图采集脚本或产品规格承诺。

Assumptions:

- 用户手头的 N9040B 已具备进入 AM/FM/PM demod 测量界面的功能，但不同固件或 license 下菜单名称可能略有差异。
- 当前仓库中 AM/FM/PM 的被测语义分别以
  - `.github/KnowledgeBase/am_baseband_self_implementation_alignment.md`
  - `.github/KnowledgeBase/fm_baseband_self_implementation_alignment.md`
  - `.github/TaskLog/2026-06-18_pm_baseband_generation_algorithm_design.md`
  为准。
- 本轮文档以静态整理为主，不额外要求编译、运行或仪器联机验证。

Success criteria:

- 形成一份可直接交给测试人员执行的 Markdown 文档。
- 文档明确区分 AM、FM、PM 在 9040B 上应观察的主结果量。
- 文档给出至少一组默认推荐用例和一组 shape 方向验证用例。
- 文档说明 PM 使用度与弧度换算，以及等效频偏对解调带宽设置的影响。
- `.github/KnowledgeBase/Index.md` 增加该文档入口。

Verification level: static.

Implementation plan:

1. 复用现有 N9040B Pulse 文档中的接线、预热、输入保护和仪器准备写法。
2. 结合 AM/FM/PM 现有算法文档，整理成以 demod time trace 和数值读数为核心的测试流程。
3. 将最终测试文档放入 `.github/KnowledgeBase/`，并更新 `Index.md`。