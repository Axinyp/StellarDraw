# WhiteBoard.Framework.Hosting.Wpf 设计文档

## 目标

本项目是 `WhiteBoard.Framework.Hosting` 的 WPF 适配层。核心框架保持 `net10.0` 与 UI 无关；本项目将 WPF 的窗口、Dispatcher 与资源生命周期接入原生 `Microsoft.Extensions.DependencyInjection`。

当前版本解决两个基础问题：

1. ViewModel 如何以确定方式定位到 View；
2. Window 如何在 UI 线程上创建、绑定、显示和释放其 DI scope。

## 非目标

- 不扫描程序集或按命名约定猜测 View；
- 不自动选择 `Application.Current.MainWindow` 或活动窗口作为 Owner；
- 不实现 Navigation、Region、业务 Dialog、Theme、Localization；
- 不定义 ViewModel 基类，不耦合 CommunityToolkit.Mvvm 的具体类型；
- 不提供运行时添加服务或动态改变已构建 DI 容器的能力。

## 架构

```text
业务模块 / WhiteBoard.Client
    │ AddSingleton<TViewModel>()
    │ AddView<TViewModel, TView>()
    ▼
ServiceCollectionExtensions
    │ ViewRegistration (显式、唯一)
    ▼
IViewLocator ──────────────► 精确映射 ViewModel Type → View Type
    │
    ▼
IWindowManager
    │ UI Dispatcher
    ├─ 创建窗口 IServiceScope（泛型 API）
    ├─ 解析 ViewModel 与 transient View
    ├─ View.DataContext = ViewModel
    ├─ 设置显式 Owner
    └─ Show / ShowDialog，关闭时释放 scope
```

## 核心组件

| 组件 | 职责 |
|---|---|
| `IViewLocator` | 只负责查询显式注册的 View 类型。 |
| `IWindowManager` | 统一窗口的 Dispatcher 调度、DI 创建、DataContext、Owner 和 scope 生命周期。 |
| `ViewRegistration` | 内部不可变映射记录。 |
| `ViewLocator` | 构建精确类型字典；重复或缺失映射给出明确异常。 |
| `WindowManager` | 每个泛型窗口一 scope；关闭或失败时确保释放。 |
| `RegionManager` / `RegionHost` | 显式维护 ContentControl 区域；替换视图时清空旧 DataContext、释放 scope，且不缓存旧视觉树。 |
| `NavigationService` | 以 ViewModel 类型导航，维护轻量后退记录，调用参数/离开确认生命周期。 |
| `DialogService` | 使用独立 scope 和 Window 显示对话框，支持参数、结果、请求关闭与关闭拦截。 |

## 关键设计决策

| 日期 | 决策 | 理由 | 影响 |
|---|---|---|---|
| 2026-07-13 | 显式注册而非约定扫描 | 插件与模块边界下，扫描结果不可预测且难诊断。 | 调用方必须写 `AddView<TVm, TView>()`。 |
| 2026-07-13 | `View` transient、ViewModel 生命周期独立 | 已关闭的 WPF Window 不可复用；业务 VM 有 singleton/transient/scoped 等不同需要。 | `AddView` 不注册 ViewModel。 |
| 2026-07-13 | 泛型窗口 API 使用独立 scope | 支持窗口级 Scoped 服务，避免从根容器创建临时业务资源。 | 窗口关闭时自动释放 scope。 |
| 2026-07-13 | Owner 必须显式传入 | 自动推断 Owner 在多窗口和插件环境中不可靠。 | 模态 API 的 Owner 为必填。 |
| 2026-07-14 | Region 后退栈不缓存视觉树 | 缓存 View/VM/scope 会泄漏 WPF 绑定、事件与作用域资源。 | 返回时重新通过 DI 创建前一页，状态应通过参数或状态服务保存。 |
| 2026-07-14 | Navigation 先初始化目标页后切换 | 目标 ViewModel 解析或进入钩子失败时，当前区域必须保持可用。 | 仅在目标页成功准备后调用离开钩子并替换 Content。 |
| 2026-07-14 | 设置、文化核心留在 Hosting，主题与动态 XAML 绑定留在 WPF | 用户偏好和资源解析可复用，`ResourceDictionary`/`MarkupExtension` 是 WPF 特有实现。 | `ISettingsStore`/`ILocalizationService` 无 WPF 依赖，Theme/LocExtension 在本项目实现。 |
| 2026-07-14 | 插件只提供声明式 UI Contribution Provider | 插件直接操作 Shell 控件会破坏排序、异常隔离和 ALC 生命周期。 | 宿主从冻结 Catalog 渲染菜单、工具栏、导航和设置页。 |

## 生命周期与线程模型

- `IWindowManager` 是单例，只持有 `IViewLocator`、`IServiceScopeFactory` 与根服务提供程序，不保存已显示 Window 或 ViewModel。
- 泛型 `ShowAsync<T>` / `ShowDialogAsync<T>` 在 UI Dispatcher 上创建 scope，并从同一 scope 解析 VM 与 View。
- 非泛型 API 接收调用方已有 VM，使用根服务提供程序创建 View；调用方负责该 VM 的生命周期。
- Dispatcher 已关闭、Owner 跨 Dispatcher、Owner 未显示时立即抛出异常，不静默回退。
- 原生 `ShowDialog()` 保持 WPF 嵌套消息循环语义；`Task` 只用于跨线程调度和调用方等待。

## 已知限制

1. `WindowExtensions.IsClosed` 只能基于 WPF 当前可观测状态判断，故 Owner 规则同时要求 `IsVisible`。
2. Region 后退不会自动恢复旧 ViewModel 内存状态；需要恢复的状态应通过导航参数、`ISettingsStore` 或业务状态服务保存。
3. Dialog 的真实模态交互依赖 WPF 应用消息循环，不能在无 Application 循环的 xUnit STA 线程中可靠端到端驱动；应由 Client 实际运行验证。
4. 不能把 AssemblyLoadContext 当作安全隔离。插件 UI 仍是受信任代码，若需不受信插件必须采用进程隔离。

## 安全考量

- 入口参数（ViewModel、Owner、映射类型）均在系统边界验证；无效状态会报错而非创建半初始化窗口。
- 不记录用户数据、Token 或任何敏感配置。
- WPF 线程访问全部经应用 Dispatcher，避免后台线程直接操作 UI 导致不可预测状态。

## 后续演进

- **P5**：Theme / Localization 通过 `Application.Resources` 资源字典集成；
- **P7 / v2**：再评估运行期插件热加载/卸载；必须先解决已显示 View、ResourceDictionary、导航栈、命令和事件订阅的 ALC 引用释放。
