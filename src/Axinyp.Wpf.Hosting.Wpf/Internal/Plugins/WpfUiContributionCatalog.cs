using Microsoft.Extensions.DependencyInjection;
using Axinyp.Wpf.Hosting.Abstractions.Modules;
using Axinyp.Wpf.Hosting.Abstractions.Wpf.Plugins;

namespace Axinyp.Wpf.Hosting.Wpf.Internal.Plugins;

internal sealed record WpfUiContributionProviderRegistration(Type ModuleType, Type ProviderType, int Order);

/// <summary>以模块拓扑顺序聚合插件 UI 元数据；任一 Provider 失败不会阻断其余贡献。</summary>
internal sealed class WpfUiContributionCatalog : IWpfUiContributionCatalog
{
    private readonly Lazy<Snapshot> _snapshot;

    public WpfUiContributionCatalog(IServiceProvider services, IModuleCatalog modules,
        IEnumerable<WpfUiContributionProviderRegistration> registrations)
        => _snapshot = new(() => Build(services, modules, registrations), LazyThreadSafetyMode.ExecutionAndPublication);

    public IReadOnlyList<WpfNavigationPageContribution> NavigationPages => _snapshot.Value.Navigation;
    public IReadOnlyList<WpfCommandContribution> Commands => _snapshot.Value.Commands;
    public IReadOnlyList<WpfSettingsPageContribution> SettingsPages => _snapshot.Value.Settings;
    public IReadOnlyList<WpfUiContributionFailure> Failures => _snapshot.Value.Failures;

    private static Snapshot Build(IServiceProvider services, IModuleCatalog modules,
        IEnumerable<WpfUiContributionProviderRegistration> registrations)
    {
        var indexes = modules.Modules.Select((module, index) => (module, index))
            .ToDictionary(item => item.module.ModuleType, item => (item.module.Descriptor.Id, item.index));
        var navigation = new List<WpfNavigationPageContribution>();
        var commands = new List<WpfCommandContribution>();
        var settings = new List<WpfSettingsPageContribution>();
        var failures = new List<WpfUiContributionFailure>();
        var navigationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var commandIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var settingsIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var registration in registrations.OrderBy(r => indexes.TryGetValue(r.ModuleType, out var source) ? source.index : int.MaxValue).ThenBy(r => r.Order))
        {
            if (!indexes.TryGetValue(registration.ModuleType, out var source))
            {
                failures.Add(new("<unknown>", registration.ProviderType, WpfUiContributionFailureKind.MissingModule, "Provider 声明的模块未进入模块目录。"));
                continue;
            }
            IWpfUiContributionProvider provider;
            try
            {
                provider = (IWpfUiContributionProvider)services.GetRequiredService(registration.ProviderType);
            }
            catch (Exception ex)
            {
                failures.Add(new(source.Id, registration.ProviderType, WpfUiContributionFailureKind.ProviderActivationFailed, ex.Message));
                continue;
            }

            try
            {
                var contribution = provider.GetContributions() ?? WpfUiContributions.Empty;
                Add(contribution.NavigationPages, navigation, navigationIds, source.Id, registration.ProviderType, failures,
                    page => !string.IsNullOrWhiteSpace(page.RegionName) && page.ViewModelType.IsClass);
                Add(contribution.Commands, commands, commandIds, source.Id, registration.ProviderType, failures,
                    command => command.Placement != WpfCommandPlacement.None && typeof(System.Windows.Input.ICommand).IsAssignableFrom(command.CommandType));
                Add(contribution.SettingsPages, settings, settingsIds, source.Id, registration.ProviderType, failures,
                    page => !string.IsNullOrWhiteSpace(page.Category) && page.ViewModelType.IsClass);
            }
            catch (Exception ex)
            {
                failures.Add(new(source.Id, registration.ProviderType, WpfUiContributionFailureKind.ProviderExecutionFailed, ex.Message));
            }
        }
        return new(navigation.OrderBy(x => x.Order).ToList(), commands.OrderBy(x => x.Order).ToList(), settings.OrderBy(x => x.Order).ToList(), failures);
    }

    private static void Add<T>(IEnumerable<T> values, ICollection<T> output, ISet<string> ids, string module,
        Type provider, ICollection<WpfUiContributionFailure> failures, Func<T, bool> isValid) where T : class
    {
        foreach (var value in values)
        {
            var id = value switch { WpfNavigationPageContribution p => p.Id, WpfCommandContribution c => c.Id, WpfSettingsPageContribution s => s.Id, _ => "" };
            if (string.IsNullOrWhiteSpace(id) || !isValid(value))
                failures.Add(new(module, provider, WpfUiContributionFailureKind.InvalidContribution, "UI 贡献元数据无效。"));
            else if (!ids.Add(id))
                failures.Add(new(module, provider, WpfUiContributionFailureKind.DuplicateContributionId, $"UI 贡献 Id '{id}' 重复。"));
            else output.Add(value);
        }
    }

    private sealed record Snapshot(IReadOnlyList<WpfNavigationPageContribution> Navigation,
        IReadOnlyList<WpfCommandContribution> Commands, IReadOnlyList<WpfSettingsPageContribution> Settings,
        IReadOnlyList<WpfUiContributionFailure> Failures);
}
