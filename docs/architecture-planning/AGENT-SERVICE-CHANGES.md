# Agent Service Changes - Architecture Planning Guide

## Overview

This document guides coding agents through changes to the ProdControlAV.Agent project, which runs on Raspberry Pi devices for monitoring and controlling A/V equipment. The agent is a .NET Worker Service that operates independently with minimal resource usage.

## Core Agent Principles

### 1. Cost Optimization
- **Queue-based command polling**: No SQL queries for command retrieval
- **Table Storage for device lists**: Fast, cheap device metadata retrieval
- **Table Storage for status uploads**: No SQL writes during monitoring
- **Idle-aware operation**: Reduce activity when system idle (future enhancement)

### 2. Reliability and Resilience
- **Automatic reconnection**: Exponential backoff for API failures
- **Graceful degradation**: Continue monitoring even if API unreachable
- **Error recovery**: Retry transient failures, log permanent failures
- **Health monitoring**: Self-monitoring and reporting

### 3. Resource Efficiency
- **Low memory footprint**: Minimize allocations, reuse objects
- **Efficient networking**: HTTP/2, connection pooling, batch operations
- **Configurable polling**: Adjust frequency based on requirements
- **Background operation**: No user interaction, minimal CPU usage

### 4. Security
- **JWT authentication**: Time-limited tokens, automatic refresh
- **API key protection**: Never log or expose API keys
- **HTTPS only**: All communication encrypted
- **Minimal privileges**: Run as non-root user, limited network capabilities

## Agent Architecture

```
┌─────────────────────────────────────────────────────────────┐
│         ProdControlAV.Agent (Raspberry Pi)                  │
├─────────────────────────────────────────────────────────────┤
│                                                               │
│  ┌──────────────────┐  ┌──────────────────┐                │
│  │  AgentService    │  │  JwtAuthService  │                │
│  │  (Main Loop)     │  │  (Token Mgmt)    │                │
│  └────────┬─────────┘  └────────┬─────────┘                │
│           │                      │                           │
│  ┌────────▼─────────┐  ┌────────▼─────────┐                │
│  │  DeviceSource    │  │  CommandService  │                │
│  │  (Device List)   │  │  (Command Poll)  │                │
│  └────────┬─────────┘  └────────┬─────────┘                │
│           │                      │                           │
│  ┌────────▼──────────────────────▼─────────┐                │
│  │        StatusPublisher                   │                │
│  │    (Status Upload to API)                │                │
│  └────────┬─────────────────────────────────┘                │
│           │                                                   │
│  ┌────────▼─────────┐  ┌──────────────────┐                │
│  │  NetworkMonitor  │  │  AtemConnection  │                │
│  │  (Ping/TCP)      │  │  (ATEM Control)  │                │
│  └──────────────────┘  └──────────────────┘                │
│                                                               │
└─────────────────────────────────────────────────────────────┘
                           │
                           ▼
              ┌────────────────────────┐
              │  A/V Equipment         │
              │  (Devices to Monitor)  │
              └────────────────────────┘
```

## Project Structure

```
src/ProdControlAV.Agent/
├── Services/                # Core services
│   ├── AgentService.cs      # Main agent loop
│   ├── JwtAuthService.cs    # Authentication
│   ├── DeviceSource.cs      # Device list management
│   ├── StatusPublisher.cs   # Status upload
│   ├── CommandService.cs    # Command polling/execution
│   └── AtemConnectionService.cs  # ATEM video switcher control
├── Interfaces/              # Service interfaces
├── Models/                  # DTOs and data models
├── Configs/                 # Configuration models
├── Program.cs               # Service host setup
└── appsettings.json         # Configuration
```

## Adding a New Service

### Step-by-Step Process

1. **Define Service Interface** in `Interfaces/`
   ```csharp
   public interface IYourService
   {
       Task InitializeAsync(CancellationToken ct);
       Task ExecuteAsync(CancellationToken ct);
       Task CleanupAsync();
   }
   ```

