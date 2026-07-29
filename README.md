# Axinyp.Wpf.Hosting

基于 **Microsoft Generic Host + Microsoft DI + CommunityToolkit.Mvvm** 的通用 WPF 应用基础设施。

它的定位不是“另一个大而全的 UI 框架”，而是提供一套足够稳定、足够透明、足够易于调试的宿主底座，让业务项目可以在不引入额外 IoC 容器和隐式约定的前提下，获得模块化、导航、对话框、主题、本地化与启动期插件能力。

## 设计目标

- 提供模块化、生命周期、启动期插件、设置、日志、配置能力；
- 提供 Window、Region、Navigation、Dialog、Theme、Localization 等 WPF 基础设施；
- 保持 API、关键实现与对外文档拥有完整中文注释；
- 先以独立框架演进，再由业务项目以“消费者”身份回接；
- 优先保证可诊断性、可维护性和类型边界清晰，而不是追求大量隐式魔法。

## 解决方案结构

```text
src/
├── Axinyp.Wpf.Hosting.Abstractions/   稳定公共契约层
├── Axinyp.Wpf.Hosting/                Generic Host / 模块 / 生命周期 / 插件 / 设置 / 本地化实现
└── Axinyp.Wpf.Hosting.Wpf/            Window / Region / Navigation / Dialog / Theme / WPF 本地化实现

samples/
└── BasicHost/                         最小可运行样例

tests/
├── Axinyp.Wpf.Hosting.Tests/          核心宿主测试
└── Axinyp.Wpf.Hosting.Wpf.Tests/      WPF 基础设施测试

consumer-smoke/                        本地 NuGet 包消费验证项目（临时验证用）
```

## 分层说明

### 1. Axinyp.Wpf.Hosting.Abstractions

这是整个框架的“稳定类型身份边界”。

适合放在这里的内容：

- 模块契约：`IModule`、`IModuleCatalog`、`ModuleDescriptor` 等；
- 生命周期契约：`IApplicationLifetime`、`ApplicationState`；
- 启动任务契约：`IStartupTask`；
- 插件发现结果契约：`IPluginCatalog`、`PluginManifest`、`PluginLoadFailure`；
- 设置与本地化抽象：`ISettingsStore`、`ILocalizationService`；
- WPF 公共契约：`IWindowManager`、`IRegionManager`、`INavigationService`、`IDialogService`、`IThemeService`、`IWpfUiContributionProvider` 等。

不放在这里的内容：

- Generic Host 运行时实现；
- JSON 设置存储实现；
- WPF Region / Navigation / Dialog / Theme 的默认实现；
- 各种“只对当前实现有意义”的内部辅助类型。

### 2. Axinyp.Wpf.Hosting

负责非 WPF 宿主能力的默认实现，包括：

- 模块收集、依赖排序与生命周期执行；
- 启动期插件扫描、依赖校验与 ALC 隔离加载；
- JSON 设置存储；
- 基于 `ResourceManager` 的本地化服务。

### 3. Axinyp.Wpf.Hosting.Wpf

负责 WPF 表示层默认实现，包括：

- 显式 ViewModel → View 映射；
- Window 管理；
- Region 注册与区域导航；
- ViewModel-first 对话框；
- 主题切换；
- XAML 动态本地化绑定；
- 插件 UI Contribution 聚合目录。

## 核心原则

- 基于原生 `HostApplicationBuilder` / `IHost` / `IConfiguration` / `ILogger<T>` / `IOptions<T>`；
- 使用显式 ViewModel → View 注册，不默认依赖命名约定扫描；
- 模块在构建前收集、排序、校验，并在启动时统一初始化、启动、停止；
- 启动期插件只承诺“构建前发现与加载”，不误导为运行时热插拔；
- 公共类型身份统一收敛到 `Axinyp.Wpf.Hosting.Abstractions`；
- 线程、生命周期、资源释放等关键逻辑必须写清楚中文注释。

## 最小使用方式

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.AddModularHosting();
builder.Services.AddJsonSettings();
builder.Services.AddResxLocalization(options =>
{
    options.SupportedCultures.Add(CultureInfo.GetCultureInfo("zh-CN"));
    options.ResourceManagers.Add(new ResourceManager("YourApp.Resources.Strings", typeof(App).Assembly));
});
builder.Services.AddWpfPresentation();
builder.Services.AddWpfLocalizationBindings();
builder.Services.AddWpfThemes(options =>
{
    options.Themes.Add(new ThemeDescriptor("light", "Light", new Uri("pack://application:,,,/..."), true));
});

builder.Services.AddSingleton<MainWindowViewModel>();
builder.Services.AddView<MainWindowViewModel, MainWindow>();

using var host = builder.BuildModularHost();
await host.StartModularHostAsync();
```

## 本地包使用说明

当前仓库已经完成本地 `dotnet pack` 与消费者恢复验证，但**不包含任何线上发布动作**。

也就是说，当前支持的是：

- 在本地生成 `.nupkg`；
- 将本地目录作为 NuGet 源进行恢复；
- 由消费者项目通过 `PackageReference` 接入；
- 验证分层、依赖与最小启动链路是否成立。

当前不做的事情：

- 不推送到 `nuget.org`；
- 不推送到 GitHub Packages；
- 不把 CI 配置为自动发布；
- 不承诺任何公网包版本管理策略。

本地验证产物目录：

```text
artifacts/packages/
```

本地消费者验证项目：

```text
consumer-smoke/
```

该项目的意义是模拟“外部消费者只拿到 nupkg，不依赖源码”的场景，用来提前发现包依赖、TFM、README 打包、公共抽象引用等问题。

## 当前短板与长期优化点

当前基础设施已经具备进入真实业务回接阶段的条件，但还不能把它视为“已经彻底完善”。

比较明确的短板包括：

1. **Shell 层偏薄**
   - 还没有形成标准化的菜单、工具栏、状态栏、设置页与工作区组织模型；
   - 更像底座，不是现成完整壳层。

2. **诊断能力还可增强**
   - 模块装配、插件发现、导航失败、主题切换等还可以继续补结构化日志与诊断信息。

3. **线程调度抽象仍可继续收敛**
   - 当前已经集中到 Dispatcher，但还没有独立 `IUiDispatcher` 这类统一抽象。

4. **测试还属于“主链路可用”，未到“边角压透”**
   - 还需要继续补复杂插件依赖、UI 线程边界、Dialog 竞态、Region 快速切换等测试。

5. **真实业务回接后一定还会暴露第二轮问题**
   - 包括 API 粒度、默认实现刚性、Shell 边界和插件落点等。

这些内容已经进一步写入：

- `docs/architecture.md`
- `src/Axinyp.Wpf.Hosting.Wpf/DESIGN.md`

后续应当采用“业务回接 -> 暴露问题 -> 文档记录 -> 回归修正”的节奏，而不是继续脱离业务做空转式抽象。

## 当前阶段

当前阶段已经完成：

1. 从原始业务项目中的 Hosting 原型中性化；
2. 建立独立解决方案、样例、测试、文档与 CI 骨架；
3. 将公共契约真正下沉到 `Axinyp.Wpf.Hosting.Abstractions`，消除实现层重复定义；
4. 验证独立构建、测试、打包通过；
5. 验证本地 NuGet 包消费者可恢复、可编译；
6. 把当前短板、长期优化点与迭代验证策略写入文档。

接下来更适合推进的方向是：

- 开始最小真实业务回接；
- 每轮回接后补回归测试和文档；
- 根据业务证据继续修正抽象与默认实现。
