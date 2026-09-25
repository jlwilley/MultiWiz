namespace MultiWiz.Core.Security;

/// <summary>Stores account passwords outside the app's JSON files (Windows Credential Manager).</summary>
public interface ICredentialVault
{
    bool Save(Guid accountId, string username, string password);
    string? GetPassword(Guid accountId);
    bool Delete(Guid accountId);
}
