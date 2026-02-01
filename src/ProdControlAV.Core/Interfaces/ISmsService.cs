using System.Threading;
using System.Threading.Tasks;

namespace ProdControlAV.Core.Interfaces;

/// <summary>
/// Service for sending SMS notifications
/// </summary>
public interface ISmsService
{
    /// <summary>
    /// Send an SMS message to a phone number
    /// </summary>
    /// <param name="toPhoneNumber">Recipient phone number in E.164 format (e.g., +15551234567)</param>
    /// <param name="message">Message content (max 160 characters for single SMS)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if message was sent successfully, false otherwise</returns>
    Task<bool> SendSmsAsync(string toPhoneNumber, string message, CancellationToken cancellationToken = default);
}
