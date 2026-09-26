using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MultiWiz.Core.Accounts;
using MultiWiz.Core.Games;
using MultiWiz.Core.Hotkeys;
using MultiWiz.Core.Legacy;
using MultiWiz.Core.Sessions;
using MultiWiz.Core.Settings;
using MultiWiz.Core.Storage;
using MultiWiz.Core.Switching;
using MultiWiz.Core.Teams;

namespace MultiWiz.Core;

public static class CoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers every Core service as a singleton. Each concrete type is registered once and its interfaces resolve
    /// to that same instance. Also registers <paramref name="paths"/> and <see cref="TimeProvider.System"/> (unless a
    /// <see cref="TimeProvider"/> is already registered). Platform services and logging are registered separately.
    /// </summary>
    public static IServiceCollection AddMultiWizCore(this IServiceCollection services, AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(paths);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(paths);

        services.AddSingletonWithInterface<ISettingsStore, JsonSettingsStore>();
        services.AddSingletonWithInterface<IAccountStore, JsonAccountStore>();
        services.AddSingletonWithInterface<ITeamStore, JsonTeamStore>();
        services.AddSingletonWithInterface<IRealmCatalog, RealmCatalog>();
        services.AddSingletonWithInterface<IInstallCatalog, InstallCatalog>();
        services.AddSingletonWithInterface<IWindowArranger, WindowArranger>();
        services.AddSingletonWithInterface<ITeamLauncher, TeamLauncher>();
        services.AddSingletonWithInterface<ISessionManager, SessionManager>();
        services.AddSingleton<ISessionEvents>(static provider => provider.GetRequiredService<SessionManager>());
        services.AddSingleton<ISessionLogin>(static provider => provider.GetRequiredService<SessionManager>());
        services.AddSingletonWithInterface<IClientSwitcher, ClientSwitcher>();
        services.AddSingletonWithInterface<IHotkeyCoordinator, HotkeyCoordinator>();
        services.AddSingletonWithInterface<ILegacyImporter, LegacyImporter>();

        return services;
    }

    private static void AddSingletonWithInterface<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        services.AddSingleton<TImplementation>();
        services.AddSingleton<TService>(static provider => provider.GetRequiredService<TImplementation>());
    }
}
