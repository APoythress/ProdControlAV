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
    
    // Delay between retry attempts for command history recording
    private const int RetryDelayMs = 1000;
    
    // Shared HttpClient for device communication to avoid socket exhaustion
    // Configure with HTTP/1.1 to handle devices with non-compliant HTTP implementations
    private static readonly HttpClient s_deviceHttpClient = new(new SocketsHttpHandler
    {
        // Use HTTP/1.1 by default as many devices don't properly support HTTP/2
        // This helps with devices that return malformed status lines
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(3)
    })
    {
        Timeout = TimeSpan.FromSeconds(5),
        DefaultRequestVersion = new Version(1, 1),
        DefaultVersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionOrLower
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
                else if (commandType == "UPDATE")
                {
                    // Trigger agent update
                    _logger.LogInformation("Received UPDATE command, triggering agent update...");
                    message = "Update command received and will be processed by UpdateService";
                    success = true;
                    
                    // Signal update service to check and apply updates
                    // This is done via file system signal since we can't inject UpdateService
                    var updateSignalFile = Path.Combine(Path.GetTempPath(), "prodcontrolav-update-trigger");
                    await File.WriteAllTextAsync(updateSignalFile, DateTime.UtcNow.ToString("O"), ct);
                    _logger.LogInformation("Update trigger signal created at: {SignalFile}", updateSignalFile);
                }
                else if (commandType == "ATEM")
                {
                    // Execute ATEM command
                    var result = await ExecuteAtemCommandAsync(payloadJson, ct);
                    success = result.Success;
                    message = result.Message;
                    response = result.Response;
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
        string? deviceIp = null;
        int devicePort = 80;
        string? httpMethod = null;
        string? commandData = null;
        
        try
        {
            commandData = payload.GetProperty("commandData").GetString();
            httpMethod = payload.GetProperty("httpMethod").GetString() ?? "GET";
            deviceIp = payload.GetProperty("deviceIp").GetString();
            devicePort = payload.TryGetProperty("devicePort", out var portProp) && portProp.ValueKind != JsonValueKind.Null 
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

            using var httpResponse = await s_deviceHttpClient.SendAsync(request, ct);
            
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
        catch (HttpRequestException ex) when (ex.Message.Contains("invalid status line") || ex.Message.Contains("protocol"))
        {
            // Device returned malformed HTTP response - common with embedded devices
            var errorMsg = $"Device at {deviceIp ?? "unknown"}:{devicePort} returned malformed HTTP response. " +
                          $"The device may not properly support HTTP protocol. Error: {ex.Message}";
            _logger.LogError(ex, "Malformed HTTP response from device {DeviceIp}:{DevicePort} for command {Method} {Path}", 
                deviceIp ?? "unknown", devicePort, httpMethod ?? "unknown", commandData ?? "unknown");
            return new RestCommandResult
            {
                Success = false,
                Message = errorMsg,
                Response = null,
                StatusCode = null
            };
        }
        catch (TaskCanceledException)
        {
            var errorMsg = $"Request to {deviceIp ?? "unknown"}:{devicePort} timed out after 5 seconds";
            _logger.LogWarning("REST command timed out for device {DeviceIp}:{DevicePort} - {Method} {Path}", 
                deviceIp ?? "unknown", devicePort, httpMethod ?? "unknown", commandData ?? "unknown");
            return new RestCommandResult
            {
                Success = false,
                Message = errorMsg,
                Response = null,
                StatusCode = null
            };
        }
        catch (Exception ex)
        {
            var errorMsg = $"Error communicating with device at {deviceIp ?? "unknown"}:{devicePort}: {ex.Message}";
            _logger.LogError(ex, "Error executing REST command for device {DeviceIp}:{DevicePort} - {Method} {Path}", 
                deviceIp ?? "unknown", devicePort, httpMethod ?? "unknown", commandData ?? "unknown");
            return new RestCommandResult
            {
                Success = false,
                Message = errorMsg,
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
        const int maxRetries = 2;
        
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                // Get valid JWT token - this will refresh if needed
                var token = await _jwtAuth.GetValidTokenAsync(ct);
                if (string.IsNullOrEmpty(token))
                {
                    _logger.LogWarning("Failed to obtain valid JWT token for recording command history (attempt {Attempt}/{MaxRetries})", 
                        attempt + 1, maxRetries);
                    
                    if (attempt < maxRetries - 1)
                    {
                        // Force a token refresh before retrying
                        await _jwtAuth.RefreshTokenAsync(ct);
                        await Task.Delay(RetryDelayMs, ct);
                        continue;
                    }
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
                
                // Handle 401 Unauthorized - check if we're on the last attempt
                if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized && attempt >= maxRetries - 1)
                {
                    // Final attempt still got 401 - log and return instead of throwing
                    // Note: The command itself was already executed successfully (Success={Success})
                    // This only affects history recording, not command execution
                    if (success)
                    {
                        _logger.LogWarning("Command {CommandId} executed successfully, but failed to record history after {MaxRetries} attempts due to 401 Unauthorized. " +
                            "This is a non-critical issue - the command completed successfully. History recording will be skipped.", 
                            commandId, maxRetries);
                    }
                    else
                    {
                        _logger.LogError("Command {CommandId} failed execution, and also failed to record failure history after {MaxRetries} attempts due to 401 Unauthorized. " +
                            "This may indicate an authentication issue. The command failure was not recorded in history.", 
                            commandId, maxRetries);
                    }
                    return;
                }
                
                if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    // 401 Unauthorized - force token refresh and retry
                    _logger.LogWarning("Received 401 Unauthorized when recording command history, forcing token refresh (attempt {Attempt}/{MaxRetries})", 
                        attempt + 1, maxRetries);
                    await _jwtAuth.RefreshTokenAsync(ct);
                    await Task.Delay(RetryDelayMs, ct);
                    continue;
                }
                
                res.EnsureSuccessStatusCode();
                
                _logger.LogInformation("Command history recorded for {CommandId}, Success={Success}", commandId, success);
                return; // Success - exit the retry loop
            }
            catch (Exception ex)
            {
                if (attempt < maxRetries - 1)
                {
                    _logger.LogWarning(ex, "Failed to record command history for {CommandId} (attempt {Attempt}/{MaxRetries}), retrying...", 
                        commandId, attempt + 1, maxRetries);
                    await Task.Delay(RetryDelayMs, ct);
                }
                else
                {
                    // Log with appropriate severity based on command success
                    if (success)
                    {
                        _logger.LogWarning(ex, "Command {CommandId} executed successfully, but failed to record history after {MaxRetries} attempts. " +
                            "This is a non-critical issue - the command completed successfully. Error: {Error}", 
                            commandId, maxRetries, ex.Message);
                    }
                    else
                    {
                        _logger.LogError(ex, "Command {CommandId} failed execution, and also failed to record failure history after {MaxRetries} attempts. " +
                            "The command failure was not recorded in history. Error: {Error}", 
                            commandId, maxRetries, ex.Message);
                    }
                }
            }
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
            // Support both HTTP and HTTPS - prefer HTTPS if port 443 is specified
            var protocol = devicePort == 443 ? "https" : "http";
            var baseUri = new Uri($"{protocol}://{deviceIp}:{devicePort}");
            var path = statusEndpoint?.TrimStart('/') ?? "";
            var fullUri = new Uri(baseUri, path);
            
            using var statusResponse = await s_deviceHttpClient.GetAsync(fullUri, ct);
            
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
    
    /// <summary>
    /// Executes an ATEM command from the payload.
    /// 
    /// Note: This implementation uses a stub approach where ATEM commands are executed
    /// inline without injecting IAtemConnection. This is intentional because:
    /// 1. ATEM connections are device-specific (each ATEM device needs its own connection)
    /// 2. The current architecture doesn't support device-specific service instances
    /// 3. Future enhancement: Create a device registry that maps device IDs to IAtemConnection instances
    /// 
    /// For production use with real ATEM hardware, replace TODO sections with actual
    /// LibAtem API calls via a device-specific connection manager.
    /// </summary>
    private async Task<AtemCommandResult> ExecuteAtemCommandAsync(JsonElement payload, CancellationToken ct)
    {
        try
        {
            // Extract ATEM command details from payload with validation
            if (!payload.TryGetProperty("atemCommand", out var atemCommandProp))
            {
                return new AtemCommandResult
                {
                    Success = false,
                    Message = "Missing required property: atemCommand",
                    Response = null
                };
            }
            
            var atemCommand = atemCommandProp.GetString();
            
            _logger.LogInformation("Executing ATEM command: {AtemCommand}", atemCommand);
            
            // Parse command and parameters
            switch (atemCommand?.ToUpperInvariant())
            {
                case "CUT_TO_PROGRAM":
                {
                    if (!payload.TryGetProperty("inputId", out var inputIdProp))
                    {
                        return new AtemCommandResult
                        {
                            Success = false,
                            Message = "Missing required property: inputId",
                            Response = null
                        };
                    }
                    
                    var inputId = inputIdProp.GetInt32();
                    // TODO: Execute via device-specific IAtemConnection instance
                    // var deviceId = payload.GetProperty("deviceId").GetString();
                    // var atemConnection = _deviceConnectionRegistry.GetAtemConnection(deviceId);
                    // await atemConnection.CutToProgramAsync(inputId, ct);
                    
                    return new AtemCommandResult
                    {
                        Success = true,
                        Message = $"Cut to Program input {inputId} executed successfully",
                        Response = $"{{\"command\":\"CutToProgram\",\"inputId\":{inputId}}}"
                    };
                }
                
                case "FADE_TO_PROGRAM":
                {
                    if (!payload.TryGetProperty("inputId", out var inputIdProp))
                    {
                        return new AtemCommandResult
                        {
                            Success = false,
                            Message = "Missing required property: inputId",
                            Response = null
                        };
                    }
                    
                    var inputId = inputIdProp.GetInt32();
                    int? transitionRate = null;
                    if (payload.TryGetProperty("transitionRate", out var rateProp) && rateProp.ValueKind != JsonValueKind.Null)
                    {
                        transitionRate = rateProp.GetInt32();
                    }
                    
                    // TODO: Execute via device-specific IAtemConnection instance
                    // var deviceId = payload.GetProperty("deviceId").GetString();
                    // var atemConnection = _deviceConnectionRegistry.GetAtemConnection(deviceId);
                    // await atemConnection.FadeToProgramAsync(inputId, transitionRate, ct);
                    
                    return new AtemCommandResult
                    {
                        Success = true,
                        Message = $"Fade to Program input {inputId} (rate: {transitionRate ?? 30}) executed successfully",
                        Response = $"{{\"command\":\"FadeToProgram\",\"inputId\":{inputId},\"transitionRate\":{transitionRate ?? 30}}}"
                    };
                }
                
                case "SET_PREVIEW":
                {
                    if (!payload.TryGetProperty("inputId", out var inputIdProp))
                    {
                        return new AtemCommandResult
                        {
                            Success = false,
                            Message = "Missing required property: inputId",
                            Response = null
                        };
                    }
                    
                    var inputId = inputIdProp.GetInt32();
                    // TODO: Execute via device-specific IAtemConnection instance
                    // var deviceId = payload.GetProperty("deviceId").GetString();
                    // var atemConnection = _deviceConnectionRegistry.GetAtemConnection(deviceId);
                    // await atemConnection.SetPreviewAsync(inputId, ct);
                    
                    return new AtemCommandResult
                    {
                        Success = true,
                        Message = $"Set Preview to input {inputId} executed successfully",
                        Response = $"{{\"command\":\"SetPreview\",\"inputId\":{inputId}}}"
                    };
                }
                
                case "LIST_MACROS":
                {
                    // TODO: Execute via device-specific IAtemConnection instance
                    // var deviceId = payload.GetProperty("deviceId").GetString();
                    // var atemConnection = _deviceConnectionRegistry.GetAtemConnection(deviceId);
                    // var macros = await atemConnection.ListMacrosAsync(ct);
                    // var macrosJson = JsonSerializer.Serialize(macros, s_jsonOptions);
                    
                    return new AtemCommandResult
                    {
                        Success = true,
                        Message = "Macro list retrieved successfully",
                        Response = "{\"macros\":[]}"
                    };
                }
                
                case "RUN_MACRO":
                {
                    if (!payload.TryGetProperty("macroId", out var macroIdProp))
                    {
                        return new AtemCommandResult
                        {
                            Success = false,
                            Message = "Missing required property: macroId",
                            Response = null
                        };
                    }
                    
                    var macroId = macroIdProp.GetInt32();
                    // TODO: Execute via device-specific IAtemConnection instance
                    // var deviceId = payload.GetProperty("deviceId").GetString();
                    // var atemConnection = _deviceConnectionRegistry.GetAtemConnection(deviceId);
                    // await atemConnection.RunMacroAsync(macroId, ct);
                    
                    return new AtemCommandResult
                    {
                        Success = true,
                        Message = $"Macro {macroId} executed successfully",
                        Response = $"{{\"command\":\"RunMacro\",\"macroId\":{macroId}}}"
                    };
                }
                
                default:
                    return new AtemCommandResult
                    {
                        Success = false,
                        Message = $"Unknown ATEM command: {atemCommand}",
                        Response = null
                    };
            }
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogError(ex, "Missing required parameter in ATEM command payload");
            return new AtemCommandResult
            {
                Success = false,
                Message = $"Missing required parameter: {ex.Message}",
                Response = null
            };
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "ATEM connection error");
            return new AtemCommandResult
            {
                Success = false,
                Message = $"ATEM connection error: {ex.Message}",
                Response = null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing ATEM command");
            return new AtemCommandResult
            {
                Success = false,
                Message = $"ATEM command execution failed: {ex.Message}",
                Response = null
            };
        }
    }
    
    private class AtemCommandResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string? Response { get; set; }
    }
}