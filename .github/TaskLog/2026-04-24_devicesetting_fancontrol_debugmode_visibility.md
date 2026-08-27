# Device Settings Fan Control DebugMode Visibility

## Goal

- 程序启动时，根据 configuration/Settings.ini 中是否存在 Debug/DebugMode 键，决定 Device Settings 页面里的 Fan Control group 是否显示。
- 这里判断的是“选项是否存在”，不是 DebugMode 的布尔值。

## Design

- 不扩展全局 Utils::Settings 封装，因为它当前按 value(default) 读取，无法区分“键不存在”和“键存在但值为 false”。
- 在 DeviceSettingPanel 构造期直接用 QSettings 读取 configuration/Settings.ini，并通过 contains("Debug/DebugMode") 做一次启动期判断。
- 将 Fan Control group 提升为成员，布局创建后立即设置 visible；若该组隐藏，则不初始化 fan mode 的设备连接与刷新逻辑。

## Expected Result

- 带有 Debug/DebugMode 键的配置下，Fan Control group 正常显示并保持现有行为。
- 不带该键的配置下，Fan Control group 在程序启动后默认不显示。