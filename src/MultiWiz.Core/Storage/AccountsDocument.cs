using System.Text.Json;
using System.Text.Json.Serialization;
using MultiWiz.Core.Accounts;

namespace MultiWiz.Core.Storage;

/// <summary>On-disk shape of <see cref="AppPaths.AccountsFile"/>.</summary>
public sealed class AccountsDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<Account> Accounts { get; set; } = [];

    /// <summary>Properties this build does not know (written by a newer one), kept so a save does not drop them.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
