这两个接口从**命名 + 你的示例 + 射频设备 GPIO 控制习惯**来看，应该不是“传入 16 路完整电平状态”，而是典型的 **bit mask 增量控制**：

```c
device_gpio_setbits(&device, bits);    // bits 中为 1 的 GPIO 置高；为 0 的 GPIO 不变
device_gpio_resetbits(&device, bits);  // bits 中为 1 的 GPIO 置低；为 0 的 GPIO 不变
```

也就是说，`bits` 里的 `1` 表示“我要操作这个 GPIO”，不是表示“最终电平为 1”。
头文件里两个接口的注释都写成了“共16个，1置高，0置低”，但这个注释和接口名 `setbits/resetbits` 以及你给的示例“其他 gpio 口不变”是冲突的；更合理的判断是：**头文件注释写得不严谨，甚至是 copy/paste 错误**。头文件确实只暴露了这两个 GPIO 接口，并且参数都是 `uint16_t bits`，覆盖 16 个 GPIO 位。

---

## 这两个接口的实际区别

假设当前 GPIO 状态是：

```text
gpio15 ... gpio3 gpio2 gpio1 gpio0
             0     1     0     0
```

调用：

```c
uint16_t bits = 0b0000000000001001; // idx=0 和 idx=3
device_gpio_setbits(&device, bits);
```

含义应是：

```text
idx=0 -> 置高
idx=3 -> 置高
其他 GPIO -> 不变
```

结果：

```text
gpio3 = 1
gpio0 = 1
其他保持原状态
```

而调用：

```c
uint16_t bits = 0b0000000000001001;
device_gpio_resetbits(&device, bits);
```

含义应是：

```text
idx=0 -> 置低
idx=3 -> 置低
其他 GPIO -> 不变
```

结果：

```text
gpio3 = 0
gpio0 = 0
其他保持原状态
```

所以二者区别是：

| 接口                      | `bits` 中为 1 的 GPIO | `bits` 中为 0 的 GPIO |
| ----------------------- | -----------------: | -----------------: |
| `device_gpio_setbits`   |                 置高 |                 不变 |
| `device_gpio_resetbits` |                 置低 |                 不变 |

不是：

```c
setbits(bits):    1置高，0置低
resetbits(bits):  1置高，0置低
```

如果真是这样，那两个接口功能完全一样，没有存在两个函数的必要。

---

## 前端只控制一个 GPIO 的 on/off，应该怎么调用？

假设前端选择的是 `idx` 这个 GPIO，范围是 `0 ~ 15`。

如果这个 GPIO 是**高电平有效**，也就是：

```text
ON  = 高电平
OFF = 低电平
```

那么后端调用应该是：

```c
int gpio_set_one(void** device, uint8_t idx, bool on)
{
    if (idx >= 16) {
        return -1; // 参数错误，可换成你们自己的错误码
    }

    uint16_t mask = (uint16_t)(1u << idx);

    if (on) {
        return device_gpio_setbits(device, mask);    // ON -> 置高
    } else {
        return device_gpio_resetbits(device, mask);  // OFF -> 置低
    }
}
```

使用示例：

```c
// 打开 GPIO0
status = gpio_set_one(&device, 0, true);

// 关闭 GPIO0
status = gpio_set_one(&device, 0, false);

// 打开 GPIO3
status = gpio_set_one(&device, 3, true);

// 关闭 GPIO3
status = gpio_set_one(&device, 3, false);
```

---

## 注意：RF 前端的 on/off 不一定等于高/低

射频行业里 GPIO 常用于控制：

```text
RF switch
PA enable
LNA enable
attenuator control
external trigger
blanking / mute
T/R switch
front-end power enable
```

这些信号不一定都是高电平有效。有些是：

```text
高电平 = ON
低电平 = OFF
```

也有些是反过来的：

```text
低电平 = ON
高电平 = OFF
```

所以前端 UI 的 `on/off` 最好不要直接绑定成“高/低”，而是后端为每个 GPIO 配一个极性：

```c
typedef enum {
    GPIO_ACTIVE_HIGH = 0,
    GPIO_ACTIVE_LOW  = 1
} gpio_polarity_t;
```

然后这样封装：

```c
int gpio_set_logical(void** device, uint8_t idx, bool on, gpio_polarity_t polarity)
{
    if (idx >= 16) {
        return -1;
    }

    uint16_t mask = (uint16_t)(1u << idx);

    bool need_high;

    if (polarity == GPIO_ACTIVE_HIGH) {
        need_high = on;
    } else {
        need_high = !on;
    }

    if (need_high) {
        return device_gpio_setbits(device, mask);
    } else {
        return device_gpio_resetbits(device, mask);
    }
}
```

这样前端永远只传：

```text
GPIO idx
ON / OFF
```

后端根据硬件定义决定到底拉高还是拉低。

---

## 你不应该这样调用

不要为了关闭某一个 GPIO 而传：

```c
device_gpio_setbits(&device, 0);
```

这大概率只是“不操作任何 GPIO”，不是“全部置低”。

也不要为了设置某一个 GPIO 的完整状态而传类似：

```c
// 想让 gpio0=1，其他=0
device_gpio_setbits(&device, 0x0001);
```

如果按照掩码语义，这只会把 `gpio0` 置高，其他 GPIO 不会被置低。

---

## 建议给 API 文档提一个修正

当前头文件里这两段建议改成类似这样：

```c
/**
 * @brief 置位GPIO。
 *
 * @param[in] device 设备句柄
 * @param[in] bits   GPIO 位掩码，共16个；bit=1 表示对应 GPIO 置高，bit=0 表示不变
 *
 * @return 0 成功，非 0 失败
 */
H2_API int device_gpio_setbits(void** device, uint16_t bits);

/**
 * @brief 复位GPIO。
 *
 * @param[in] device 设备句柄
 * @param[in] bits   GPIO 位掩码，共16个；bit=1 表示对应 GPIO 置低，bit=0 表示不变
 *
 * @return 0 成功，非 0 失败
 */
H2_API int device_gpio_resetbits(void** device, uint16_t bits);
```

---

结论：前端如果是单个 GPIO 的 `on/off` 交互，推荐后端统一封装成：

```c
mask = 1u << idx;

ON  -> device_gpio_setbits(..., mask);
OFF -> device_gpio_resetbits(..., mask);
```

但要额外确认这个 GPIO 控制的射频器件是**高有效**还是**低有效**。如果是低有效，就把 ON/OFF 的调用反过来。

---

## SGStudio 当前接入方式（2026-09-15）

当前 H2 API 只提供 `device_gpio_setbits()` 和 `device_gpio_resetbits()`，没有 GPIO 数量查询或电平回读接口。因此 `FancyDevice` 保持以下边界：

- GPIO 数量继续由设备 `HardwareVersion` 的高字节推导：`0` 表示不支持，`0x60` 表示 8 路，其他非零值表示 4 路。
- 设备打开后，软件用有效 GPIO 掩码调用一次 `device_gpio_resetbits()`，把本次会话的初始状态统一为低电平。
- 后续 UI 状态来自本次会话内的成功写入缓存；只有 H2 API 调用成功后才更新缓存。
- 设备关闭、切换或断联时清空缓存，避免下一次会话显示旧状态。
- `queryGpioState()` 返回正数 GPIO 数量时，Core 才动态注册 `System > GPIO` 菜单入口。

这里的缓存不是硬件回读。如果未来 H2 API 增加 GPIO 状态或能力查询，应优先改为消费真实 API 结果，并移除对应的推导或缓存职责。
