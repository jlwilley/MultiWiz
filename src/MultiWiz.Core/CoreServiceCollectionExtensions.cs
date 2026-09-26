using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using MultiWiz.Core.Accounts;
using MultiWiz.Core.Games;
using MultiWiz.Core.Hotkeys;
using MultiWiz.Core.Legacy;
using MultiWiz.Core.Patching;
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
        services.AddSingleton<ISessionLinking>(static provider => provider.GetRequiredService<SessionManager>());
        services.AddSingletonWithInterface<IClientSwitcher, ClientSwitcher>();
        services.AddSingletonWithInterface<IHotkeyCoordinator, HotkeyCoordinator>();
        services.AddSingletonWithInterface<ILegacyImporter, LegacyImporter>();

        // Game file downloads: one shared HttpClient for the patch CDN (tests construct GameDownloader directly).
        services.TryAddSingleton<IPatchConnectionFactory, TcpPatchConnectionFactory>();
        services.AddSingletonWithInterface<IPatchServerClient, PatchServerClient>();
        services.AddSingleton(static provider => new GameDownloader(
            provider.GetRequiredService<AppPaths>(),
            provider.GetRequiredService<IPatchServerClient>(),
            GameDownloader.CreateDefaultHttpClient(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<GameDownloader>>()));
        services.AddSingleton<IGameDownloader>(static provider => provider.GetRequiredService<GameDownloader>());

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
