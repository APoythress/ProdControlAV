using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProdControlAV.Core.Interfaces;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace ProdControlAV.Infrastructure.Services;

public class TwilioSmsService : ISmsService
{
    private readonly ILogger<TwilioSmsService> _logger;
    private readonly TwilioConfig _config;
    private readonly bool _isConfigured;

    public TwilioSmsService(IOptions<TwilioConfig> config, ILogger<TwilioSmsService> logger)
    {
        _logger = logger;
        _config = config.Value;
        
        // Check if Twilio is properly configured
        _isConfigured = !string.IsNullOrEmpty(_config?.AccountSid) &&
                       !string.IsNullOrEmpty(_config?.AuthToken) &&
                       !string.IsNullOrEmpty(_config?.FromPhoneNumber);
        
        if (_isConfigured)
        {
            TwilioClient.Init(_config.AccountSid, _config.AuthToken);
            _logger.LogInformation("Twilio SMS service initialized successfully");
        }
        else
        {
            _logger.LogWarning("Twilio SMS service not configured. SMS notifications will not be sent.");
        }
    }

    public async Task<bool> SendSmsAsync(string toPhoneNumber, string message, CancellationToken cancellationToken = default)
    {
        if (!_isConfigured)
        {
            _logger.LogWarning("Cannot send SMS: Twilio not configured");
            return false;
        }

        if (string.IsNullOrEmpty(toPhoneNumber))
        {
            _logger.LogWarning("Cannot send SMS: Phone number is empty");
            return false;
        }

        if (string.IsNullOrEmpty(message))
        {
            _logger.LogWarning("Cannot send SMS: Message is empty");
            return false;
        }

        try
        {
            _logger.LogInformation("Sending SMS to {PhoneNumber} (length: {MessageLength} chars)", 
                MaskPhoneNumber(toPhoneNumber), message.Length);

            var messageResource = await MessageResource.CreateAsync(
                to: new PhoneNumber(toPhoneNumber),
                from: new PhoneNumber(_config.FromPhoneNumber),
                body: message
            );

            if (messageResource.ErrorCode.HasValue)
            {
                _logger.LogError("Failed to send SMS. Error code: {ErrorCode}, Message: {ErrorMessage}",
                    messageResource.ErrorCode, messageResource.ErrorMessage);
                return false;
            }

            _logger.LogInformation("SMS sent successfully. SID: {MessageSid}, Status: {Status}",
                messageResource.Sid, messageResource.Status);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while sending SMS to {PhoneNumber}", MaskPhoneNumber(toPhoneNumber));
            return false;
        }
    }

    /// <summary>
    /// Mask phone number for logging (show only last 4 digits)
    /// </summary>
    private static string MaskPhoneNumber(string phoneNumber)
    {
        if (string.IsNullOrEmpty(phoneNumber) || phoneNumber.Length < 4)
        {
            return "***";
        }
        
        return "***-***-" + phoneNumber.Substring(phoneNumber.Length - 4);
    }
}

public class TwilioConfig
{
    public string? AccountSid { get; set; }
    public string? AuthToken { get; set; }
    public string? FromPhoneNumber { get; set; }
}