2. **Implement Service** in `Services/`
   ```csharp
   public class YourService : IYourService
   {
       private readonly HttpClient _httpClient;
       private readonly ILogger<YourService> _logger;
       private readonly IOptions<YourServiceOptions> _options;
       
       public YourService(
           HttpClient httpClient,
           ILogger<YourService> logger,
           IOptions<YourServiceOptions> options)
       {
           _httpClient = httpClient;
           _logger = logger;
           _options = options;
       }
       
       public async Task InitializeAsync(CancellationToken ct)
       {
           _logger.LogInformation("Initializing YourService");
           
           // Perform initialization
           // Test connectivity, validate config, etc.
           
           _logger.LogInformation("YourService initialized successfully");
       }
       
       public async Task ExecuteAsync(CancellationToken ct)
       {
           try
           {
               _logger.LogDebug("Executing YourService operation");
               
               // Perform service operation
               // Use JWT authentication for API calls
               
               _logger.LogDebug("YourService operation completed");
           }
           catch (Exception ex)
           {
               _logger.LogError(ex, "Error in YourService execution");
               throw; // Let caller handle retry logic
           }
       }
       
       public async Task CleanupAsync()
       {
           _logger.LogInformation("Cleaning up YourService");
           // Release resources, close connections
       }
   }
   ```

3. **Add Configuration** in `Configs/`
   ```csharp
   public class YourServiceOptions
   {
       public const string SectionName = "YourService";
       
       public bool Enabled { get; set; } = true;
       public int PollingIntervalSeconds { get; set; } = 60;
       public int TimeoutSeconds { get; set; } = 30;
       public int MaxRetries { get; set; } = 3;
   }
   ```

4. **Register in DI** in `Program.cs`
   ```csharp
   // Add configuration
   builder.Services.Configure<YourServiceOptions>(
       builder.Configuration.GetSection(YourServiceOptions.SectionName));
   
   // Register service
   builder.Services.AddSingleton<IYourService, YourService>();
   
   // Register in main agent service
   builder.Services.AddHostedService<YourServiceHostedService>();
   ```

5. **Add to appsettings.json**
   ```json
   {
     "YourService": {
       "Enabled": true,
       "PollingIntervalSeconds": 60,
       "TimeoutSeconds": 30,
       "MaxRetries": 3
     }
   }
   ```

## Device Monitoring Pattern

### Existing Device Monitoring (AgentService)

The core agent monitoring follows this pattern:

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    _logger.LogInformation("Agent service starting");
    
    // Initial device load
    await LoadDevicesAsync(stoppingToken);
    
    while (!stoppingToken.IsCancellationRequested)
    {
        try
        {
            // Reload devices periodically
            if (_lastDeviceLoad + TimeSpan.FromMinutes(5) < DateTime.UtcNow)
            {
                await LoadDevicesAsync(stoppingToken);
            }
            
            // Monitor each device based on its ping frequency
            var tasks = _devices
                .Where(d => ShouldPingDevice(d))
                .Select(d => MonitorDeviceAsync(d, stoppingToken));
            
            await Task.WhenAll(tasks);
            
            // Wait before next cycle
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in agent service loop");
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); // Back off on error
        }
    }
    
    _logger.LogInformation("Agent service stopping");
}

private bool ShouldPingDevice(Device device)
{
    // Check if enough time has passed since last ping
    var lastPing = _lastPingTimes.GetValueOrDefault(device.Id, DateTime.MinValue);
    var frequency = TimeSpan.FromSeconds(device.PingFrequencySeconds);
    return DateTime.UtcNow - lastPing >= frequency;
}

private async Task MonitorDeviceAsync(Device device, CancellationToken ct)
{
    try
    {
        var isOnline = await _networkMonitor.PingAsync(device.Ip, ct);
        
        // Update status via API
        await _statusPublisher.PublishStatusAsync(new DeviceStatus
        {
            DeviceId = device.Id,
            IsOnline = isOnline,
            Timestamp = DateTimeOffset.UtcNow
        }, ct);
        
        _lastPingTimes[device.Id] = DateTime.UtcNow;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error monitoring device {DeviceId}", device.Id);
    }
}
```

### Adding Custom Device Monitoring

To add monitoring for a new device type:

1. **Check if device type supported**
   ```csharp
   if (device.Type == "YourDeviceType")
   {
       await MonitorYourDeviceAsync(device, ct);
   }
   ```

2. **Implement device-specific monitoring**
   ```csharp
   private async Task MonitorYourDeviceAsync(Device device, CancellationToken ct)
   {
       // Device-specific probing logic
       var status = await ProbeDeviceAsync(device.Ip, device.Port, ct);
       
       // Report status
       await _statusPublisher.PublishStatusAsync(new DeviceStatus
       {
           DeviceId = device.Id,
           IsOnline = status.IsReachable,
           Timestamp = DateTimeOffset.UtcNow,
           AdditionalData = status.CustomData
       }, ct);
   }
   ```

## Command Execution Pattern

### Queue-Based Command Polling

The agent polls Azure Queue Storage for commands:

```csharp
public class CommandService : ICommandService
{
    private readonly HttpClient _httpClient;
    private readonly IJwtAuthService _auth;
    private readonly ILogger<CommandService> _logger;
    
