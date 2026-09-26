using MultiWiz.Core.Accounts;

namespace MultiWiz.Core.Storage;

/// <summary>On-disk shape of <see cref="AppPaths.AccountsFile"/>.</summary>
public sealed class AccountsDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<Account> Accounts { get; set; } = [];
}
