# Linux RPATH 机制与打包发布指南（$ORIGIN / RUNPATH）

本文档聚焦 Linux 下的 `$ORIGIN`/RUNPATH（常被称作 RPATH）机制与打包发布要点；工程拆分与输出目录的总约定请优先参考 `PROJECT_STRUCTURE_AND_PACKAGING.md`。

## 1. 工程模块与依赖关系（与 RUNPATH 相关）

为了实现模块解耦和更好的依赖管理

## 2. Linux 打包发布标准结构（必须遵循）

`.pro` 文件经过配置，会根据操作系统将编译产物输出到特定的目录，以模拟最终的发布结构。

### Windows
所有产物均输出到 `bin` 目录，以便 `.exe` 能直接加载同级目录下的 `.dll`。
*   `bin/vna-gui-app.exe`
*   `bin/vna_api.dll`
*   `bin/RsVisa32.dll` (需拷贝或位于系统路径)

### Linux
遵循 Linux 标准目录规范，将库文件分离到 `libs`。
*   `bin/vna-gui-app`
*   `libs/libvna_api.so`
*   (第三方库通常在打包时处理)

## 3. Linux 打包发布标准结构

**必须**遵循以下目录结构进行打包（例如 AppImage, .deb, 或 .tar.gz），以确保 RPATH 设置生效：

```text
install_root/
  ├── bin/
  │    └── vna-gui-app      (可执行主程序)
  ├── libs/
  │    └── libvna_api.so    (我们的中间件库)
  └── 3rd/
       └── librsvisa.so     (第三方依赖库)
```

## 4. QMAKE_LFLAGS 与 RPATH 机制详解

在 Linux 下，为了让程序在没有安装到 `/usr/lib` 的情况下也能找到依赖库（即“绿色运行”），我们利用 `$ORIGIN` 变量配置了 `RUNPATH`。

### 3.1 主程序配置 (`app.pro`)

App 位于 `bin/`，需要加载位于 `libs/` 的 `libvna_api.so`。

```qmake
unix:!macx {
    # 告诉加载器：去可执行文件所在目录($ORIGIN)的兄弟目录 ../libs 下找库
    QMAKE_LFLAGS += -Wl,-rpath,\'\$$ORIGIN/../libs\'
}
```
*   **运行时解析**：`bin/vna-gui-app` 启动 -> `$ORIGIN` 解析为 `/path/to/bin` -> 搜索 `/path/to/bin/../libs` -> 找到 `libvna_api.so`。

### 3.2 API 库配置 (`vna_api.pro`)

`libvna_api.so` 位于 `libs/`，需要加载位于 `3rd/` 的 `librsvisa.so`。
为了同时支持**发布环境**（库在独立目录）和**开发环境**（源码编译目录），我们配置了两条路径：

```qmake
unix:!macx {
    # 路径 1: 发布环境 (Deployment)
    # 当 libvna_api.so 位于 install_root/libs/ 时，去 ../3rd 找依赖
    QMAKE_LFLAGS += -Wl,-rpath,\'\$$ORIGIN/../3rd\'

    # 路径 2: 开发环境 (Development)
    # 当 libvna_api.so 还躺在源码的构建目录或 bin 目录时，去源码树的 3rd/RsVisa/lib 找
    QMAKE_LFLAGS += -Wl,-rpath,\'\$$ORIGIN/../3rd/RsVisa/lib\'
}
```

### 1.1 原理机制 (Runtime Resolution)

*   **`$ORIGIN` 是什么？**
    `$ORIGIN` 是 Linux 动态链接器 (`ld.so`) 识别的一个特殊变量。它代表**当前正在执行的程序所在的目录**。
    
*   **工作流程：**
    1.  当用户启动程序 `app` 时，操作系统加载器会检查程序头部的 `RUNPATH` 或 `RPATH` 字段。
    2.  如果发现路径中包含 `$ORIGIN`，加载器会将其替换为 `app` 文件的实际路径。
    3.  然后加载器根据拼接后的路径（例如 `/path/to/app/../libs`）去查找依赖的 `.so` 库文件。

### 1.2 语法详解：为什么要写得这么复杂？

你可能注意到代码中有很多反斜杠和引号：`-Wl,-rpath,\'\$$ORIGIN/../libs\'`。这是为了让 `$ORIGIN` 这个字符串能够“存活”过编译系统的层层解析，最终原封不动地写入二进制文件。

这是一个“过五关斩六将”的过程：

1.  **QMake 解析层 (`.pro` -> `Makefile`)**
    *   `\$$`：告诉 QMake 这是一个普通的 `$` 字符，而不是变量引用的开始。
    *   `\'`：告诉 QMake 这是一个普通的单引号 `'`，而不是字符串的结束符。
    *   **结果**：Makefile 中生成了 `-Wl,-rpath,'$ORIGIN/../libs'`。