    public async Task<AgentCommand?> PollForCommandAsync(CancellationToken ct)
    {
        try
        {
            // Get JWT token
            var token = await _auth.GetTokenAsync(ct);
            
            // Poll queue via API
            var request = new HttpRequestMessage(HttpMethod.Post, "api/agents/commands/receive");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            
            var response = await _httpClient.SendAsync(request, ct);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Command poll returned {StatusCode}", response.StatusCode);
                return null;
            }
            
            var command = await response.Content.ReadFromJsonAsync<AgentCommand>(ct);
            
            if (command != null)
            {
                _logger.LogInformation("Received command {CommandId}: {Verb}", 
                    command.Id, command.Verb);
            }
            
            return command;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error polling for commands");
            return null;
        }
    }
    
    public async Task ExecuteCommandAsync(AgentCommand command, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Executing command {CommandId}: {Verb}", 
                command.Id, command.Verb);
            
            var result = command.Verb switch
            {
                "PING" => await ExecutePingAsync(command, ct),
                "REBOOT" => await ExecuteRebootAsync(command, ct),
                "HTTP_GET" => await ExecuteHttpGetAsync(command, ct),
                "HTTP_POST" => await ExecuteHttpPostAsync(command, ct),
                _ => throw new NotSupportedException($"Command verb '{command.Verb}' not supported")
            };
            
            // Acknowledge command
            await AcknowledgeCommandAsync(command, ct);
            
            // Report completion
            await ReportCompletionAsync(command.Id, result.Success, result.Message, ct);
            
            _logger.LogInformation("Command {CommandId} completed successfully", command.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command {CommandId}", command.Id);
            
            // Report failure (don't acknowledge - let it retry)
            await ReportCompletionAsync(command.Id, false, ex.Message, ct);
        }
    }
}
```

### Adding New Command Verbs

To add a new command verb:

1. **Add verb constant**
   ```csharp
   public static class CommandVerbs
   {
       public const string Ping = "PING";
       public const string Reboot = "REBOOT";
       public const string YourNewVerb = "YOUR_NEW_VERB";
   }
   ```

2. **Implement command handler**
   ```csharp
   private async Task<CommandResult> ExecuteYourNewVerbAsync(AgentCommand command, CancellationToken ct)
   {
       try
       {
           // Parse payload
           var payload = JsonSerializer.Deserialize<YourPayload>(command.Payload);
           
           // Execute command logic
           // ...
           
           return new CommandResult { Success = true, Message = "Command executed" };
       }
       catch (Exception ex)
       {
           _logger.LogError(ex, "Error executing YOUR_NEW_VERB command");
           return new CommandResult { Success = false, Message = ex.Message };
       }
   }
   ```

3. **Add to command switch**
   ```csharp
   var result = command.Verb switch
   {
       CommandVerbs.Ping => await ExecutePingAsync(command, ct),
       CommandVerbs.YourNewVerb => await ExecuteYourNewVerbAsync(command, ct),
       _ => throw new NotSupportedException($"Command verb '{command.Verb}' not supported")
   };
   ```

## JWT Authentication

### Token Management

The agent uses JWT tokens for API authentication:

```csharp
public class JwtAuthService : IJwtAuthService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<JwtAuthService> _logger;
    private readonly string _apiKey;
    
    private string? _currentToken;
    private DateTimeOffset _tokenExpiry;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    
    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        // Check if token needs refresh (2-minute buffer)
        if (_currentToken == null || _tokenExpiry < DateTimeOffset.UtcNow.AddMinutes(2))
        {
            await _refreshLock.WaitAsync(ct);
            try
            {
                // Double-check after acquiring lock
                if (_currentToken == null || _tokenExpiry < DateTimeOffset.UtcNow.AddMinutes(2))
                {
                    await RefreshTokenAsync(ct);
                }
            }
            finally
            {
                _refreshLock.Release();
            }
        }
        
        return _currentToken!;
    }
    
    private async Task RefreshTokenAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Refreshing JWT token");
            
            var request = new { agentKey = _apiKey };
            var response = await _httpClient.PostAsJsonAsync("api/agents/auth", request, ct);
            response.EnsureSuccessStatusCode();
            
            var authResponse = await response.Content.ReadFromJsonAsync<AuthResponse>(ct);
            
            _currentToken = authResponse!.Token;
            _tokenExpiry = authResponse.ExpiresAt;
            
            _logger.LogInformation("JWT token refreshed, expires at {Expiry}", _tokenExpiry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh JWT token");
            throw;
        }
    }
}
```

### Using JWT in API Calls

```csharp
// Get token
var token = await _auth.GetTokenAsync(ct);

