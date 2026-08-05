using Microsoft.Extensions.DependencyInjection;
using Axinyp.Wpf.Hosting.Abstractions.Modules;
using Axinyp.Wpf.Hosting.Abstractions.Wpf.Plugins;

namespace Axinyp.Wpf.Hosting.Wpf.Internal.Plugins;

internal sealed record WpfUiContributionProviderRegistration(Type ModuleType, Type ProviderType, int Order);

/// <summary>以模块拓扑顺序聚合插件 UI 元数据；任一 Provider 失败不会阻断其余贡献。</summary>
internal sealed class WpfUiContributionCatalog : IWpfUiContributionCatalog
{
    private readonly Lazy<Snapshot> _snapshot;

    public WpfUiContributionCatalog(
        IServiceScopeFactory scopeFactory,
        IModuleCatalog modules,
        IEnumerable<WpfUiContributionProviderRegistration> registrations,
        ILogger<WpfUiContributionCatalog> logger)
        => _snapshot = new(
            () => Build(scopeFactory, modules, registrations, logger),
            LazyThreadSafetyMode.ExecutionAndPublication);

    public IReadOnlyList<WpfNavigationPageContribution> NavigationPages => _snapshot.Value.Navigation;
    public IReadOnlyList<WpfCommandContribution> Commands => _snapshot.Value.Commands;
    public IReadOnlyList<WpfSettingsPageContribution> SettingsPages => _snapshot.Value.Settings;
    public IReadOnlyList<WpfUiContributionFailure> Failures => _snapshot.Value.Failures;

    private static Snapshot Build(
        IServiceScopeFactory scopeFactory,
        IModuleCatalog modules,
        IEnumerable<WpfUiContributionProviderRegistration> registrations,
        ILogger logger)
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

        foreach (var registration in registrations
                     .OrderBy(registration => indexes.TryGetValue(registration.ModuleType, out var source)
                         ? source.index
                         : int.MaxValue)
                     .ThenBy(registration => registration.Order))
        {
            if (!indexes.TryGetValue(registration.ModuleType, out var source))
            {
                AddFailure(failures, logger, "<unknown>", registration.ProviderType,
                    WpfUiContributionFailureKind.MissingModule,
                    "Provider 声明的模块未进入模块目录。", WpfHostingEventIds.UiContributionProviderMissingModule);
                continue;
            }

            WpfUiContributions contribution;
            var providerActivated = false;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var provider = (IWpfUiContributionProvider)scope.ServiceProvider
                    .GetRequiredService(registration.ProviderType);
                providerActivated = true;
                contribution = provider.GetContributions() ?? WpfUiContributions.Empty;
            }
            catch (Exception exception)
            {
                var kind = providerActivated
                    ? WpfUiContributionFailureKind.ProviderExecutionFailed
                    : WpfUiContributionFailureKind.ProviderActivationFailed;
                var message = providerActivated
                    ? "UI 贡献 Provider 执行失败。"
                    : "UI 贡献 Provider 无法激活。";
                var eventId = providerActivated
                    ? WpfHostingEventIds.UiContributionProviderExecutionFailed
                    : WpfHostingEventIds.UiContributionProviderActivationFailed;
                AddFailure(failures, logger, source.Id, registration.ProviderType, kind, message, eventId, exception);
                continue;
            }

            Add(contribution.NavigationPages, navigation, navigationIds, source.Id, registration.ProviderType, failures, logger,
                "NavigationPage", page => !string.IsNullOrWhiteSpace(page.RegionName) && page.ViewModelType.IsClass);
            Add(contribution.Commands, commands, commandIds, source.Id, registration.ProviderType, failures, logger,
                "Command", command => command.Placement != WpfCommandPlacement.None &&
                                  typeof(System.Windows.Input.ICommand).IsAssignableFrom(command.CommandType));
            Add(contribution.SettingsPages, settings, settingsIds, source.Id, registration.ProviderType, failures, logger,
                "SettingsPage", page => !string.IsNullOrWhiteSpace(page.Category) && page.ViewModelType.IsClass);
        }

        logger.LogInformation(WpfHostingEventIds.UiContributionCatalogBuilt,
            "WPF UI Contribution 目录已构建：NavigationPages={NavigationPageCount}; Commands={CommandCount}; SettingsPages={SettingsPageCount}; Failures={FailureCount}。",
            navigation.Count, commands.Count, settings.Count, failures.Count);
        return new(
            navigation.OrderBy(contribution => contribution.Order).ToList(),
            commands.OrderBy(contribution => contribution.Order).ToList(),
            settings.OrderBy(contribution => contribution.Order).ToList(),
            failures);
    }

    private static void Add<T>(
        IEnumerable<T> values,
        ICollection<T> output,
        ISet<string> ids,
        string module,
        Type provider,
        ICollection<WpfUiContributionFailure> failures,
        ILogger logger,
        string contributionKind,
        Func<T, bool> isValid)
        where T : class
    {
        foreach (var value in values)
        {
            var id = value switch
            {
                WpfNavigationPageContribution page => page.Id,
                WpfCommandContribution command => command.Id,
                WpfSettingsPageContribution page => page.Id,
                _ => ""
            };

            if (string.IsNullOrWhiteSpace(id) || !isValid(value))
            {
                AddFailure(failures, logger, module, provider, WpfUiContributionFailureKind.InvalidContribution,
                    "UI 贡献元数据无效。", WpfHostingEventIds.InvalidUiContribution,
                    contributionKind: contributionKind, contributionId: string.IsNullOrWhiteSpace(id) ? null : id);
            }
            else if (!ids.Add(id))
            {
                AddFailure(failures, logger, module, provider, WpfUiContributionFailureKind.DuplicateContributionId,
                    "UI 贡献 Id 重复。", WpfHostingEventIds.DuplicateUiContributionId,
                    contributionKind: contributionKind, contributionId: id);
            }
            else
            {
                output.Add(value);
            }
        }
    }

    private static void AddFailure(
        ICollection<WpfUiContributionFailure> failures,
        ILogger logger,
        string sourceModuleId,
        Type providerType,
        WpfUiContributionFailureKind kind,
        string message,
        EventId eventId,
        Exception? exception = null,
        string? contributionKind = null,
        string? contributionId = null)
    {
        failures.Add(new WpfUiContributionFailure(sourceModuleId, providerType, kind, message)
        {
            ExceptionType = exception?.GetType().FullName,
            ContributionKind = contributionKind,
            ContributionId = contributionId
        });
        logger.LogWarning(eventId, exception,
            "WPF UI Contribution 失败。SourceModuleId={SourceModuleId}; ProviderType={ProviderType}; FailureKind={FailureKind}; ContributionKind={ContributionKind}; ContributionId={ContributionId}",
            sourceModuleId, providerType.FullName, kind, contributionKind, contributionId);
    }

    private sealed record Snapshot(
        IReadOnlyList<WpfNavigationPageContribution> Navigation,
        IReadOnlyList<WpfCommandContribution> Commands,
        IReadOnlyList<WpfSettingsPageContribution> Settings,
        IReadOnlyList<WpfUiContributionFailure> Failures);
}
