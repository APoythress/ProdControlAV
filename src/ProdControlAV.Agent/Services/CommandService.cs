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
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using ProdControlAV.Infrastructure.Services;

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

public class CommandPayload
{
    public Guid CommandId { get; set; }
    public string DeviceIp { get; set; }
    public Guid DeviceId { get; set; }
    public int? DevicePort { get; set; } = 80;
    public string DeviceType { get; set; }
    public bool? MonitorRecordingStatus { get; set; }
    public string StatusEndpoint { get; set; }
    public string? CommandType { get; set; }
    public string? AtemFunction { get; set; }
    public long? AtemInputId { get; set; }
    public int? AtemTransitionRate { get; set; }
    public int AttemptCount { get; set; }
}

public interface ICommandService
{
    Task<List<CommandPayload>> PollCommandsAsync(CancellationToken ct);
    Task ExecuteCommandAsync(CommandPayload command, CancellationToken ct);
}

public class CommandService : ICommandService
{
    private readonly HttpClient _http;
    private readonly ILogger<CommandService> _logger;
    private readonly ApiOptions _api;
    private readonly IJwtAuthService _jwtAuth;
    private readonly AtemConnectionManager _atemManager;

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
    
    // JSON parsing helper methods
    static string? GetStringOrNull(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var p) || p.ValueKind == JsonValueKind.Null)
            return null;

        return p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString();
    }

    static bool? GetBoolOrNull(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var p) || p.ValueKind == JsonValueKind.Null)
            return null;

        return p.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    static int? GetIntOrNull(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var p) || p.ValueKind == JsonValueKind.Null)
            return null;

        return p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v) ? v : null;
    }

    static string RequireString(JsonElement root, string name)
    {
        var v = GetStringOrNull(root, name);
        if (string.IsNullOrWhiteSpace(v))
            throw new JsonException($"Missing required property '{name}'.");
        return v;
    }

    public CommandService(
        HttpClient http, 
        ILogger<CommandService> logger, 
        IOptions<ApiOptions> api, 
        IJwtAuthService jwtAuth,
        AtemConnectionManager atemManager) 
    {
        _http = http;
        _logger = logger;
        _api = api.Value;
        _jwtAuth = jwtAuth;
        _atemManager = atemManager;
        _http.BaseAddress = new Uri(_api.BaseUrl);
    }

    public async Task<List<CommandPayload>> PollCommandsAsync(CancellationToken ct)
    {
        try
        {
            // Get valid JWT token
            var token = await _jwtAuth.GetValidTokenAsync(ct);
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("Failed to obtain valid JWT token for command polling");
                return new List<CommandPayload>();
            }

            // Use the new Table Storage-based polling endpoint
            var endpoint = "/api/agents/commands/poll";
            
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to poll commands: {StatusCode}", res.StatusCode);
                return new List<CommandPayload>();
            }

            // AgentsController.PollCommandQueue()
            var responseJson = await res.Content.ReadFromJsonAsync<JsonElement>(s_jsonOptions, ct);
            
            // Check if command is null (no messages available)
            if (!responseJson.TryGetProperty("command", out var commandProp))
                return new List<CommandPayload>();

            if (!commandProp.TryGetProperty("payload", out var payloadProp) ||
                payloadProp.ValueKind != JsonValueKind.String)
                return new List<CommandPayload>();

            var payloadJson = payloadProp.GetString();
            if (string.IsNullOrWhiteSpace(payloadJson))
                return new List<CommandPayload>();

            using var payloadDoc = JsonDocument.Parse(payloadJson);
            var root = payloadDoc.RootElement;

            // required
            var deviceIp = RequireString(root, "deviceIp");

            // optional
            var deviceType = GetStringOrNull(root, "deviceType");
            var commandType = GetStringOrNull(root, "commandType");
            var statusEndpoint = GetStringOrNull(root, "statusEndpoint");
            var monitorRecordingStatus = GetBoolOrNull(root, "monitorRecordingStatus") ?? false;

            var atemFunction = GetStringOrNull(root, "atemFunction");
            var atemInputId = GetIntOrNull(root, "atemInputId");
            var atemTransitionRate = GetIntOrNull(root, "atemTransitionRate");
            var attemptCount = GetIntOrNull(root, "attemptCount"); // may not exist

            
            // var atemFunction = payloadProp.TryGetProperty("atemFunction", out var functionProp) ? functionProp.GetString() : null;
            // var payload = JsonSerializer.Deserialize<CommandPayload>(payloadJson);

            // Parse the command from the response
            var command = new CommandPayload
            {
                CommandId = Guid.Parse(commandProp.GetProperty("commandId").GetString()!),
                DeviceId = Guid.Parse(commandProp.GetProperty("deviceId").GetString()!),
                DeviceIp = deviceIp,
                DeviceType = deviceType,
                MonitorRecordingStatus = monitorRecordingStatus,
                StatusEndpoint = statusEndpoint,
                CommandType = commandType,
                AtemFunction = atemFunction,
                AtemInputId = atemInputId,
                AtemTransitionRate = atemTransitionRate,
                AttemptCount = (int)attemptCount
            };

            return new List<CommandPayload> { command };
        }
        catch (OperationCanceledException)
        {
            return new List<CommandPayload>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error polling for commands");
            return new List<CommandPayload>();
        }
    }

    public async Task ExecuteCommandAsync(CommandPayload command, CancellationToken ct)
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
        Guid deviceId = command.DeviceId;

        try
        {
            if (command.CommandType == "REST")
            {
                // // Execute REST API command
                // var result = await ExecuteRestCommandAsync(command, ct);
                // success = result.Success;
                // message = result.Message;
                // response = result.Response;
                // httpStatusCode = result.StatusCode;
            }
            else if (command.CommandType == "Telnet")
            {
                // Execute Telnet command (future implementation)
                message = "Telnet commands not yet implemented";
                _logger.LogWarning("Telnet command execution not yet implemented");
            }
            else if (command.CommandType == "UPDATE")
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
            else if (command.CommandType == "ATEM")
            {
                // Execute ATEM command
                var result = await ExecuteAtemCommandAsync(command, ct);
                success = result.Success;
                message = result.Message;
                response = result.Response;
            }
            else
            {
                message = $"Unknown command type: {command.CommandType}";
                _logger.LogWarning(message);
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
            _logger.LogError(ex, "Error executing command {CommandId}", command.CommandId);
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
    /// Executes an ATEM command from the payload using LibAtem.
    /// </summary>
    private async Task<AtemCommandResult> ExecuteAtemCommandAsync(CommandPayload command, CancellationToken ct)
    {
        try
        {
            // Extract device information
            if (command.DeviceId == Guid.Empty)
            {
                return new AtemCommandResult
                {
                    Success = false,
                    Message = "Missing required property: deviceId",
                    Response = null
                };
            }

            // Extract device connection info from payload (should be added by API)
            string? deviceIp = command.DeviceIp;
            long inputId = command.AtemInputId ?? 0;
            int devicePort;

            if (command.DevicePort == null) // default to 9910 if not provided
                devicePort = 9910;
            else
                devicePort = (int)command.DevicePort;

            if (string.IsNullOrEmpty(deviceIp))
            {
                return new AtemCommandResult
                {
                    Success = false,
                    Message = "Missing required property: deviceIp",
                    Response = null
                };
            }

            // Extract ATEM command details
            string? atemCommand = command.AtemFunction;
            
            if (string.IsNullOrEmpty(atemCommand))
            {
                return new AtemCommandResult
                {
                    Success = false,
                    Message = "Missing required property: atemCommand or atemFunction",
                    Response = null
                };
            }
            
            _logger.LogInformation("Executing ATEM command: {AtemCommand} for device {DeviceId}", atemCommand, command.DeviceId);
            
            // Parse command and parameters
            if (inputId == null)
            {
                return new AtemCommandResult
                {
                    Success = false,
                    Message = "Missing required property: AtemInputId",
                    Response = null
                };
            }
            
            switch (atemCommand?.ToUpperInvariant())
            {
                case "CUTTOPROGRAM":
                {
                    var success = await _atemManager.CutToProgramAsync(command.DeviceId, deviceIp, (int)command.DevicePort, inputId, ct);
                    
                    return new AtemCommandResult
                    {
                        Success = success,
                        Message = success 
                            ? $"Cut to Program input {inputId} executed successfully"
                            : $"Failed to execute Cut to Program input {inputId}",
                        Response = success ? $"{{\"command\":\"CutToProgram\",\"inputId\":{inputId}}}" : null
                    };
                }
                
                case "FADETOPROGRAM":
                {
                    int? transitionRate = null;
                    if (command.AtemTransitionRate == null)
                        transitionRate = 30; // default to 30fps if not provided
                    else 
                        transitionRate = command.AtemTransitionRate;
                    
                    var success = await _atemManager.AutoToProgramAsync(command.DeviceId, deviceIp, devicePort, inputId, transitionRate, ct);
                    
                    return new AtemCommandResult
                    {
                        Success = success,
                        Message = success
                            ? $"Fade to Program input {inputId} (rate: {transitionRate ?? 30}) executed successfully"
                            : $"Failed to execute Fade to Program input {inputId}",
                        Response = success ? $"{{\"command\":\"FadeToProgram\",\"inputId\":{inputId},\"transitionRate\":{transitionRate ?? 30}}}" : null
                    };
                }
                
                case "SETPREVIEW":
                {
                    // SetPreview not yet implemented in AtemConnectionManager
                    _logger.LogWarning("SetPreview ATEM function not yet implemented");
                    return new AtemCommandResult
                    {
                        Success = false,
                        Message = "SetPreview function is not yet implemented",
                        Response = null
                    };
                }
                
                case "RUNMACRO":
                {
                    // RunMacro not yet implemented in AtemConnectionManager
                    _logger.LogWarning("RunMacro ATEM function not yet implemented");
                    return new AtemCommandResult
                    {
                        Success = false,
                        Message = "RunMacro function is not yet implemented",
                        Response = null
                    };
                }
                
                case "SET_AUX_AUX1":
                case "SET_AUX_AUX2":
                case "SET_AUX_AUX3":
                case "FADE_AUX_AUX1":
                case "FADE_AUX_AUX2":
                case "FADE_AUX_AUX3":
                {
                    var auxIndex = atemCommand!.EndsWith("AUX1") ? 0 : atemCommand.EndsWith("AUX2") ? 1 : 2;
                    var success = await _atemManager.SetAuxOutputAsync(command.DeviceId, deviceIp, devicePort, auxIndex, inputId, ct);
                    
                    return new AtemCommandResult
                    {
                        Success = success,
                        Message = success
                            ? $"Set Aux{auxIndex + 1} to input {inputId} executed successfully"
                            : $"Failed to set Aux{auxIndex + 1} to input {inputId}",
                        Response = success ? $"{{\"command\":\"SetAux{auxIndex + 1}\",\"inputId\":{inputId}}}" : null
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