// Use in HTTP request
var request = new HttpRequestMessage(HttpMethod.Get, "api/agents/devices");
request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

var response = await _httpClient.SendAsync(request, ct);
```

## Error Handling and Retry Logic

### Exponential Backoff Pattern

```csharp
public class RetryHelper
{
    private static readonly TimeSpan[] RetryDelays = new[]
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    };
    
    public static async Task<T> RetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        ILogger logger,
        CancellationToken ct,
        int maxAttempts = 5)
    {
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                return await operation(ct);
            }
            catch (Exception ex) when (attempt < maxAttempts - 1)
            {
                var delay = RetryDelays[Math.Min(attempt, RetryDelays.Length - 1)];
                logger.LogWarning(ex, 
                    "Operation failed (attempt {Attempt}/{MaxAttempts}), retrying in {Delay}",
                    attempt + 1, maxAttempts, delay);
                
                await Task.Delay(delay, ct);
            }
        }
        
        throw new InvalidOperationException("Max retry attempts exceeded");
    }
}
```

### Usage Example

```csharp
var devices = await RetryHelper.RetryAsync(
    async ct =>
    {
        var token = await _auth.GetTokenAsync(ct);
        var response = await _httpClient.GetAsync("api/agents/devices", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<Device>>(ct);
    },
    _logger,
    stoppingToken
);
```

## Network Operations

### ICMP Ping

```csharp
public class NetworkMonitor : INetworkMonitor
{
    private readonly ILogger<NetworkMonitor> _logger;
    
    public async Task<bool> PingAsync(string host, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 5000); // 5 second timeout
            
            bool isReachable = reply.Status == IPStatus.Success;
            
            _logger.LogDebug("Ping {Host}: {Status} ({RoundtripTime}ms)", 
                host, reply.Status, reply.RoundtripTime);
            
            return isReachable;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error pinging {Host}", host);
            return false;
        }
    }
}
```

### TCP Probe

```csharp
public async Task<bool> ProbePortAsync(string host, int port, CancellationToken ct)
{
    try
    {
        using var tcpClient = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(5000); // 5 second timeout
        
        await tcpClient.ConnectAsync(host, port, cts.Token);
        
        _logger.LogDebug("TCP probe {Host}:{Port}: Connected", host, port);
        return true;
    }
    catch (Exception ex)
    {
        _logger.LogDebug("TCP probe {Host}:{Port}: Failed - {Message}", host, port, ex.Message);
        return false;
    }
}
```

## Configuration Management

### Environment-Specific Configuration

```json
// appsettings.json (Base configuration)
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  },
  "Api": {
    "BaseUrl": "https://api.prodcontrolav.com",
    "ApiKey": "",
    "TimeoutSeconds": 30
  },
  "Agent": {
    "TenantId": "",
    "DeviceRefreshMinutes": 5,
    "CommandPollIntervalSeconds": 10,
    "StatusUploadBatchSize": 10
  },
  "Monitoring": {
    "DefaultPingFrequencySeconds": 300,
    "MaxConcurrentPings": 10,
    "PingTimeoutSeconds": 5
  }
}

// appsettings.Development.json (Local overrides)
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug"
    }
  },
  "Api": {
    "BaseUrl": "https://localhost:5001"
  }
}

// appsettings.Production.json (Production overrides)
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

### Configuration Validation

