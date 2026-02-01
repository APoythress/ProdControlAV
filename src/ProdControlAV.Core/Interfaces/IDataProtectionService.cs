using System.Threading.Tasks;

namespace ProdControlAV.Core.Interfaces;

/// <summary>
/// Service for encrypting and decrypting sensitive user data like phone numbers
/// </summary>
public interface IDataProtectionService
{
    /// <summary>
    /// Encrypt sensitive data
    /// </summary>
    /// <param name="plainText">Plain text to encrypt</param>
    /// <returns>Encrypted string</returns>
    string Protect(string plainText);

    /// <summary>
    /// Decrypt sensitive data
    /// </summary>
    /// <param name="encryptedText">Encrypted text to decrypt</param>
    /// <returns>Decrypted plain text</returns>
    string Unprotect(string encryptedText);
}
