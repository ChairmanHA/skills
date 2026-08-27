# SGStudio 程序关闭到进程退出的全流程（含易死锁点与解决方案）

更新时间：2026-01-15

本文总结 SGStudio（Qt 5 + 插件驱动）从“用户触发关闭”到“进程真正退出”的完整链路，并标注容易卡死/死锁的位置与当前已落地的修复策略。

---

## 1. 总览：谁负责什么

- **Qt 主线程（GUI 线程）**：窗口事件、主事件循环 `QApplication::exec()`、插件 shutdown 调度（在 `aboutToQuit` 槽里发生）。
- **ExtensionSystem::PluginManager**：统一 stop/kill 插件，保证按 loadQueue 顺序关闭。
- **CorePlugin / MainWindow**：承载 DeviceManager、BusinessManager 等核心对象的生命周期。
- **DeviceManager**：维护设备对象、设备状态轮询线程（`statusUpdateThread`）与定时器（`statusUpdateTimer`）。
- **业务（Business）对象（例如 StreamingBussiness）**：通常自带工作线程，并在析构/stopBusiness 时负责退出线程。
- **Scanner（例如 HTRADeviceScanner）**：扫描线程与定时器；设备断开时可能会被 DeviceManager 触发重新扫描。

---

## 2. 时间线：关闭到退出（按实际调用顺序）

### 2.1 用户触发关闭

典型来源：

- 标题栏关闭按钮
- File -> Exit（触发 `MainWindow::close()`）

此时 Qt 会开始走窗口关闭逻辑（`QCloseEvent`），最终导致应用进入退出阶段。

### 2.2 `QApplication::aboutToQuit` 被触发

在 `src/app/main.cpp` 中有关键连接：

```cpp
QObject::connect(&app, &QApplication::aboutToQuit,
                 &pluginManager, &ExtensionSystem::PluginManager::shutdown);
```

含义：**Qt 一旦决定退出事件循环，就同步调用 PluginManager::shutdown()**。

### 2.3 PluginManager 进入 shutdown（Stop 阶段）

在 `PluginManagerPrivate::shutdown()`：

1) 打印 `Start Shutdown`
2) 依次对 `loadQueue` 中每个插件调用 `spec->d->stop()`
   - `PluginSpecPrivate::stop()` 会调用插件的 `IPlugin::aboutToShutdown()`
   - 插件应该在这里停止自身后台工作（扫描、worker、业务等）

如果某插件返回 `AsynchronousShutdown`，PluginManager 会创建 `QEventLoop` 等待该插件发出 `asynchronousShutdownFinished`。

### 2.4 PluginManager 进入 shutdown（Delete 阶段）

在 Stop 全部完成（并等待异步关闭完成）后：

- 依次对 `loadQueue` 调用 `spec->d->kill()`
- `kill()` 内部 `delete plugin`，触发插件析构链

这一步是“真正释放资源”的关键；如果某个析构阻塞（例如等待线程 join 卡死），**会导致 Delete 阶段日志不再输出**，进程也不会退出。

### 2.5 CorePlugin 被 delete 后的析构链

CorePlugin 析构会删除 MainWindow，MainWindow（及其 QObject 子对象）会依次析构：

- DeviceManager（停止状态更新线程/定时器）
- BusinessManager（销毁所有 business 对象，触发 business 析构与线程退出）
- 其他 UI/对话框/控件

### 2.6 `app.exec()` 返回，main() 返回，进程结束

当 `aboutToQuit` 的槽（shutdown）执行完成，Qt 完成退出，`QApplication::exec()` 返回，`main()` 返回，进程真正退出。

---

## 3. 最容易卡住/死锁的点（症状 -> 根因 -> 方案）

### 3.1 `deleteLater()` 在退出期间可能永远不执行

- **症状**：Stop/甚至 Delete 日志都出现，但进程仍驻留；或业务析构不发生，线程不退出。
- **根因**：`deleteLater()` 依赖事件循环后续迭代处理 `DeferredDelete`。退出阶段事件循环不再正常“跑起来”，导致对象不析构。
- **解决方案（已落地）**：在 `BusinessManager::~BusinessManager()` 对业务对象使用同步 `delete`，确保析构立即发生、线程可 join。

### 3.2 `condition_variable::wait()` 的谓词不包含退出条件

- **症状**：Delete 阶段卡住，常见于业务析构里的 `thread->wait()` 永远等不到。
- **根因**：worker 线程在 `wait()` 上睡眠，析构时即便设置了 `exitFlag` 并 `notify`，若 wait 谓词只检查 `enabled`，线程会被唤醒后立刻再次睡回去。
- **解决方案（已落地）**：StreamingBussiness 的 wait 谓词改为：`enabled || exitFlag`，保证析构唤醒后可退出。

### 3.3 `Qt::BlockingQueuedConnection` 的两类死锁

#### A) 调用方与接收方在同一线程（“自锁”）

- **症状**：运行中设备断开时出现：
  `QMetaMethod::invoke: Dead lock detected in BlockingQueuedConnection: Receiver is QTimer(...)`
- **根因**：设备断开可能发生在 DeviceManager 的 `statusUpdateThread` 内（timeout 回调 DirectConnection）。如果在该线程里又用 `BlockingQueuedConnection` 去 invoke 同线程对象的 slot，就会“等待自己处理自己”。
- **解决方案（已落地）**：`stopStatusUpdates()` 内判断 `QThread::currentThread() == statusUpdateThread`，同线程则直接 `timer->stop()`，不走 invoke。

#### B) 接收方线程未运行，但仍使用 BlockingQueuedConnection

- **症状**：无设备时关闭程序，只打印 Stop 不打印 Delete，主线程卡死。
- **根因**：线程未 running 没有事件循环，BlockingQueuedConnection 永远等不到执行。
- **解决方案（已落地）**：只有在 `statusUpdateThread->isRunning()` 时才使用 blocking stop。

---

## 4. 当前落地的关键修复点（面向维护）

- **BusinessManager 析构**：业务对象必须同步销毁（避免 `deleteLater()` 失效）。
- **StreamingBussiness::workerLoop()**：wait 谓词必须同时允许 `exitFlag` 唤醒退出。
- **DeviceManager::stopStatusUpdates()**：
  - 线程未运行时不能做 BlockingQueuedConnection
  - 同线程调用不能做 BlockingQueuedConnection
  - 仅停止 timer 不足以保证退出，必要时应退出线程事件循环

---