```csharp
public class AgentOptions
{
    public const string SectionName = "Agent";
    
    [Required]
    public Guid TenantId { get; set; }
    
    [Range(1, 60)]
    public int DeviceRefreshMinutes { get; set; } = 5;
    
    [Range(5, 300)]
    public int CommandPollIntervalSeconds { get; set; } = 10;
    
    public void Validate()
    {
        if (TenantId == Guid.Empty)
            throw new InvalidOperationException("Agent TenantId is required");
    }
}

// In Program.cs
builder.Services.AddOptions<AgentOptions>()
    .Bind(builder.Configuration.GetSection(AgentOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

## Deployment on Raspberry Pi

### Cross-Compilation

```bash
# Publish for ARM64 Raspberry Pi
dotnet publish src/ProdControlAV.Agent/ProdControlAV.Agent.csproj \
    -c Release \
    -r linux-arm64 \
    --self-contained true \
    -o ./publish/agent-pi
```

### Systemd Service Setup

```ini
# /etc/systemd/system/prodcontrolav-agent.service
[Unit]
Description=ProdControlAV Agent
After=network.target

[Service]
Type=notify
User=prodcontrol
Group=prodcontrol
WorkingDirectory=/opt/prodcontrolav-agent
ExecStart=/opt/prodcontrolav-agent/ProdControlAV.Agent
Restart=always
RestartSec=10
SyslogIdentifier=prodcontrolav-agent
Environment="DOTNET_ENVIRONMENT=Production"

# Security settings
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/opt/prodcontrolav-agent/logs

# Network capabilities (needed for ICMP ping)
AmbientCapabilities=CAP_NET_RAW
CapabilityBoundingSet=CAP_NET_RAW

[Install]
WantedBy=multi-user.target
```

### Deployment Script

```bash
#!/bin/bash
# deploy-agent.sh

PI_HOST="192.168.1.100"
PI_USER="pi"
DEPLOY_PATH="/opt/prodcontrolav-agent"

echo "Building agent..."
dotnet publish -c Release -r linux-arm64 --self-contained true -o ./publish/agent-pi

echo "Stopping agent service..."
ssh $PI_USER@$PI_HOST "sudo systemctl stop prodcontrolav-agent"

echo "Copying files..."
rsync -avz --delete ./publish/agent-pi/ $PI_USER@$PI_HOST:$DEPLOY_PATH/

echo "Setting permissions..."
ssh $PI_USER@$PI_HOST "sudo chown -R prodcontrol:prodcontrol $DEPLOY_PATH && sudo chmod +x $DEPLOY_PATH/ProdControlAV.Agent"

echo "Starting agent service..."
ssh $PI_USER@$PI_HOST "sudo systemctl start prodcontrolav-agent"

