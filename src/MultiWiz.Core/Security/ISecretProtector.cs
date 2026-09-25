namespace MultiWiz.Core.Security;

/// <summary>Current-user data protection (Windows DPAPI). Used to read the v3 config during import.</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plaintext);

    /// <summary>Throws <see cref="System.Security.Cryptography.CryptographicException"/> if the data cannot be decrypted.</summary>
    byte[] Unprotect(byte[] protectedData);
}
