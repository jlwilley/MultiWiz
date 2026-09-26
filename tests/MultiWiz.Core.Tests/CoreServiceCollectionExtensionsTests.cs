using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MultiWiz.Core.Accounts;
using MultiWiz.Core.Games;
using MultiWiz.Core.Hotkeys;
using MultiWiz.Core.Legacy;
using MultiWiz.Core.Platform;
using MultiWiz.Core.Security;
using MultiWiz.Core.Sessions;
using MultiWiz.Core.Settings;
using MultiWiz.Core.Storage;
using MultiWiz.Core.Switching;
using MultiWiz.Core.Teams;
using MultiWiz.Core.Tests.Fakes;
using MultiWiz.Core.Tests.Support;

namespace MultiWiz.Core.Tests;

public sealed class CoreServiceCollectionExtensionsTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Registers_every_core_service_as_a_shared_singleton()
    {
        var paths = _temp.CreateAppPaths();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        AddFakePlatform(services);

        services.AddMultiWizCore(paths);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.Same(paths, provider.GetRequiredService<AppPaths>());
        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
        AssertShared<ISettingsStore, JsonSettingsStore>(provider);
        AssertShared<IAccountStore, JsonAccountStore>(provider);
        AssertShared<ITeamStore, JsonTeamStore>(provider);
        AssertShared<IRealmCatalog, RealmCatalog>(provider);
        AssertShared<IInstallCatalog, InstallCatalog>(provider);
        AssertShared<IWindowArranger, WindowArranger>(provider);
        AssertShared<ITeamLauncher, TeamLauncher>(provider);
        AssertShared<ISessionManager, SessionManager>(provider);
        AssertShared<ISessionEvents, SessionManager>(provider);
        AssertShared<ISessionLogin, SessionManager>(provider);
        AssertShared<ISessionLinking, SessionManager>(provider);
        AssertShared<IClientSwitcher, ClientSwitcher>(provider);
        AssertShared<IHotkeyCoordinator, HotkeyCoordinator>(provider);
        AssertShared<ILegacyImporter, LegacyImporter>(provider);
    }

    [Fact]
    public void Keeps_a_time_provider_that_was_registered_first()
    {
        var custom = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(custom);

        services.AddMultiWizCore(_temp.CreateAppPaths());

        using var provider = services.BuildServiceProvider();
        Assert.Same(custom, provider.GetRequiredService<TimeProvider>());
    }

    private static void AssertShared<TService, TImplementation>(IServiceProvider provider)
        where TService : class
        where TImplementation : class
    {
        var viaInterface = provider.GetRequiredService<TService>();
        Assert.IsType<TImplementation>(viaInterface);
        Assert.Same(provider.GetRequiredService<TImplementation>(), viaInterface);
        Assert.Same(viaInterface, provider.GetRequiredService<TService>());
    }

    private static void AddFakePlatform(IServiceCollection services)
    {
        services.AddSingleton<IInstallLocator>(new FakeInstallLocator());
        services.AddSingleton<ISteamSupport>(new FakeSteamSupport());
        services.AddSingleton<IProcessLauncher>(new FakeProcessLauncher(TimeProvider.System));
        services.AddSingleton<IWindowService>(new FakeWindowService());
        services.AddSingleton<IWindowEvents>(new FakeWindowEvents());
        services.AddSingleton<IInputSender>(new FakeInputSender());
        services.AddSingleton<IDisplayService>(new FakeDisplayService());
        services.AddSingleton<IHotkeyService>(new FakeHotkeyService());
        services.AddSingleton<IAudioService>(new FakeAudioService());
        services.AddSingleton<IProcessThrottler>(new FakeProcessThrottler());
        services.AddSingleton<ICredentialVault>(new FakeCredentialVault());
        services.AddSingleton<ISecretProtector>(new FakeSecretProtector());
    }
}