2.  **Shell 执行层 (`make` -> `g++`)**
    *   当 `make` 调用 Shell 执行编译命令时，Shell 会看到单引号 `'...'`。
    *   **单引号的作用**：告诉 Shell “这里面的内容是纯文本，**不要**把 `$ORIGIN` 当作环境变量展开”。
    *   如果没有单引号，Shell 会试图查找名为 `ORIGIN` 的环境变量（通常为空），导致链接器收到错误的路径（如 `/../libs`）。
    *   **结果**：链接器 (`ld`) 收到了参数 `-rpath $ORIGIN/../libs`。

3.  **链接器层 (`ld` -> Binary)**
    *   链接器将 `$ORIGIN/../libs` 字符串写入可执行文件的 `RUNPATH` 字段。

### 1.3 验证方法

编译完成后，可以使用 `readelf` 命令检查：

```bash
readelf -d app | grep RUNPATH
```

**正确输出**应该包含 `$ORIGIN`：
```
0x000000000000001d (RUNPATH)            Library runpath: [$ORIGIN/../libs:$ORIGIN/../3rd/RsVisa/lib]
```

**错误输出**（Shell 展开导致的问题）：
```
0x000000000000001d (RUNPATH)            Library runpath: [/../libs:/../3rd/RsVisa/lib]
```

---

## 2. 对比机制：基于 `$$PWD` 的绝对路径

在之前的项目中，你可能使用过类似这样的写法：

```qmake
MY_LIB_PATH = $$clean_path($$PWD/../libs)
QMAKE_LFLAGS += -Wl,-rpath,$$MY_LIB_PATH
```

### 2.1 原理机制 (Build-time Resolution)

*   **`$$PWD` 是什么？**
    它是 QMake 的内置变量，代表当前 `.pro` 文件所在的**绝对路径**。

*   **工作流程：**
    1.  在运行 `qmake` 时，`$$PWD` 立即被替换为实际的路径（例如 `/home/pi/projects/vna/src/app`）。
    2.  生成的 Makefile 中包含的是硬编码的绝对路径：`-Wl,-rpath,/home/pi/projects/vna/libs`。
    3.  链接器将这个绝对路径写入程序。

### 2.2 优缺点对比

| 特性 | `$ORIGIN` (当前方案) | `$$PWD` (旧方案) |
| :--- | :--- | :--- |
| **路径类型** | **相对路径** (动态计算) | **绝对路径** (写死) |
| **可移植性** | **高**。只要保持文件夹结构（`bin` 和 `libs` 的相对位置）不变，程序拷贝到任何机器、任何目录下都能运行。 | **低**。程序必须放在编译时的完全相同的路径下才能运行。移动文件夹或拷贝到另一台机器通常会报错。 |
| **打包发布** | **适合**。这是发布 Linux 软件的标准做法。 | **不适合**。用户安装路径不可控。 |
| **配置复杂度** | **高**。需要处理复杂的转义字符。 | **低**。直接使用 QMake 变量即可。 |

---

## 3. 打包与发布指南

鉴于我们使用了 `$ORIGIN` 机制，在打包发布软件时，请遵循以下原则：

1.  **保持目录结构**：
    确保发布包解压后的目录结构与编译输出目录一致。
    ```
    vna-package/
    ├── bin/
    │   └── app  (可执行文件)
    ├── libs/    (依赖库)
    │   ├── libcontrols.so
    │   └── ...
    └── 3rd/
        └── RsVisa/ (可以没有这层,直接放在 3rd/)
            └── lib/
    ```
    只要 `app` 和 `libs` 的相对关系是 `../libs`，程序就能找到库。

2.  **不要依赖系统路径**：
    尽量不要要求用户把库复制到 `/usr/lib` 或 `/usr/local/lib`。使用 `$ORIGIN` 可以让你的程序“自带环境”，互不干扰（Green/Portable App）。

3.  **调试建议**：
    如果遇到“找不到库”的错误：
    *   使用 `ldd ./app` 查看依赖解析情况。
    *   使用 `readelf -d ./app` 检查 `RUNPATH` 是否正确写入了 `$ORIGIN`。
    编写打包脚本（如 `pack_vna.sh`）时，请执行以下步骤：
1.  创建根目录 `root`。
2.  创建子目录 `root/bin`, `root/libs`, `root/3rd`。
3.  拷贝 `vna-gui-app` 到 `root/bin`。
4.  拷贝 `libvna_api.so` 到 `root/libs`。
5.  拷贝 `librsvisa.so` (及相关的 `.so.x.x` 软链) 到 `root/3rd`。
6.  (可选) 编写启动脚本 `run.sh` 在根目录，调用 `bin/vna-gui-app`。

## 6. Windows 部署注意

Windows 不需要 RPATH。只需确保：
*   `vna_api.dll` 与 `.exe` 在同一目录。
*   `RsVisa32.dll` (及 `visa32.dll` 等依赖) 与 `.exe` 在同一目录，或者已安装在系统 `System32` 目录下。
*   在打包 Release 版本时，直接将所有 DLL 扔进 `bin` 文件夹即可。
