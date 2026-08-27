# Property 绑定重复软键盘陷阱

本文记录一个 UI / property 信号层面的常见坑：同一个 `PropertySystem::IProperty` 被多个控件或多个 panel 绑定后，一次用户编辑可能触发多个订阅者同时打开 `TouchNumKeyboard`。

## 现象

用户点击一个参数按钮后，表面上只看到一个软键盘；但提交单位或关闭键盘后，键盘看起来没有消失，或者又露出另一个键盘。

典型表现：

- 参数值已经正常提交，主界面显示也更新了。
- 当前键盘关闭后仍有另一个键盘停留在界面上。
- 日志或断点能看到同一次 `property->beginEditing(triggerObj)` 被多个 panel 的槽响应。

## 根因

`PropertyBindingManager::bindButtonToProperty(button, property)` 通常会让按钮点击触发：

```cpp
emit property->beginEditing(button);
```

如果多个 panel 都连接了同一个 property 的 `beginEditing` 信号，并且每个槽都无条件打开键盘，那么一次点击就会打开多个键盘。

这个问题最容易出现在下面几类场景：

1. 一个全局 property 同时被两个业务 panel 复用。
2. 旧业务从 entry list 注销了，但 QObject / panel 仍然存活。
3. 同名 provider 做运行期切换，inactive provider 的 panel 仍连接着共享 property。
4. 一个 property 被同一页面的多个控件绑定，其中多个控件都各自承担“打开编辑器”的职责。

注意：`BusinessManager::unregisterBusiness(...)` 只是不再把 business 显示在 entry host / modulation list 中，不等于销毁 business、panel 或断开 QObject signal。

## 判断方法

遇到重复键盘时，先检查：

1. 触发键盘的 property 名称是什么，例如 `Am_Rate`、`Fm_Deviation`、`Pulse_Width`。
2. 全仓搜索这个 property 的 `beginEditing` 连接点。
3. 看是否存在多个 panel 对同一个 property 执行 `prepareNumericKeyBoard(...)`。
4. 确认这些 panel 是否都可能在当前运行期存活，即使其中一些已经从业务列表隐藏。

如果一个点击只 emit 一次 `beginEditing`，但打开多个键盘，问题通常就在多个订阅者。

## 修复原则

优先选择唯一 owner：

- 同一个 property 的“打开编辑器”职责最好只属于当前可见、当前 active 的控件或 panel。
- 如果旧 provider / 旧 panel 不再是产品路径，应删除或销毁旧对象，而不是只隐藏 entry。
- 如果多个控件只是展示同一个值，尽量让只有一个控件负责 `beginEditing`，其他控件只做显示同步。

如果确实需要多个 panel 共享 property，并且这些 panel 可能同时存活，就必须在 `beginEditing` 槽里判断触发来源：

```cpp
connect(property, &PropertySystem::IProperty::beginEditing, this, [this, property](QObject *triggerObj) {
    if (!PropertyBindingHelper::isEditTriggerFromWidget(this, triggerObj)) {
        return;
    }

    auto keyboard = PropertyBindingHelper::prepareNumericKeyBoard(property, triggerObj);
    keyboard->open();
});
```

`PropertyBindingHelper::isEditTriggerFromWidget(owner, triggerObj)` 的语义是：只有当 `triggerObj` 是当前 panel 自己或它的子控件时，当前 panel 才响应这次编辑。

## 什么时候不该加 guard

不要把 guard 当成默认样板。

如果已经确认只有一个 panel / 一个控件负责打开键盘，额外 guard 只会增加维护噪音。比如当前 HTRA 基础调制已经是 `AM`、`FM`、`Pulse`、`Digital Ramp`、`AWGN` 的唯一 provider owner，不再存在隐藏同名 Analog panel，这类 panel 就不需要为了旧双 provider 场景保留 guard。

## 维护规则

- 新增 property 绑定时，先确认它是否是全局共享 property。
- 新增 panel 的 `beginEditing` 槽时，搜索同名 property 是否已有其它键盘打开逻辑。
- 隐藏 business 不等于断开 property signal；如果隐藏对象仍会产生副作用，应销毁对象、disconnect，或做 active owner 判断。
- 如果某个 guard 是为了特定双 provider / hidden panel 场景存在，应在该场景删除后同步删除 guard，避免未来误判当前架构仍有多 owner。

## 相关文件

- `src/libs/business/propertybindingmanager.*`
- `src/libs/business/utils.*`
- `src/libs/controls/touchnumkeyboard.*`
- `.github/KnowledgeBase/analog_htra_provider_lifecycle_and_duplicate_panel_guard.md`
