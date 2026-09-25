using MultiWiz.Core.Accounts;

namespace MultiWiz.Core.Games;

/// <summary>Discovered installs (cached) plus custom installs from settings.</summary>
public interface IInstallCatalog
{
    IReadOnlyList<GameInstall> GetAll();
    GameInstall? Find(string installId);
    IReadOnlyList<GameInstall> ForGame(GameKind game);

    /// <summary>The install an account launches from (see docs/ARCHITECTURE.md for the rules), or null if none exists.</summary>
    GameInstall? Resolve(Account account);

    /// <summary>Re-runs discovery.</summary>
    void Refresh();

    event EventHandler? Changed;
}