echo "Checking status..."
ssh $PI_USER@$PI_HOST "sudo systemctl status prodcontrolav-agent"
```

## Testing Agent Changes

### Unit Tests

```csharp
public class AgentServiceTests
{
    [Fact]
    public async Task MonitorDevice_WhenOnline_ReportsOnlineStatus()
    {
        // Arrange
        var mockNetworkMonitor = new Mock<INetworkMonitor>();
        mockNetworkMonitor.Setup(m => m.PingAsync("192.168.1.100", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        
        var mockStatusPublisher = new Mock<IStatusPublisher>();
        
        var service = new AgentService(mockNetworkMonitor.Object, mockStatusPublisher.Object);
        
        var device = new Device { Id = Guid.NewGuid(), Ip = "192.168.1.100" };
        
        // Act
        await service.MonitorDeviceAsync(device, CancellationToken.None);
        
        // Assert
        mockStatusPublisher.Verify(p => p.PublishStatusAsync(
            It.Is<DeviceStatus>(s => s.DeviceId == device.Id && s.IsOnline),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

### Integration Tests

```csharp
public class AgentIntegrationTests
{
    [Fact]
    public async Task AgentCanAuthenticateAndFetchDevices()
    {
        // Arrange
        using var apiHost = CreateTestHost();
        var httpClient = apiHost.GetTestClient();
        
        var auth = new JwtAuthService(httpClient, _logger, _options);
        var deviceSource = new DeviceSource(httpClient, auth, _logger);
        
        // Act
        var devices = await deviceSource.GetDevicesAsync(CancellationToken.None);
        
        // Assert
        Assert.NotNull(devices);
    }
}
```

## Common Pitfalls

### ❌ Avoid These Anti-Patterns

1. **Querying SQL database directly**
   ```csharp
   // ❌ BAD - agent should never query SQL
   var devices = await _db.Devices.ToListAsync();
   ```
   
   **Fix**: Use API endpoints that read from Table Storage

2. **Not using JWT authentication**
   ```csharp
   // ❌ BAD - using agent key directly
   request.Headers.Add("X-Api-Key", _apiKey);
   ```
   
   **Fix**: Use JWT tokens
   ```csharp
   // ✅ GOOD
   var token = await _auth.GetTokenAsync(ct);
   request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
   ```

3. **Blocking the main loop**
   ```csharp
   // ❌ BAD - synchronous operation blocks loop
   var status = MonitorDevice(device); // Blocks other devices
   ```
   
   **Fix**: Use async operations
   ```csharp
   // ✅ GOOD
   var tasks = _devices.Select(d => MonitorDeviceAsync(d, ct));
   await Task.WhenAll(tasks);
   ```

4. **Not handling cancellation**
   ```csharp
   // ❌ BAD - ignores cancellation token
   while (true)
   {
       await DoWorkAsync();
       await Task.Delay(1000);
   }
   ```
   
   **Fix**: Respect cancellation token
   ```csharp
   // ✅ GOOD
   while (!stoppingToken.IsCancellationRequested)
   {
       await DoWorkAsync(stoppingToken);
       await Task.Delay(1000, stoppingToken);
   }
   ```

5. **Excessive logging**
   ```csharp
   // ❌ BAD - logs every ping (thousands per day)
   _logger.LogInformation("Pinging device {DeviceId}", deviceId);
   ```
   
   **Fix**: Use appropriate log levels
   ```csharp
   // ✅ GOOD
   _logger.LogDebug("Pinging device {DeviceId}", deviceId);
   ```

## Performance Optimization

### Connection Pooling

```csharp
// In Program.cs
builder.Services.AddHttpClient("AgentClient", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"]);
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "ProdControlAV-Agent/1.0");
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
    MaxConnectionsPerServer = 10
});
```

### Batch Status Uploads

```csharp
// Collect statuses
var statusBatch = new List<DeviceStatus>();

foreach (var device in devices)
{
    var status = await MonitorDeviceAsync(device, ct);
    statusBatch.Add(status);
    
    // Upload in batches of 10
    if (statusBatch.Count >= 10)
    {
        await _statusPublisher.PublishBatchAsync(statusBatch, ct);
        statusBatch.Clear();
    }
}

// Upload remaining
if (statusBatch.Any())
{
    await _statusPublisher.PublishBatchAsync(statusBatch, ct);
}
```

## Monitoring and Observability

### Structured Logging

```csharp
_logger.LogInformation(
    "Monitored {DeviceCount} devices in {Duration}ms, {OnlineCount} online",
    deviceCount, stopwatch.ElapsedMilliseconds, onlineCount);
```

### Health Reporting

```csharp
public class AgentHealthReporter
{
    public async Task ReportHealthAsync(CancellationToken ct)
    {
        var health = new AgentHealth
        {
            AgentId = _agentId,
            Timestamp = DateTimeOffset.UtcNow,
            IsHealthy = true,
            DeviceCount = _devices.Count,
            LastCommandReceived = _lastCommandTime,
            UptimeSeconds = (int)(DateTime.UtcNow - _startTime).TotalSeconds
        };
        
        await _httpClient.PostAsJsonAsync("api/agents/health", health, ct);
    }
}
```

## Deployment Checklist

Before deploying agent changes:

- [ ] Tested on Raspberry Pi hardware
- [ ] JWT authentication working
- [ ] Command execution tested
- [ ] Device monitoring verified
- [ ] Error handling tested (network failures, API unavailable)
- [ ] Resource usage acceptable (CPU < 10%, Memory < 100MB)
- [ ] Systemd service starts on boot
- [ ] Logs properly written and rotated
- [ ] Configuration validated
- [ ] Rollback plan documented

## References

- [Agent README](../../src/ProdControlAV.Agent/README.md)
- [JWT Authentication](../JWT_AUTHENTICATION.md)
- [Queue Commands](../QUEUE_COMMANDS.md)
- [Agent Deployment](../../src/ProdControlAV.Agent/scripts/deploy.sh)
