using Microsoft.AspNetCore.DataProtection;
using ProdControlAV.Core.Interfaces;

namespace ProdControlAV.API.Services;

/// <summary>
/// Service for encrypting and decrypting sensitive data using ASP.NET Core Data Protection API
/// </summary>
public class AspNetCoreDataProtectionService : IDataProtectionService
{
    private readonly IDataProtector _protector;

    public AspNetCoreDataProtectionService(IDataProtectionProvider dataProtectionProvider)
    {
        // Create a protector with a specific purpose string for phone numbers
        _protector = dataProtectionProvider.CreateProtector("ProdControlAV.UserData.PhoneNumbers");
    }

    public string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return string.Empty;
        }

        return _protector.Protect(plainText);
    }

    public string Unprotect(string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText))
        {
            return string.Empty;
        }

        return _protector.Unprotect(encryptedText);
    }
}
