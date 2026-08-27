# 数字调制默认值与 FSK Deviation 联动

## 目标
- 修复只连接data会报-8，设备成功打开后，弹窗不消失。
- 隐藏PEP峰值功率
- 频率功率扫描和mod互斥，频扫时打开mod自动停止，反之亦然
根据外协反馈优化：
- 默认值职责从 DigitalModulator 回收到 packing.cpp，由 Digital_DeInit / Digital_Configuraion 作为唯一默认源头。
- 将数字调制默认值调整为：SymbolRate=1M、FilterLength=16、PN=15、ModulationType=QAM64。
- Oversample 默认固定优先为 4；仅在采样率约束下不合法时，退化到其它合法偶数值。
- 数字调制 UI 侧移除暂不开放的调制类型选项：DBPSK、DQPSK、Pi4DQPSK、D8PSK、QAM1024、APSK16。
- 对 FSK 调制增加频偏自动同步：
  - 切换到 FSK 时，Deviation 自动同步为 SymbolRate。
  - FSK 模式下修改 SymbolRate 时，Deviation 自动同步为 SymbolRate。
  - 用户显式修改 Deviation 时，仍按原有 逻辑生效。

## 设计

- 默认值与参数限制统一收口在 packing.cpp：
  - Digital_DeInit 负责给出业务默认值。
  - Digital_Configuraion 负责默认值和用户输入的合法化。
  - DigitalModulator 不再二次覆盖默认值，避免默认规则散落在 UI/业务适配层。
- Oversample 选择固定为 4：
  - 对常见数字调制与 RRC/RC/Gaussian 成形，4 sps 已能提供足够的波形成形分辨率。
  - 相比 8 sps，可将默认波形点数、下载负载、重算时间近似减半，更符合仪表业务的默认配置习惯。
  - 8 sps 保留给用户按需上调，而非作为默认值。
- 暂不开放的调制类型直接在 UI option 源头删除，而不是仅标记 disabled：
  - 可确保用户无法通过界面选中这些类型。
  - 底层枚举、配置和算法入口保持不动，便于未来业务恢复。
- FSK 频偏联动也放在 DigitalModulator setter 层：
  - setModulationType 切入 FSK 时触发自动同步。
  - setSymbolRate 在 FSK 模式下触发自动同步。
  - setFskDeviation 保持显式设置语义，不增加额外状态机。

## 验证

- 编译 Release，确保改动无编译错误。
- 检查数字调制属性初始化与参数改动路径未引入新的类型/信号错误。