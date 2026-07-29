# Axinyp.Wpf.Hosting.Wpf

WPF 宿主适配层：在不污染 `Axinyp.Wpf.Hosting`（`net10.0`、无 UI 依赖）的前提下，提供基于原生 DI 的显式 View/VM 映射与窗口、区域、导航、对话框、主题、本地化能力。

## 能力

- **显式 View 映射**：通过 `AddView<TViewModel, TView>()` 注册精确的一对一 ViewModel → View 映射；不使用命名约定、反射扫描或基类回退。
- **VM-first 窗口显示**：`IWindowManager` 从 DI 解析 ViewModel、定位并创建 View，统一设置 `DataContext`。
- **窗口级作用域**：泛型显示 API 为每个窗口创建独立 `IServiceScope`，窗口关闭或显示失败时释放。
- **UI 线程安全**：所有 WPF 对象创建、Owner 设置与 `Show` / `ShowDialog` 均调度到应用 `Dispatcher`。
- **原生模态支持**：保留 WPF `ShowDialog()` 语义，模态窗口必须显式提供 Owner。
- **区域导航**：`IRegionManager` 显式托管 `ContentControl`，`INavigationService` 提供 VM-first 前进/返回、参数传递和离开确认；后退栈只保存轻量导航记录，不缓存视觉树。
- **Dialog 协议**：`IDialogService`、`IDialogAware` 与 `IDialogResult` 支持窗口级参数、请求关闭和关闭拦截。
- **主题**：`IThemeService` 切换受信任的应用级 `ResourceDictionary`，并将主题 ID 作为用户偏好持久化；可切换资源必须使用 `DynamicResource`。
- **动态本地化**：核心 `ILocalizationService` 保存 culture，`{loc:Loc Key}` 将 XAML 绑定到资源并在文化切换时刷新。
- **插件 UI 贡献**：启动期插件可通过声明式 `IWpfUiContributionProvider` 贡献导航页、菜单/工具栏命令与设置页；宿主通过冻结 Catalog 统一渲染。

## 不包含的能力

本项目提供的是 **WPF 宿主基础设施**，而不是完整业务壳层。以下内容仍由上层应用自行决定：

- 具体业务页面、导航策略与权限控制；
- 消息框样式、主题资源内容与最终视觉规范；
- 菜单、工具栏、设置页等 UI Contribution 的最终渲染方式；
- 运行时热插拔插件管理界面。

## 使用

```csharp
using Axinyp.Wpf.Hosting.Extensions;
using Axinyp.Wpf.Hosting.Abstractions.Wpf;
using Axinyp.Wpf.Hosting.Wpf.Extensions;

var builder = Host.CreateApplicationBuilder(args);
builder.AddModularHosting();
builder.Services.AddWpfPresentation();

builder.Services.AddSingleton<MainWindowViewModel>();
builder.Services.AddView<MainWindowViewModel, MainWindow>();

using var host = builder.BuildModularHost();
await host.StartModularHostAsync();

var windows = host.Services.GetRequiredService<IWindowManager>();
Application.Current.MainWindow = await windows.ShowAsync<MainWindowViewModel>();
```

显示模态窗口时，调用方必须提供同一 Dispatcher 上已显示的 Owner：

```csharp
bool? accepted = await windows.ShowDialogAsync<SettingsViewModel>(Application.Current.MainWindow);
```

## 区域导航

XAML 仅标记区域名；实际注册在宿主完成加载后显式进行：

```xml
<ContentControl x:Name="MainContent"
                local:Region.Name="Main" />
```

```csharp
using var region = regionManager.RegisterRegion("Main", MainContent);
await navigation.NavigateAsync<HomeViewModel>("Main", parameter: lessonId);
await navigation.GoBackAsync("Main");
```

目标 ViewModel 实现 `INavigationAware` 可接收参数并响应进入/离开；实现 `IConfirmNavigation` 可以拒绝离开当前页。区域替换会清除旧 `DataContext` 并释放旧 DI scope，历史视觉树不会被缓存。

## Dialog 协议

```csharp
var result = await dialogs.ShowDialogAsync<SettingsDialogViewModel>(
    owner: Application.Current.MainWindow,
    parameters: new DialogParameters { ["Section"] = "AI" });
```

Dialog ViewModel 可实现 `IDialogAware`，在 `OnDialogOpened` 接收参数，并通过 `RequestClose` 返回 `DialogResult.Ok(payload)` 或 `DialogResult.Cancel()`。用户点击窗口关闭按钮同样会经过 `CanCloseDialog()`。

## 主题与本地化

```csharp
await themes.ApplyAsync("dark");
await localization.SetCultureAsync(CultureInfo.GetCultureInfo("en-US"));
```

主题只接受应用内受信任的 `pack://application` 资源字典；主题与文化都会写入 `ISettingsStore` 的用户偏好 JSON。该存储显式拒绝包含 `token`、`password`、`secret` 等敏感键，不能替代凭据管理器。

XAML 中用 `DynamicResource` 取得可切换颜色，使用 `LocExtension` 取得动态本地化文本：

```xml
<Grid Background="{DynamicResource App.BackgroundBrush}">
    <TextBlock Text="{loc:Loc MainWindow.Title}" />
</Grid>
```

## 本地包消费说明

当前仓库已经验证过：

- `dotnet pack` 生成本地 `.nupkg`；
- 外部消费者通过本地目录源恢复 `Axinyp.Wpf.Hosting` / `Axinyp.Wpf.Hosting.Wpf`；
- 最小 WPF 宿主可完成编译与装配。

当前不做的事情：

- 不推送到任何线上公共包源；
- 不把本项目描述为已经对外正式发布；
- 不保证公网版本号策略或线上兼容承诺。

如果只是本地验证，可直接参考仓库中的：

```text
consumer-smoke/
```

它演示了如何不依赖源码 `ProjectReference`，仅使用本地包源完成恢复与编译。

详见 [DESIGN.md](DESIGN.md)。
