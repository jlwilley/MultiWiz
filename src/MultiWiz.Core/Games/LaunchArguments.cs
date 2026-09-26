using System.Globalization;

namespace MultiWiz.Core.Games;

/// <summary>Builds the game client's command line.</summary>
public static class LaunchArguments
{
    /// <summary>
    /// <c>-ST</c> when the install is a Steam install, then <c>-L host port</c> for the realm, then the
    /// install's extra arguments (if any), separated by single spaces.
    /// </summary>
    public static string Build(Realm realm, GameInstall install)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(install);

        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(install.SteamAppId))
        {
            parts.Add("-ST");
        }

        parts.Add($"-L {realm.LoginHost.Trim()} {realm.LoginPort.ToString(CultureInfo.InvariantCulture)}");

        if (!string.IsNullOrWhiteSpace(install.ExtraArguments))
        {
            parts.Add(install.ExtraArguments.Trim());
        }

        return string.Join(' ', parts);
    }
}
