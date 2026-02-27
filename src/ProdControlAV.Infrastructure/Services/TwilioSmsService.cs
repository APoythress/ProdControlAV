using System;
using System.Text.RegularExpressions;
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
        _config = config.Value ?? new TwilioConfig();
        
        // Keep raw values so we can detect placeholders that didn't resolve
        var rawAccount = _config.AccountSid;
        var rawToken = _config.AuthToken;
        var rawFrom = _config.FromPhoneNumber;

        _config.AccountSid = ResolveEnvVar(rawAccount);
        _config.AuthToken = ResolveEnvVar(rawToken);
        _config.FromPhoneNumber = ResolveEnvVar(rawFrom);
        
        // Log any placeholders that couldn't be resolved
        if (IsPlaceholder(rawAccount) && _config.AccountSid == rawAccount)
            _logger.LogWarning("Twilio AccountSid placeholder present but environment variable '{Env}' was not set.", rawAccount);
        if (IsPlaceholder(rawToken) && _config.AuthToken == rawToken)
            _logger.LogWarning("Twilio AuthToken placeholder present but environment variable '{Env}' was not set.", rawToken);
        if (IsPlaceholder(rawFrom) && _config.FromPhoneNumber == rawFrom)
            _logger.LogWarning("Twilio FromPhoneNumber placeholder present but environment variable '{Env}' was not set.", rawFrom);

        _isConfigured = !string.IsNullOrWhiteSpace(_config?.AccountSid)
                        && !string.IsNullOrWhiteSpace(_config?.AuthToken)
                        && !string.IsNullOrWhiteSpace(_config?.FromPhoneNumber);

        if (_isConfigured)
        {
            try
            {
                TwilioClient.Init(_config.AccountSid, _config.AuthToken);
                _logger.LogInformation("Twilio SMS service initialized successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Twilio client.");
                _isConfigured = false;
            }
        }
        else
        {
            _logger.LogWarning("Twilio SMS service not configured. SMS notifications will not be sent.");
        }
    }
    
    private static string? ResolveEnvVar(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var trimmed = value.Trim();

        string? envVarName = null;

        if (trimmed.StartsWith("${", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal))
        {
            envVarName = trimmed.Substring(2, trimmed.Length - 3);
        }
        else if (trimmed.StartsWith("%", StringComparison.Ordinal) && trimmed.EndsWith("%", StringComparison.Ordinal))
        {
            envVarName = trimmed.Substring(1, trimmed.Length - 2);
        }
        else if (trimmed.StartsWith("$", StringComparison.Ordinal) && Regex.IsMatch(trimmed, @"^\$[A-Za-z0-9_]+$"))
        {
            envVarName = trimmed.Substring(1);
        }

        if (!string.IsNullOrEmpty(envVarName))
        {
            var envValue = Environment.GetEnvironmentVariable(envVarName);
            return string.IsNullOrEmpty(envValue) ? value : envValue;
        }

        return value;
    }
    
    private static bool IsPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var trimmed = value.Trim();
        return (trimmed.StartsWith("${") && trimmed.EndsWith("}"))
               || (trimmed.StartsWith("%") && trimmed.EndsWith("%"))
               || (trimmed.StartsWith("$") && Regex.IsMatch(trimmed, @"^\$[A-Za-z0-9_]+$"));
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
