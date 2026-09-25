using MultiWiz.Core.Games;

namespace MultiWiz.Core.Platform;

/// <summary>Finds game installations on this machine (standalone installer locations, uninstall registry keys, Steam libraries).</summary>
public interface IInstallLocator
{
    /// <summary>Installs whose client executable exists on disk. Ids are deterministic across runs.</summary>
    IReadOnlyList<GameInstall> Discover();
}
