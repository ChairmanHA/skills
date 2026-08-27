# Qt `cleanPath()` 与 IPC 路径分隔符不一致陷阱

## 现象

Windows 下，minibar helper 的 Quick Waveform 列表可以显示根目录和 WAV 文件，但选择
文件后点击 Load 没有发出有效加载请求。相同逻辑在 Linux 上不一定复现。

本次已验证的失败表达式等价于：

```cpp
const QString cleanRoot = QDir::cleanPath(rootPath);
const QString cleanFile = QDir::cleanPath(filePath);
const QString rootPrefix = cleanRoot + QDir::separator();

if (!cleanFile.startsWith(rootPrefix, pathCase)) {
    // 误判为根目录之外
}
```

在本次 Windows / Qt 5 运行结果中：

```text
cleanRoot = C:/waveforms
cleanFile = C:/waveforms/example.wav
QDir::separator() = \
rootPrefix = C:/waveforms\
```

`cleanFile.startsWith(rootPrefix)` 必然为 `false`，于是根目录下所有 WAV 都被误判为越界。

## 根因

代码混用了两种不同目的的路径表示：

- `QDir::cleanPath()` 用于清理路径；本次 Windows 结果中的比较字符串使用 `/`。
- `QDir::separator()` 返回本机目录分隔符；Windows 为 `\`，Linux 为 `/`。

单独调用二者都没有问题，问题在于先得到一种规范化表示，再用另一种表示拼接前缀。
Linux 上两者碰巧都是 `/`，所以原实现能够通过；这只是平台巧合，不代表表达式具有
跨平台正确性。

## 正确的比较原则

路径比较应先选择一种内部表示，并让所有参与比较的操作数和边界分隔符都使用该表示。
本项目使用 `/` 作为跨平台内部比较格式：

```cpp
QString canonicalComparisonPath(const QString &path)
{
    return QDir::fromNativeSeparators(
            QDir::cleanPath(QFileInfo(path).canonicalFilePath()));
}

bool isWithinRoot(const QString &rootPath,
                  const QString &candidatePath,
                  bool allowRoot)
{
    const QString root = canonicalComparisonPath(rootPath);
    const QString candidate = canonicalComparisonPath(candidatePath);
    if (root.isEmpty() || candidate.isEmpty()) {
        return false;
    }

#ifdef Q_OS_WIN
    constexpr Qt::CaseSensitivity pathCase = Qt::CaseInsensitive;
#else
    constexpr Qt::CaseSensitivity pathCase = Qt::CaseSensitive;
#endif

    if (allowRoot && candidate.compare(root, pathCase) == 0) {
        return true;
    }

    const QString rootPrefix = root.endsWith(QLatin1Char('/'))
            ? root
            : root + QLatin1Char('/');
    return candidate.startsWith(rootPrefix, pathCase);
}
```

注意：

- 不能只判断 `startsWith(root)`。例如 `/data/waveforms-old/a.wav` 也以
  `/data/waveforms` 开头；必须包含目录边界 `/`。
- `root.endsWith('/')` 的分支用于正确处理 Windows 盘符根目录 `C:/` 和 Linux 根目录
  `/`，避免拼成 `C://` 或 `//`。
- Windows 文件路径通常按大小写不敏感比较；Linux 保持大小写敏感。平台分支只用于
 真实的平台文件系统语义。
- `canonicalFilePath()` 会解析 `.`、`..` 和符号链接，并且目标不存在时返回空字符串。
  因此上述实现适合“必须已经存在”的根目录和输入文件。
- 如果校验的是即将创建、当前尚不存在的目标文件，应规范化并校验其已存在的父目录，
  再单独验证文件名；不要把 `canonicalFilePath()` 返回空误解成路径不在根目录。
- `QDir::toNativeSeparators()` 适合显示给用户或传给要求本机格式的外部 API，不适合作为
  内部比较格式的一部分。

## IPC 路径约定

IPC 中的路径只是业务数据，不能把发送方字符串当作接收方已经验证过的文件系统事实。

推荐边界：

1. helper 可以做本地校验，用于及时禁用无效操作或避免发送明显错误的请求。
2. sender 可以使用 `/` 规范化路径以稳定日志和测试，但这不是安全保证。
3. JSON 编解码器负责 `\` 的转义；业务代码不要手工重复转义或反转义路径。
4. main 收到路径后必须重新执行存在性、canonical path、根目录边界、扩展名和业务权限
   校验；main/business 是最终权威边界。
5. receiver 不应依赖 sender 的操作系统、当前工作目录、分隔符格式或大小写习惯。
6. 错误响应应保留明确的验证原因，避免 helper 只表现为“点击无效”。

新增专用 IPC 命令不能修复发送前或接收后的路径误判。遇到 IPC 操作没有效果时，应先用
证据定位第一个缺失边界，不要先增加第二条同步通道或绕开既有 revision/state 语义。

## IPC 排障顺序

对文件类 intent 进行临时取证时，依次确认：

1. UI 是否生成了非空的选择路径。
2. helper 的本地根目录校验是否接受该路径。
3. client 是否真正编码并发送了请求；命令、request id 和 action 是否正确。
4. main 是否收到并成功解码同一条路径。
5. main 记录的 canonical root、canonical candidate、统一分隔符结果和比较结果是什么。
6. owning business 是否开始并最终提交了文件加载。
7. accepted response 和异步完成 snapshot 是否返回并被 helper 应用。

第一个没有出现的边界就是当前调查对象。只有日志证明现有协议不能表达用户意图时，才考虑
扩展 IPC；不要用协议改造掩盖 UI 本地校验、路径规范化或业务拒绝问题。

## 最小回归矩阵

| 场景 | Windows 预期 | Linux 预期 |
| :--- | :--- | :--- |
| 根目录下现有 WAV | 接受 | 接受 |
| `/`、`\` 或混合分隔符输入 | 规范化后接受 | 按有效路径规范化后接受 |
| 与根目录同前缀的兄弟目录，如 `waveforms-old` | 拒绝 | 拒绝 |
| `..` 解析后仍在根目录内 | 接受 | 接受 |
| `..` 或符号链接解析到根目录外 | 拒绝 | 拒绝 |
| 仅大小写不同的现有路径 | 按 Windows 语义比较 | 按 Linux 语义比较 |
| 不存在的输入文件 | 拒绝 | 拒绝 |
| 根目录本身 | 由 `allowRoot` 决定 | 由 `allowRoot` 决定 |

如果产品支持 UNC、挂载点或网络文件系统，还应增加对应环境的 canonical path 和大小写
行为测试，不能直接套用本机磁盘假设。

## 本次落地点

- `RemoteQuickWaveformPanel::isWithinRoot()`：helper 的本地浏览/选择校验。
- `QuickWaveformBusiness::applyRemoteEditorAction()`：main business 的权威文件校验。

两处都把 canonical/clean path 通过 `QDir::fromNativeSeparators()` 转成 `/` 表示，并使用
`QLatin1Char('/')` 构造目录边界。helper 校验用于交互反馈，business 校验不能省略。
