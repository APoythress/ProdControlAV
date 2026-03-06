using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Options;
using ProdControlAV.Agent.Interfaces;

namespace ProdControlAV.Agent.Services;
public class CommandPayload
{
    public Guid CommandId { get; set; }
    public string DeviceIp { get; set; }
    public Guid DeviceId { get; set; }
    public int? DevicePort { get; set; } = 80;
    public string DeviceType { get; set; }
    public bool? MonitorRecordingStatus { get; set; } = false;
    public string StatusEndpoint { get; set; }
    public string? CommandType { get; set; } = null;
    public string? AtemFunction { get; set; } = null;
    public long? AtemInputId { get; set; } = null;
    public int? AtemTransitionRate { get; set; } = null;
    public int AttemptCount { get; set; }
    /// <summary>HyperDeck text command to send (e.g. "play", "stop", "record").</summary>
    public string? HyperDeckCommand { get; set; } = null;
}

public interface ICommandService
{
    Task<CommandPayload> PollCommandsAsync(CancellationToken ct);
    Task ExecuteCommandAsync(CommandPayload command, CancellationToken ct);
}

public class CommandService : ICommandService
{
    private readonly HttpClient _http;
    private readonly ILogger<CommandService> _logger;
    private readonly ApiOptions _api;
    private readonly IJwtAuthService _jwtAuth;
    private readonly AtemUdpConnectionManager _atemUdpManager;
    private readonly HyperDeckConnectionPool _hyperDeckPool;

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
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
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
        AtemUdpConnectionManager atemUdpManager,
        HyperDeckConnectionPool hyperDeckPool,
        AtemStateSnapshot atemStateSnapshop) 
    {
        _http = http;
        _logger = logger;
        _api = api.Value;
        _jwtAuth = jwtAuth;
        _atemUdpManager = atemUdpManager;
        _hyperDeckPool = hyperDeckPool;
        _http.BaseAddress = new Uri(_api.BaseUrl);
    }

public async Task<CommandPayload> PollCommandsAsync(CancellationToken ct)
    {
        try
        {
            var token = await _jwtAuth.GetValidTokenAsync(ct);
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("Failed to obtain valid JWT token for command polling");
                return new CommandPayload();
            }
    
            var endpoint = "/api/agents/commands/poll";
    
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    
            using var res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to poll commands: {StatusCode}", res.StatusCode);
                return new CommandPayload();
            }
    
            if (res.StatusCode == HttpStatusCode.NoContent)
                return new CommandPayload();
    
            var body = await res.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body))
                return new CommandPayload();
    
            using var responseDoc = JsonDocument.Parse(body);
            var responseJson = responseDoc.RootElement;
    
            if (responseJson.ValueKind != JsonValueKind.Object)
                return new CommandPayload();
    
            if (!responseJson.TryGetProperty("command", out var commandProp))
                return new CommandPayload();
    
            if (commandProp.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return new CommandPayload();
    
            if (commandProp.ValueKind != JsonValueKind.Object)
                return new CommandPayload();
    
            if (!commandProp.TryGetProperty("payload", out var payloadProp) || payloadProp.ValueKind != JsonValueKind.String)
                return new CommandPayload();
    
            var payloadJson = payloadProp.GetString();
            if (string.IsNullOrWhiteSpace(payloadJson))
                return new CommandPayload();
    
            using var payloadDoc = JsonDocument.Parse(payloadJson!);
            var root = payloadDoc.RootElement;
    
            var deviceIp = RequireString(root, "deviceIp");
    
            var deviceType = GetStringOrNull(root, "deviceType");
            var commandType = GetStringOrNull(root, "commandType");
            var statusEndpoint = GetStringOrNull(root, "statusEndpoint");
            var monitorRecordingStatus = GetBoolOrNull(root, "monitorRecordingStatus") ?? false;
    
            var atemFunction = GetStringOrNull(root, "atemFunction");
            var atemInputId = GetIntOrNull(root, "atemInputId");
            var atemTransitionRate = GetIntOrNull(root, "atemTransitionRate");
            var attemptCount = GetIntOrNull(root, "attemptCount");
            // TODO - this should be more dynamic and less hardcoded, but for now it's fine. Should always be commandData to be reusable and not just for hyperDeck
            var hyperDeckCommand = GetStringOrNull(root, "commandData");
            var devicePort = GetIntOrNull(root, "devicePort");
    
            if (!commandProp.TryGetProperty("commandId", out var cmdIdProp) || cmdIdProp.ValueKind != JsonValueKind.String)
                return new CommandPayload();
    
            var cmdIdStr = cmdIdProp.GetString();
            if (string.IsNullOrWhiteSpace(cmdIdStr) || !Guid.TryParse(cmdIdStr, out var cmdId))
                return new CommandPayload();
    
            if (!commandProp.TryGetProperty("deviceId", out var devIdProp) || devIdProp.ValueKind != JsonValueKind.String)
                return new CommandPayload();
    
            var devIdStr = devIdProp.GetString();
            if (string.IsNullOrWhiteSpace(devIdStr) || !Guid.TryParse(devIdStr, out var devId))
                return new CommandPayload();
    
            return new CommandPayload
            {
                CommandId = cmdId,
                DeviceId = devId,
                DeviceIp = deviceIp,
                DevicePort = devicePort,
                DeviceType = deviceType,
                MonitorRecordingStatus = monitorRecordingStatus,
                StatusEndpoint = statusEndpoint,
                CommandType = commandType,
                AtemFunction = atemFunction,
                AtemInputId = atemInputId,
                AtemTransitionRate = atemTransitionRate,
                AttemptCount = attemptCount.GetValueOrDefault(0),
                HyperDeckCommand = hyperDeckCommand
            };
        }
        catch (OperationCanceledException)
        {
            return new CommandPayload();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error polling for commands");
            return new CommandPayload();
        }
    }
    
    
