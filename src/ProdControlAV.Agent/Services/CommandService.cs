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
            // Get valid JWT token
            var token = await _jwtAuth.GetValidTokenAsync(ct);
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("Failed to obtain valid JWT token for command polling");
                return new List<CommandEnvelope>();
            }

            // Use the new Table Storage-based polling endpoint
            var endpoint = "/api/agents/commands/poll";
            
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
        string? response = null;
        int? httpStatusCode = null;
        bool monitorRecordingStatus = false;
        string? statusEndpoint = null;
        string? deviceIp = null;
        int devicePort = 80;
        string? deviceType = null;

        try
        {
            // Parse payload to get command details
            if (!string.IsNullOrEmpty(command.Payload))
            {
                var payloadJson = JsonSerializer.Deserialize<JsonElement>(command.Payload, s_jsonOptions);
                
                var commandType = payloadJson.GetProperty("commandType").GetString();
                
                // Extract device info for potential recording status check
                deviceIp = payloadJson.TryGetProperty("deviceIp", out var ipProp) ? ipProp.GetString() : null;
                devicePort = payloadJson.TryGetProperty("devicePort", out var portProp) && portProp.ValueKind != JsonValueKind.Null 
                    ? portProp.GetInt32() : 80;
                deviceType = payloadJson.TryGetProperty("deviceType", out var typeProp) ? typeProp.GetString() : null;
                
                // Check if we should monitor recording status
                if (payloadJson.TryGetProperty("monitorRecordingStatus", out var monitorProp))
                {
                    monitorRecordingStatus = monitorProp.GetBoolean();
                }
                
                if (monitorRecordingStatus && payloadJson.TryGetProperty("statusEndpoint", out var endpointProp))
                {
                    statusEndpoint = endpointProp.GetString();
                }
                
                if (commandType == "REST")
                {
                    // Execute REST API command
                    var result = await ExecuteRestCommandAsync(payloadJson, ct);
                    success = result.Success;
                    message = result.Message;
                    response = result.Response;
                    httpStatusCode = result.StatusCode;
                }
                else if (commandType == "Telnet")
                {
                    // Execute Telnet command (future implementation)
                    message = "Telnet commands not yet implemented";
                    _logger.LogWarning("Telnet command execution not yet implemented");
                }
                else
                {
                    message = $"Unknown command type: {commandType}";
                    _logger.LogWarning(message);
                }
            }
            else
            {
                // Legacy command format - execute as before
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
            
            // If command was successful and we should monitor recording status, check it
            if (success && monitorRecordingStatus && !string.IsNullOrEmpty(statusEndpoint) 
                && !string.IsNullOrEmpty(deviceIp) && deviceType == "Video")
            {
                await CheckAndUpdateRecordingStatusAsync(command.DeviceId, deviceIp, devicePort, statusEndpoint, ct);
            }
        }
        catch (Exception ex)
        {
            message = $"Command execution failed: {ex.Message}";
            _logger.LogError(ex, "Error executing command {CommandId} - {Verb}", command.CommandId, command.Verb);
        }

        var durationMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
        
        // Record execution in CommandHistory table (Table Storage)
        await RecordCommandHistoryAsync(command.CommandId, command.DeviceId, success, message, response, httpStatusCode, durationMs, ct);
    }

    private async Task<RestCommandResult> ExecuteRestCommandAsync(JsonElement payload, CancellationToken ct)
    {
        try
        {
            var commandData = payload.GetProperty("commandData").GetString();
            var httpMethod = payload.GetProperty("httpMethod").GetString() ?? "GET";
            var deviceIp = payload.GetProperty("deviceIp").GetString();
            var devicePort = payload.TryGetProperty("devicePort", out var portProp) && portProp.ValueKind != JsonValueKind.Null 
                ? portProp.GetInt32() : 80;
            
            var requestBody = payload.TryGetProperty("requestBody", out var bodyProp) && bodyProp.ValueKind != JsonValueKind.Null
                ? bodyProp.GetString() : null;
            
            var requestHeaders = payload.TryGetProperty("requestHeaders", out var headersProp) && headersProp.ValueKind != JsonValueKind.Null
                ? headersProp.GetString() : null;

            // Build the full URL
            var baseUri = new Uri($"http://{deviceIp}:{devicePort}");
            var path = commandData?.TrimStart('/') ?? "";
            var fullUri = new Uri(baseUri, path);

            _logger.LogInformation("Executing REST command: {Method} {Uri}", httpMethod, fullUri);

            using var request = new HttpRequestMessage(new HttpMethod(httpMethod), fullUri);

            // Add custom headers if provided
            if (!string.IsNullOrEmpty(requestHeaders))
            {
                try
                {
                    var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(requestHeaders, s_jsonOptions);
                    if (headers != null)
                    {
                        foreach (var (key, value) in headers)
                        {
                            request.Headers.TryAddWithoutValidation(key, value);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse request headers, continuing without them");
                }
            }

            // Add request body if provided
            if (!string.IsNullOrEmpty(requestBody) && (httpMethod == "POST" || httpMethod == "PUT" || httpMethod == "PATCH"))
            {
                request.Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
            }

            using var deviceClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var httpResponse = await deviceClient.SendAsync(request, ct);
            
            var responseBody = await httpResponse.Content.ReadAsStringAsync(ct);
            var statusCode = (int)httpResponse.StatusCode;
            
            _logger.LogInformation("REST command completed: Status={StatusCode}, Response={Response}", 
                statusCode, responseBody?.Substring(0, Math.Min(100, responseBody?.Length ?? 0)));

            return new RestCommandResult
            {
                Success = httpResponse.IsSuccessStatusCode,
                Message = httpResponse.IsSuccessStatusCode ? "REST command executed successfully" : $"Device returned status {statusCode}",
                Response = responseBody,
                StatusCode = statusCode
            };
        }
        catch (TaskCanceledException)
        {
            return new RestCommandResult
            {
                Success = false,
                Message = "Request timed out after 5 seconds",
                Response = null,
                StatusCode = null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing REST command");
            return new RestCommandResult
            {
                Success = false,
                Message = $"Error: {ex.Message}",
                Response = null,
                StatusCode = null
            };
        }
    }

    private async Task RecordCommandHistoryAsync(
        Guid commandId, 
        Guid deviceId, 
        bool success, 
        string message, 
        string? response,
        int? httpStatusCode,
        double executionTimeMs, 
        CancellationToken ct)
    {
        try
        {
            // Get valid JWT token
            var token = await _jwtAuth.GetValidTokenAsync(ct);
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("Failed to obtain valid JWT token for recording command history");
                return;
            }

            var historyRequest = new
            {
                commandId,
                deviceId,
                commandName = "REST Command", // Will be populated from payload in future
                success,
                errorMessage = success ? null : message,
                response = response?.Length > 2000 ? response.Substring(0, 2000) : response,
                httpStatusCode,
                executionTimeMs
            };

            var endpoint = "/api/agents/commands/history";
            
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(historyRequest, options: s_jsonOptions)
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var res = await _http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            
            _logger.LogInformation("Command history recorded for {CommandId}, Success={Success}", commandId, success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record command history for {CommandId}", commandId);
        }
    }

    private class RestCommandResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string? Response { get; set; }
        public int? StatusCode { get; set; }
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
    
    private async Task CheckAndUpdateRecordingStatusAsync(Guid deviceId, string deviceIp, int devicePort, string statusEndpoint, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Checking recording status for Video device {DeviceId} at endpoint {StatusEndpoint}", 
                deviceId, statusEndpoint);
            
            // Build the full URL for the status check
            var baseUri = new Uri($"http://{deviceIp}:{devicePort}");
            var path = statusEndpoint?.TrimStart('/') ?? "";
            var fullUri = new Uri(baseUri, path);
            
            using var deviceClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var statusResponse = await deviceClient.GetAsync(fullUri, ct);
            
            if (statusResponse.IsSuccessStatusCode)
            {
                var statusJson = await statusResponse.Content.ReadAsStringAsync(ct);
                _logger.LogDebug("Recording status response: {Response}", statusJson);
                
                // Try to parse the response to determine recording status
                // Assume the response is JSON with a "recording" boolean field
                bool isRecording = false;
                try
                {
                    var json = JsonSerializer.Deserialize<JsonElement>(statusJson, s_jsonOptions);
                    if (json.TryGetProperty("recording", out var recordingProp))
                    {
                        isRecording = recordingProp.GetBoolean();
                    }
                    else if (json.TryGetProperty("isRecording", out var isRecordingProp))
                    {
                        isRecording = isRecordingProp.GetBoolean();
                    }
                }
                catch
                {
                    // If parsing fails, assume not recording
                    _logger.LogWarning("Failed to parse recording status response, assuming not recording");
                }
                
                // Update recording status via API
                await UpdateRecordingStatusAsync(deviceId, isRecording, ct);
            }
            else
            {
                _logger.LogWarning("Failed to get recording status: HTTP {StatusCode}", statusResponse.StatusCode);
            }
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Recording status check timed out for device {DeviceId}", deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking recording status for device {DeviceId}", deviceId);
        }
    }
    
    private async Task UpdateRecordingStatusAsync(Guid deviceId, bool recordingStatus, CancellationToken ct)
    {
        try
        {
            // Get valid JWT token
            var token = await _jwtAuth.GetValidTokenAsync(ct);
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("Failed to obtain valid JWT token for updating recording status");
                return;
            }
            
            var updateRequest = new
            {
                deviceId,
                recordingStatus
            };
            
            var endpoint = "/api/agents/recording-status";
            
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(updateRequest, options: s_jsonOptions)
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            
            using var res = await _http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            
            _logger.LogInformation("Recording status updated for device {DeviceId}: {RecordingStatus}", 
                deviceId, recordingStatus ? "Recording" : "Idle");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update recording status for device {DeviceId}", deviceId);
        }
    }
}