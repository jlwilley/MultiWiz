using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MultiWiz.Core.Accounts;
using MultiWiz.Core.Games;
using MultiWiz.Core.Sessions;
using MultiWiz.Core.Settings;
using MultiWiz.Core.Storage;
using MultiWiz.Core.Tests.Fakes;

namespace MultiWiz.Core.Tests.Support;

/// <summary>A <see cref="SessionManager"/> wired to fakes, fake time, and a temp-folder Wizard101 install.</summary>
internal sealed class SessionHarness : IDisposable
{
    private readonly InstallCatalog _installCatalog;

    public SessionHarness(LoginSettings login)
    {
        Settings = new FakeSettingsStore(new AppSettings { Login = login });
        Launcher = new FakeProcessLauncher(Time);
        StandaloneInstall = new GameInstall
        {
            Id = "standalone-wizard101",
            Game = GameKind.Wizard101,
            Source = InstallSource.Standalone,
            RootPath = Temp.CreateGameFolder("Wizard101", GameExecutables.ClientExecutableName(GameKind.Wizard101)),
        };
        Locator.Installs.Add(StandaloneInstall);

        _installCatalog = new InstallCatalog(Locator, Settings, NullLogger<InstallCatalog>.Instance);
        Paths = Temp.CreateAppPaths();
        Manager = CreateManager();
    }

    public AppPaths Paths { get; }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    public TempDirectory Temp { get; } = new();

    public FakeAccountStore Accounts { get; } = new();

    public FakeSettingsStore Settings { get; }

    public FakeInstallLocator Locator { get; } = new();

    public FakeSteamSupport Steam { get; } = new();

    public FakeProcessLauncher Launcher { get; }

    public FakeWindowService Windows { get; } = new();

    public FakeInputSender Input { get; } = new();

    public FakeCredentialVault Vault { get; } = new();

    public FakeAudioService Audio { get; } = new();

    public FakeProcessThrottler Throttler { get; } = new();

    public GameInstall StandaloneInstall { get; }

    public SessionManager Manager { get; }

    public static LoginSettings FastLogin(int windowTimeoutSeconds = 10, int staggerSeconds = 2, bool autoLogin = true, bool refocus = false) => new()
    {
        AutoLogin = autoLogin,
        ReadyDelaySeconds = 1,
        WindowTimeoutSeconds = windowTimeoutSeconds,
        KeystrokeDelayMs = 0,
        StaggerSeconds = staggerSeconds,
        RefocusAfterLogin = refocus,
    };

    /// <summary>A new manager over the same fakes and files, like MultiWiz starting again.</summary>
    public SessionManager CreateManager() => new(
        Accounts,
        new RealmCatalog(Settings),
        _installCatalog,
        Settings,
        Steam,
        Launcher,
        Windows,
        Input,
        Vault,
        Audio,
        Throttler,
        Paths,
        Time,
        NullLogger<SessionManager>.Instance);

    public Account AddAccount(string name, string? password = "correct horse", string? installId = null)
    {
        var account = Accounts.Add(name, installId: installId);
        if (password is not null)
        {
            Vault.Save(account.Id, account.Username, password);
        }

        return account;
    }

    public GameInstall AddSteamInstall()
    {
        var install = new GameInstall
        {
            Id = "steam-wizard101-799960",
            Game = GameKind.Wizard101,
            Source = InstallSource.Steam,
            RootPath = Temp.CreateGameFolder("SteamWizard101", GameExecutables.ClientExecutableName(GameKind.Wizard101)),
            SteamAppId = "799960",
        };
        Locator.Installs.Add(install);
        return install;
    }

    /// <summary>Completes when a session of the account reaches <paramref name="state"/> (real-time safety timeout).</summary>
    public Task<ClientSession> WaitForStateAsync(Guid accountId, ClientSessionState state, SessionManager? manager = null)
    {
        var reached = new TaskCompletionSource<ClientSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        (manager ?? Manager).SessionChanged += (_, session) =>
        {
            if (session.AccountId == accountId && session.State == state)
            {
                reached.TrySetResult(session);
            }
        };
        return reached.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    public void Dispose()
    {
        Manager.Dispose();
        _installCatalog.Dispose();
        Temp.Dispose();
    }
}