// csharp
    // File: 'src/ProdControlAV.Agent/Services/CommandService.cs'
    public async Task ExecuteCommandAsync(CommandPayload command, CancellationToken ct)
    {
        // Skip if no valid command was polled
        if (command.CommandId == Guid.Empty)
        {
            _logger.LogDebug("No command to execute; skipping history recording.");
            return;
        }
    
        var startTime = DateTime.UtcNow;
        bool success = false;
        string message = "";
        string? response = null;
        int? httpStatusCode = null;
    
        // Use values from the payload
        bool monitorRecordingStatus = command.MonitorRecordingStatus ?? false;
        string? statusEndpoint = command.StatusEndpoint;
        string? deviceIp = command.DeviceIp;
        int devicePort = command.DevicePort ?? 80;
        string? deviceType = command.DeviceType;
        Guid deviceId = command.DeviceId;
    
        try
        {
            if (string.Equals(command.CommandType, "REST", StringComparison.OrdinalIgnoreCase))
            {
                // Implement REST execution when available
                message = "REST command execution not implemented";
                _logger.LogWarning(message);
            }
            else if (string.Equals(command.CommandType, "Telnet", StringComparison.OrdinalIgnoreCase))
            {
                message = "Telnet commands not yet implemented";
                _logger.LogWarning(message);
            }
            else if (string.Equals(command.CommandType, "UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Received UPDATE command, triggering agent update...");
                message = "Update command received and will be processed by UpdateService";
                success = true;
    
                var updateSignalFile = Path.Combine(Path.GetTempPath(), "prodcontrolav-update-trigger");
                await File.WriteAllTextAsync(updateSignalFile, DateTime.UtcNow.ToString("O"), ct);
                _logger.LogInformation("Update trigger signal created at: {SignalFile}", updateSignalFile);
            }
            else if (string.Equals(command.CommandType, "ATEM", StringComparison.OrdinalIgnoreCase))
            {
                var result = await ExecuteAtemCommandAsync(command, ct);
                success = result.Success;
                message = result.Message;
                response = result.Response;
            }
            else if (string.Equals(command.CommandType, "HYPERDECK", StringComparison.OrdinalIgnoreCase))
            {
                var result = await ExecuteHyperDeckCommandAsync(command, ct);
                success = result.Success;
                message = result.Message;
                response = result.Response;
            }
            else
            {
                message = $"Unknown command type: {command.CommandType}";
                _logger.LogWarning(message);
            }
    
            if (success && monitorRecordingStatus && !string.IsNullOrEmpty(statusEndpoint)
                && !string.IsNullOrEmpty(deviceIp) && string.Equals(deviceType, "Video", StringComparison.OrdinalIgnoreCase))
            {
                await CheckAndUpdateRecordingStatusAsync(deviceId, deviceIp!, devicePort, statusEndpoint!, ct);
            }
        }
        catch (Exception ex)
        {
            message = $"Command execution failed: {ex.Message}";
            _logger.LogError(ex, "Error executing command {CommandId}", command.CommandId);
        }
    
        var durationMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
    
        await RecordCommandHistoryAsync(command.CommandId, deviceId, success, message, response, httpStatusCode, durationMs, ct);
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
        // Skip recording if commandId is empty
        if (commandId == Guid.Empty)
        {
            _logger.LogDebug("Skipping command history recording for empty commandId.");
            return;
        }
    
        const int maxRetries = 2;
    
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var token = await _jwtAuth.GetValidTokenAsync(ct);
                if (string.IsNullOrEmpty(token))
                {
                    _logger.LogWarning("Failed to obtain valid JWT token for recording command history (attempt {Attempt}/{MaxRetries})",
                        attempt + 1, maxRetries);
    
                    if (attempt < maxRetries - 1)
                    {
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
                    commandName = commandId != Guid.Empty ? "REST Command" : null,
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
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    
                using var res = await _http.SendAsync(req, ct);
    
                if (res.StatusCode == HttpStatusCode.Unauthorized && attempt >= maxRetries - 1)
                {
                    if (success)
                    {
                        _logger.LogWarning("Command {CommandId} executed successfully, but failed to record history after {MaxRetries} attempts due to 401 Unauthorized.",
                            commandId, maxRetries);
                    }
                    else
                    {
                        _logger.LogError("Command {CommandId} failed execution, and also failed to record failure history after {MaxRetries} attempts due to 401 Unauthorized.",
                            commandId, maxRetries);
                    }
                    return;
                }
    
                if (res.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _logger.LogWarning("Received 401 Unauthorized when recording command history, forcing token refresh (attempt {Attempt}/{MaxRetries})",
                        attempt + 1, maxRetries);
                    await _jwtAuth.RefreshTokenAsync(ct);
                    await Task.Delay(RetryDelayMs, ct);
                    continue;
                }
    
                res.EnsureSuccessStatusCode();
    
                _logger.LogInformation("Command history recorded for {CommandId}, Success={Success}", commandId, success);
                return;
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
                    if (success)
                    {
                        _logger.LogWarning(ex, "Command {CommandId} executed successfully, but failed to record history after {MaxRetries} attempts. Error: {Error}",
                            commandId, maxRetries, ex.Message);
                    }
                    else
                    {
                        _logger.LogError(ex, "Command {CommandId} failed execution, and also failed to record failure history after {MaxRetries} attempts. Error: {Error}",
                            commandId, maxRetries, ex.Message);
                    }
                }
            }
        }
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
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            
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
    /// Executes an ATEM command from the payload using Atem UDP Protocol.
    /// </summary>
    private async Task<CommandResult> ExecuteAtemCommandAsync(CommandPayload command, CancellationToken ct)
    {
        var deviceIp = command.DeviceIp;
        var devicePort = command.DevicePort > 0 ? (int)command.DevicePort : AtemUdpConnection.DefaultAtemPort;

        if (string.IsNullOrWhiteSpace(deviceIp))
        {
            return new CommandResult
            {
                Success = false,
                Message = "ATEM command missing deviceIp.",
                Response = null
            };
        }

        var atemFunction = (command.AtemFunction ?? "").Trim();
        if (string.IsNullOrWhiteSpace(atemFunction))
        {
            return new CommandResult
            {
                Success = false,
                Message = "ATEM command missing AtemFunction/AtemCommand.",
                Response = null
            };
        }

        // Acquire UDP connection (handshake + loops)
        var conn = await _atemUdpManager.GetOrCreateAsync(command.DeviceId, deviceIp, devicePort, ct);

        try
        {
            switch (atemFunction.ToUpperInvariant())
            {
                case "CUTTOPROGRAM":
                {
                    var inputId = (int)(command.AtemInputId ?? 0);
                    await conn.CutToProgramAsync(inputId, ct);

                    return new CommandResult
                    {
                        Success = true,
                        Message = $"Cut to Program input {inputId} executed successfully",
                        Response = $"{{\"command\":\"CutToProgram\",\"inputId\":{inputId}}}"
                    };
                }

                case "FADETOPROGRAM":
                {
                    var inputId = (int)(command.AtemInputId ?? 0);
                    var rate = command.AtemTransitionRate ?? 30;

                    // If your AtemUdpConnection exposes a FadeToProgramAsync(rate), call it.
                    // If it only has AutoToProgramAsync, call that. (Match your actual methods.)
                    await conn.FadeToProgramAsync(inputId, rate, ct);

                    return new CommandResult
                    {
                        Success = true,
                        Message = $"Fade to Program input {inputId} (rate: {rate}) executed successfully",
                        Response = $"{{\"command\":\"FadeToProgram\",\"inputId\":{inputId},\"transitionRate\":{rate}}}"
                    };
                }

                case "SETPREVIEW":
                {
                    var inputId = (int)(command.AtemInputId ?? 0);
                    await conn.SetPreviewAsync(inputId, ct);

                    return new CommandResult
                    {
                        Success = true,
                        Message = $"Preview set to input {inputId} successfully",
                        Response = $"{{\"command\":\"SetPreview\",\"inputId\":{inputId}}}"
                    };
                }

                case "SETUX":
                case "SETAUX":
                case "GETAUXSOURCE":
                {
                    // Example: Aux channel stored 0-based per your docs
                    var channel = command.AtemInputId ?? 0;
                    var inputId = (int)(command.AtemInputId ?? 0);

                    await conn.SetAuxAsync((int)channel, inputId, ct);

                    return new CommandResult
                    {
                        Success = true,
                        Message = $"Aux {channel} set to input {inputId} successfully",
                        Response = $"{{\"command\":\"SetAux\",\"channel\":{channel},\"inputId\":{inputId}}}"
                    };
                }

                case "RUNMACRO":
                {
                    var macroId = (int)(command.AtemInputId ?? 0);
                    await conn.RunMacroAsync(macroId, ct);

                    return new CommandResult
                    {
                        Success = true,
                        Message = $"Macro {macroId} executed successfully",
                        Response = $"{{\"command\":\"RunMacro\",\"macroId\":{macroId}}}"
                    };
                }

                // If you want read-only state: prefer conn.CurrentState (if you maintain snapshot),
                // or keep _atemSnapshot if it's truly updated from conn.StateChanged events.
                case "GETPROGRAMINPUT":
                {
                    var program = conn.CurrentState?.ProgramInputId ?? -1;
                    return new CommandResult
                    {
                        Success = program >= 0,
                        Message = $"ATEM Program input = {program}",
                        Response = program.ToString()
                    };
                }

                default:
                    return new CommandResult
                    {
                        Success = false,
                        Message = $"Unknown ATEM function '{atemFunction}'.",
                        Response = null
                    };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ATEM UDP command failed for device {DeviceId} ({Ip}:{Port}) function={Func}",
                command.DeviceId, deviceIp, devicePort, atemFunction);

            return new CommandResult
            {
                Success = false,
                Message = $"ATEM UDP command failed: {ex.Message}",
                Response = null
            };
        }
    }
    
    /// <summary>
    /// Executes a HyperDeck command over a persistent TCP connection.
    /// </summary>
    private async Task<CommandResult> ExecuteHyperDeckCommandAsync(CommandPayload command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.DeviceIp))
        {
            return new CommandResult
            {
                Success = false,
                Message = "Missing required property: deviceIp",
                Response = null
            };
        }

        if (string.IsNullOrWhiteSpace(command.HyperDeckCommand))
        {
            return new CommandResult
            {
                Success = false,
                Message = "Missing required property: hyperDeckCommand",
                Response = null
            };
        }

        var port = command.DevicePort ?? 9993;

        try
        {
            _logger.LogInformation(
                "Executing HyperDeck command '{Command}' on device {DeviceId} at {Ip}:{Port}",
                command.HyperDeckCommand, command.DeviceId, command.DeviceIp, port);

            var connection = await _hyperDeckPool.GetOrCreateAsync(command.DeviceIp, port, ct);
            var deviceResponse = await connection.SendCommandAsync(command.HyperDeckCommand, ct);

            var success = deviceResponse.StatusCode is >= 200 and < 300;
            var responseJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                statusCode = deviceResponse.StatusCode,
                statusText = deviceResponse.Message,
                fields = deviceResponse.Data
            }, s_jsonOptions);

            _logger.LogInformation(
                "HyperDeck command '{Command}' completed with status {StatusCode} {StatusText}",
                command.HyperDeckCommand, deviceResponse.StatusCode, deviceResponse.Message);

            return new CommandResult
            {
                Success = success,
                Message = success
                    ? $"HyperDeck command '{command.HyperDeckCommand}' succeeded: {deviceResponse.StatusCode} {deviceResponse.Message}"
                    : $"HyperDeck command '{command.HyperDeckCommand}' failed: {deviceResponse.StatusCode} {deviceResponse.Message}",
                Response = responseJson
            };
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex,
                "HyperDeck command '{Command}' timed out for device {DeviceId}",
                command.HyperDeckCommand, command.DeviceId);
            return new CommandResult
            {
                Success = false,
                Message = ex.Message,
                Response = null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error executing HyperDeck command '{Command}' for device {DeviceId}",
                command.HyperDeckCommand, command.DeviceId);
            return new CommandResult
            {
                Success = false,
                Message = $"HyperDeck command execution failed: {ex.Message}",
                Response = null
            };
        }
    }

    private class CommandResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string? Response { get; set; }
    }
}