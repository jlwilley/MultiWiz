using Microsoft.Extensions.DependencyInjection;
using MultiWiz.Core.Live;
using MultiWiz.Core.Platform;
using MultiWiz.Core.Security;
using MultiWiz.Platform.Windows.Audio;
using MultiWiz.Platform.Windows.Games;
using MultiWiz.Platform.Windows.Input;
using MultiWiz.Platform.Windows.Processes;
using MultiWiz.Platform.Windows.Security;
using MultiWiz.Platform.Windows.Windowing;

namespace MultiWiz.Platform.Windows;

public static class WindowsPlatformServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Windows implementations of every <c>MultiWiz.Core</c> platform interface as singletons.
    /// Requires logging (<c>ILogger&lt;T&gt;</c>) to be registered by the host.
    /// </summary>
    public static IServiceCollection AddWindowsPlatform(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<Win32MessageThread>();
        services.AddSingleton<IHotkeyService, HotkeyService>();
        services.AddSingleton<IWindowEvents, WindowEventsService>();
        services.AddSingleton<IWindowService, WindowService>();
        services.AddSingleton<IInputSender, InputSender>();
        services.AddSingleton<IDisplayService, DisplayService>();
        services.AddSingleton<IThumbnailService, ThumbnailService>();
        services.AddSingleton<IOverlayWindowStyler, OverlayWindowStyler>();
        services.AddSingleton<IProcessLauncher, ProcessLauncher>();
        services.AddSingleton<IProcessThrottler, ProcessThrottler>();
        services.AddSingleton<IProcessMemoryFactory, ProcessMemoryFactory>();
        services.AddSingleton<IAudioService, AudioService>();
        services.AddSingleton<IInstallLocator, InstallLocator>();
        services.AddSingleton<ISteamSupport, SteamSupport>();
        services.AddSingleton<ICredentialVault, CredentialVault>();
        services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        return services;
    }
}
