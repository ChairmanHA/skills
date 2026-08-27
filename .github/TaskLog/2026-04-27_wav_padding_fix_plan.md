# WAV 自定义 chunk 偶数字节补齐修复计划

## 现象

- 数字调制 wav 会在 `data` chunk 之前写入自定义 `prof` chunk。
- 当前写入逻辑未对奇数长度 chunk 做 RIFF/WAV 约定的偶数字节补齐，导致仓库导出的 wav 不能稳定被标准 Python `wave` 等解析器读取。

## 局部假设

- 根因位于 `Utils::WavHeader`：`toByteArray()` / `wavToByteArray()` 原样拼接 `otherChunks`，`readWavHeader()` 读未知 chunk 时也未跳过 pad byte，因此仓库内部读写自洽，但与标准解析器不兼容。
- 只要在 `Utils::WavHeader` 内部统一做到“写未知 chunk 后对 odd size 自动补 1 字节、读未知 chunk 时兼容跳过该 pad byte”，业务侧 `digitalmodulation.cpp` 无需感知此细节。

## 最小实现

- 在 `wavheader.cpp` 增加私有 helper，标准化 unknown chunk 的写入与读取推进。
- 保持 `prof` payload 本身不变，只修 RIFF 容器层的补齐。

## 校验

- 先做一次 Debug 构建，确认 `Utils` 相关改动编译通过。
- 如构建通过，则文档里关于“必须手写无补齐解析”的内容后续可以显著简化或删除。