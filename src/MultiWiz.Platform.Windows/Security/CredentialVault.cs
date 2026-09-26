using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using MultiWiz.Core.Security;
using Windows.Win32;
using Windows.Win32.Security.Credentials;

namespace MultiWiz.Platform.Windows.Security;

/// <summary>
/// Account passwords in Windows Credential Manager (generic credentials, target
/// <c>MultiWiz:account:{id:N}</c>). The password is stored as UTF-16LE and never copied into temporary managed buffers.
/// </summary>
internal sealed unsafe class CredentialVault : ICredentialVault
{
    // CRED_MAX_CREDENTIAL_BLOB_SIZE is 5 * 512 bytes, i.e. 1,280 UTF-16 characters.
    private const int MaxPasswordLength = 1280;
    private const int ErrorNotFound = 1168;

    private readonly ILogger<CredentialVault> _logger;

    public CredentialVault(ILogger<CredentialVault> logger)
    {
        _logger = logger;
    }

    public bool Save(Guid accountId, string username, string password)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(password);

        if (password.Length > MaxPasswordLength)
        {
            _logger.LogWarning(
                "The password for account {AccountId} is longer than {MaxLength} characters and was not saved.",
                accountId, MaxPasswordLength);
            return false;
        }

        var target = TargetName(accountId);
        fixed (char* targetName = target)
        fixed (char* userName = username)
        fixed (char* secret = password)
        {
            // The string's own UTF-16LE memory is the blob, so no copy of the password is made.
            var credential = new CREDENTIALW
            {
                Type = CRED_TYPE.CRED_TYPE_GENERIC,
                TargetName = targetName,
                UserName = userName,
                CredentialBlob = (byte*)secret,
                CredentialBlobSize = (uint)(password.Length * sizeof(char)),
                Persist = CRED_PERSIST.CRED_PERSIST_LOCAL_MACHINE,
            };

            if (!PInvoke.CredWrite(in credential, 0))
            {
                _logger.LogWarning(
                    "Could not save the password for account {AccountId} (error {Error}).",
                    accountId, Marshal.GetLastPInvokeError());
                return false;
            }
        }

        return true;
    }

    public string? GetPassword(Guid accountId)
    {
        if (!PInvoke.CredRead(TargetName(accountId), CRED_TYPE.CRED_TYPE_GENERIC, out CREDENTIALW* credential))
        {
            var error = Marshal.GetLastPInvokeError();
            if (error != ErrorNotFound)
            {
                _logger.LogWarning("Could not read the password for account {AccountId} (error {Error}).", accountId, error);
            }

            return null;
        }

        try
        {
            var size = (int)credential->CredentialBlobSize;
            if (size == 0 || credential->CredentialBlob == null)
            {
                return string.Empty;
            }

            var password = new string((char*)credential->CredentialBlob, 0, size / sizeof(char));

            // Wipe the native copy before handing the buffer back.
            new Span<byte>(credential->CredentialBlob, size).Clear();
            return password;
        }
        finally
        {
            PInvoke.CredFree(credential);
        }
    }

    public bool Delete(Guid accountId)
    {
        if (PInvoke.CredDelete(TargetName(accountId), CRED_TYPE.CRED_TYPE_GENERIC))
        {
            return true;
        }

        var error = Marshal.GetLastPInvokeError();
        if (error != ErrorNotFound)
        {
            _logger.LogWarning("Could not delete the password for account {AccountId} (error {Error}).", accountId, error);
        }

        return false;
    }

    private static string TargetName(Guid accountId) => $"MultiWiz:account:{accountId:N}";
}
