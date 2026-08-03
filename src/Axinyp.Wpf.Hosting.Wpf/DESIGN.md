# Axinyp.Wpf.Hosting.Wpf 设计说明

## 定位

`Axinyp.Wpf.Hosting.Wpf` 是 `Axinyp.Wpf.Hosting` 的 WPF 适配层，目标是为基于 **Microsoft DI + Generic Host + CommunityToolkit.Mvvm** 的桌面应用提供一套轻量、可测试、显式注册的 UI 基础设施。

它已经覆盖以下能力：

- Window 管理；
- Region / Navigation；
- Dialog 协议；
- Theme 服务；
- 动态 Localization 绑定；
- 启动期插件的 UI Contribution 聚合。

## 设计原则

### 1. 显式优于约定

不默认使用命名约定扫描或程序集反射发现 View。所有 ViewModel → View 的映射必须通过 `AddView<TViewModel, TView>()` 显式注册，避免大型应用中出现隐式绑定和调试困难。

### 2. Scope 生命周期清晰

窗口与区域内容的创建都必须有明确的 DI scope 边界。关闭窗口、替换区域内容、导航离开后，旧 scope 必须可释放，避免 View / ViewModel / 服务悬挂。

### 3. 不缓存视觉树

导航后退只保留轻量 journal，不直接缓存已有视觉树。这样可以降低内存占用，并避免 UI 对象跨线程、跨生命周期复用。

### 4. UI 线程操作集中化

所有 WPF 对象创建、窗口显示、Owner 绑定、资源字典变更和关闭相关逻辑，都通过 WPF 专属的 `IUiDispatcher` 调度。该契约属于 `Axinyp.Wpf.Hosting.Abstractions.Wpf`，不下沉到无 UI 依赖的 `Axinyp.Wpf.Hosting`。

默认实现会在目标 Dispatcher 线程内联执行；跨线程时以可等待的任务切换到目标 Dispatcher。Dispatcher 已开始或完成关闭时，新的 UI 操作会被拒绝，调用方必须观察返回任务的异常，不能遗留未观察的调度任务。

Region 的清理分为两个阶段：Dispatcher 可用时先从视觉树解除 View、清空 `DataContext`，再释放 scope；Dispatcher 关闭后不再访问 WPF 视觉属性，但仍无条件释放 scope。关闭期释放可能在调用方或任务线程执行，因此 scope 内服务必须将 UI 解绑置于 Dispatcher 仍可用的阶段，且不得在关闭期 `Dispose` 中访问 WPF 对象。`CanGoBack` 仅读取线程安全的后退栈快照，不需要从后台线程同步调用 Dispatcher。

### 5. 插件只承诺启动期接入

当前版本的插件 UI 贡献仅承诺“宿主构建前发现并在启动后聚合”，不承诺运行时热插拔 UI 组件。

## 非目标

以下能力不属于本层直接职责：

- 业务级 Shell 布局与菜单视觉样式；
- 页面权限控制与导航策略 DSL；
- 运行时插件商店、插件启停界面；
- 跨平台 UI 抽象。

## 关键语义

### WindowManager

- 泛型显示 API 会创建独立 scope；
- 非模态窗口关闭时释放 scope；
- 模态窗口要求显式 Owner；
- 所有窗口操作必须运行在 WPF Dispatcher 上。

### NavigationService

- 负责区域内容替换；
- 进入新页面前可询问旧页面是否允许离开；
- 历史记录只保存导航信息，不缓存 View；
- 区域切换时必须清理旧 `DataContext` 并释放相关资源；
- `NavigationResult` 为 consumer 提供稳定失败分类、诊断代码和异常类型名；原始异常只随 `1100–1104` 日志事件写入 `ILogger`，不会包含导航参数。

### UI Contribution Catalog

- 每个 Provider 在独立 DI scope 中激活和执行，构建后的 catalog 只保留不可变元数据；
- `WpfUiContributionFailure` 只公开安全消息、失败分类和安全上下文；Provider 原始异常只记录在 `1140–1145` 日志事件中。

### DialogService

- 通过 `IDialogAware` 协议与 ViewModel 通信；
- 支持参数传递与请求关闭；
- 用户点击关闭按钮时仍然走 `CanCloseDialog()` 语义。

### Theme / Localization

- Theme 仅加载受信任的应用内 `pack://application:,,,/` 资源；
- Localization 通过绑定源广播文化切换；
- 两者都可与设置存储结合做持久化。

## 当前短板

### 1. 还没有形成标准 Shell 层

当前已经提供了足够完整的 WPF 底座，但还没有标准化的：

- 菜单 / 工具栏 / 状态栏宿主模型；
- 设置页容器规范；
- 工作区、浮窗、停靠区、导航页之间的组合约定；
- UI Contribution 的最终推荐渲染方案。

这意味着当前更偏“基础设施”，还不是“现成应用骨架”。

### 2. WPF 边界测试还可继续增加

当前已覆盖 Dispatcher 内联与跨线程调度、关闭期拒绝新的 Window / Dialog / Navigation / Theme 操作、Region 关闭期 scope 释放，以及后台读取后退栈快照。后续可继续增强：

- Dialog 竞态关闭与异常回收；
- Region 快速切换与回退；
- 本地化和主题资源缺失时的降级行为。

### 3. 插件 UI Contribution 仍停留在“声明聚合”，没有 Shell 侧标准落点

当前这层已经能可靠收集元数据，但业务方仍需要自己决定：

- 导航页如何渲染；
- 命令如何落到菜单或工具栏；
- 设置页如何组织与分组；
- 不同贡献之间冲突时如何呈现。

## 后续演进建议

后续建议优先补齐：

1. 文档中的线程模型与资源释放时序图；
2. 更完整的窗口 / 对话框 / 主题切换测试；
3. 插件 compatibility 与 UI Contribution 示例；
4. 一个可选 Shell 参考实现或更完整 Shell 样例；
5. 基于真实业务回接结果继续修正当前 API 粒度与默认行为。

## 与真实业务回接的关系

当前 WPF 基础设施已经足以承接第一轮真实业务回接，
但建议采用“最小迁移 + 逐轮验证”的策略：

1. 先接主窗口启动；
2. 再接一个真实 Region 导航链路；
3. 再接一个对话框链路；
4. 再接主题/本地化/设置链路；
5. 最后再扩大到 UI Contribution 和插件场景。

每一轮回接中暴露的缺口，都应优先沉淀到文档，再决定是否下沉为框架能力。
