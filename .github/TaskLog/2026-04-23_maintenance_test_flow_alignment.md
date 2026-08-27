# 2026-04-23 Maintenance 测试流对齐

## 用户测试期望

- 清理 Updater 缓存后，启动主程序并让其重新静默下载服务器新包。
- 插入设备后弹出 `Update Now / Later / Ignore`。
- 点击 `Update Now` 后：
  - 立即停止设备并关闭主程序
  - 从当前安装目录 `bin/maintenance.exe` 启动 maintenance
  - maintenance 弹出 UAC 提权对话框
  - 若用户取消提权，不再弹额外错误窗口，也不继续执行 updater
  - 若用户同意提权，maintenance 以 `-y` 启动包内 updater
  - updater 完成后 maintenance 关闭并拉起主程序

## 当前代码与期望差异

1. 自动三选提示路径点击 `Update Now` 时没有默认附加 `-y`。
2. `UpdateCoordinator` 当前优先使用包内 `bin/maintenance.exe`，而测试期望要求固定使用当前安装目录 `bin/maintenance.exe`。
3. maintenance 自提权时，若用户取消 UAC，会显示失败状态窗口；测试期望要求静默结束，不再继续任何动作。

## 本次修正范围

- 仅修正上述三点，不扩展 install.bat 或整包覆盖逻辑。
- 静态分析会同时基于：
  - 当前 `UpdateCoordinator` / `PacketSpec` / `maintenance` 代码
  - 重新从服务器下载并解压的真实 `SGStudio.zip`

## 当前结果

- 上述三项差异已经完成收口。
- 用户已手工跑通完整链路：
  - 静默下载
  - 设备连接后提示更新
  - maintenance 提权
  - 固件更新成功
  - maintenance 重启主程序
- 用户已进一步验证主程序侧收口：
  - 第一次重启动后会弹出 release notes + 插拔设备提示
  - 第二次正常启动不再重复弹出

## 下一步入口

- 当前链路已经不再卡在 maintenance 接管本身。
- update session 与首次启动提示这一步已经完成。
- 下一阶段应直接转到 install update 收口：
  - maintenance 调用包内 `install.bat`
  - 更新 dll 和主程序
  - 覆盖成功后再拉起主程序
  - 覆盖失败时保留日志并阻止误报成功