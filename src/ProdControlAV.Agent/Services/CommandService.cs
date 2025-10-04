using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProdControlAV.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ProdControlAV.Agent.Services;

public class CommandPullRequest 
{ 
    public int Max { get; set; } = 10; 
}

public class CommandPullResponse 
{ 
    public List<CommandEnvelope> Commands { get; set; } = new(); 
}

public class CommandCompleteRequest 
{ 
    public Guid CommandId { get; set; } 
    public bool Success { get; set; } 
    public string? Message { get; set; } 
    public int? DurationMs { get; set; } 
}

public interface ICommandService
{
    Task<List<CommandEnvelope>> PollCommandsAsync(CancellationToken ct);
    Task ExecuteCommandAsync(CommandEnvelope command, CancellationToken ct);
    Task CompleteCommandAsync(Guid commandId, bool success, string? message, int? durationMs, CancellationToken ct);
}

public class CommandService : ICommandService
{
    private readonly HttpClient _http;
    private readonly ILogger<CommandService> _logger;
    private readonly ApiOptions _api;
    private readonly IJwtAuthService _jwtAuth;

    // Explicit JsonSerializerOptions with a TypeInfoResolver to opt-out of the reflection-disabled behavior
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public CommandService(HttpClient http, ILogger<CommandService> logger, IOptions<ApiOptions> api, IJwtAuthService jwtAuth)
    {
        _http = http;
        _logger = logger;
        _api = api.Value;
        _jwtAuth = jwtAuth;
        _http.BaseAddress = new Uri(_api.BaseUrl);
    }

    public async Task<List<CommandEnvelope>> PollCommandsAsync(CancellationToken ct)
    {
        try
        {
            // Use the new queue-based endpoint instead of SQL polling
            if (string.IsNullOrWhiteSpace(_api.CommandsEndpoint))
                return new List<CommandEnvelope>();

            // Get valid JWT token
            var token = await _jwtAuth.GetValidTokenAsync(ct);
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("Failed to obtain valid JWT token for command polling");
                return new List<CommandEnvelope>();
            }

            // Use the new /commands/receive endpoint for queue-based polling
            var endpoint = _api.CommandsEndpoint.Replace("/commands/next", "/commands/receive");
            
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to poll commands: {StatusCode}", res.StatusCode);
                return new List<CommandEnvelope>();
            }

            var responseJson = await res.Content.ReadFromJsonAsync<JsonElement>(s_jsonOptions, ct);
            
            // Check if command is null (no messages available)
            if (!responseJson.TryGetProperty("command", out var commandProp) || 
                commandProp.ValueKind == JsonValueKind.Null)
            {
                return new List<CommandEnvelope>();
            }

            // Parse the command from the response
            var command = new CommandEnvelope
            {
                CommandId = Guid.Parse(commandProp.GetProperty("commandId").GetString()!),
                DeviceId = Guid.Parse(commandProp.GetProperty("deviceId").GetString()!),
                Verb = commandProp.GetProperty("verb").GetString()!,
                Payload = commandProp.TryGetProperty("payload", out var payload) ? payload.GetString() : null
            };
            
            // Store message metadata for later acknowledgment
            if (commandProp.TryGetProperty("messageId", out var msgId) && 
                commandProp.TryGetProperty("popReceipt", out var popReceipt))
            {
                command.MessageId = msgId.GetString();
                command.PopReceipt = popReceipt.GetString();
            }

            return new List<CommandEnvelope> { command };
        }
        catch (OperationCanceledException)
        {
            return new List<CommandEnvelope>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error polling for commands");
            return new List<CommandEnvelope>();
        }
    }

    public async Task ExecuteCommandAsync(CommandEnvelope command, CancellationToken ct)
    {
        var startTime = DateTime.UtcNow;
        bool success = false;
        string message = "";

        try
        {
            // For security, only execute whitelisted commands
            switch (command.Verb?.ToUpperInvariant())
            {
                case "PING":
                    await ExecutePingCommand(command, ct);
                    success = true;
                    message = "Ping command executed successfully";
                    break;
                
                case "STATUS":
                    await ExecuteStatusCommand(command, ct);
                    success = true;
                    message = "Status command executed successfully";
                    break;
                    
                default:
                    message = $"Unknown or unauthorized command: {command.Verb}";
                    _logger.LogWarning(message);
                    break;
            }
        }
        catch (Exception ex)
        {
            message = $"Command execution failed: {ex.Message}";
            _logger.LogError(ex, "Error executing command {CommandId} - {Verb}", command.CommandId, command.Verb);
        }

        var durationMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
        
        // Complete the command in SQL (authoritative record)
        await CompleteCommandAsync(command.CommandId, success, message, durationMs, ct);
        
        // If command came from queue (has MessageId), acknowledge it to delete from queue
        if (success && !string.IsNullOrEmpty(command.MessageId) && !string.IsNullOrEmpty(command.PopReceipt))
        {
            await AcknowledgeCommandAsync(command.MessageId, command.PopReceipt, ct);
        }
    }

    public async Task CompleteCommandAsync(Guid commandId, bool success, string? message, int? durationMs, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_api.CommandCompleteEndpoint))
                return;

            // Get valid JWT token
            var token = await _jwtAuth.GetValidTokenAsync(ct);
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("Failed to obtain valid JWT token for command completion");
                return;
            }

            var request = new CommandCompleteRequest
            {
                CommandId = commandId,
                Success = success,
                Message = message,
                DurationMs = durationMs
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, _api.CommandCompleteEndpoint)
            {
                Content = JsonContent.Create(request, options: s_jsonOptions)
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var res = await _http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            
            _logger.LogInformation("Command {CommandId} completed: {Success}", commandId, success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to report command completion for {CommandId}", commandId);
        }
    }
    
    private async Task AcknowledgeCommandAsync(string messageId, string popReceipt, CancellationToken ct)
    {
        try
        {
            // Get valid JWT token
            var token = await _jwtAuth.GetValidTokenAsync(ct);
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("Failed to obtain valid JWT token for command acknowledgment");
                return;
            }

            var endpoint = _api.CommandsEndpoint?.Replace("/commands/next", "/commands/acknowledge") 
                ?? "/api/agents/commands/acknowledge";
            
            var request = new { messageId, popReceipt };

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(request, options: s_jsonOptions)
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var res = await _http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            
            _logger.LogInformation("Command message {MessageId} acknowledged and deleted from queue", messageId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to acknowledge command message {MessageId}", messageId);
        }
    }

    private async Task ExecutePingCommand(CommandEnvelope command, CancellationToken ct)
    {
        // For security, this is a controlled ping operation
        _logger.LogInformation("Executing ping command for device {DeviceId}", command.DeviceId);
        await Task.Delay(100, ct); // Simulate ping operation
    }

    private async Task ExecuteStatusCommand(CommandEnvelope command, CancellationToken ct)
    {
        // For security, this is a controlled status check operation
        _logger.LogInformation("Executing status command for device {DeviceId}", command.DeviceId);
        await Task.Delay(50, ct); // Simulate status check
    }
}