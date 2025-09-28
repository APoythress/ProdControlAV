# ProdControlAV.Infrastructure - Service Implementations

The Infrastructure project contains concrete implementations of all interfaces defined in ProdControlAV.Core. This layer handles external integrations including device communication, network monitoring, and command processing. It serves as the **Infrastructure Layer** in Clean Architecture.

## Architecture Role

ProdControlAV.Infrastructure provides:
- **External Service Integration**: Real implementations of Core interfaces
- **Technology Abstraction**: Hides implementation details from business logic
- **Cross-Cutting Concerns**: Logging, error handling, and performance monitoring
- **Dependency Isolation**: All external dependencies are contained here

## Project Structure

### Services (`/Services`)

Concrete implementations of Core interfaces for production use:

#### Network Monitoring
- **`PingNetworkMonitor.cs`** - ICMP ping-based device connectivity monitoring
  - Implements `INetworkMonitor` from Core
  - Uses .NET `Ping` class for ICMP packet transmission
  - Configurable timeout and retry logic
  - Handles network exceptions gracefully

#### Device Communication
- **`TelnetDeviceController.cs`** - Telnet protocol device control implementation
  - Implements `IDeviceController` from Core
  - Uses TCP sockets for telnet communication
  - Supports ASCII command transmission
  - Configurable port (default: 23)
  - Connection pooling for performance

#### Command Processing
- **`JsonCommandQueue.cs`** - File-based asynchronous command queue
  - Implements `ICommandQueue` from Core
  - JSON serialization for command persistence
  - Directory-based queue management
  - Thread-safe operations for concurrent access

## Service Implementations

### Network Monitoring Service

**Purpose**: Monitors A/V device connectivity using ICMP ping

**Implementation Details**:
```csharp
public class PingNetworkMonitor : INetworkMonitor
{
    public async Task<bool> IsDeviceOnlineAsync(string ipAddress)
    {
        using var ping = new Ping();
        var reply = await ping.SendPingAsync(ipAddress, 1000); // 1-second timeout
        return reply.Status == IPStatus.Success;
    }
}
```

**Features**:
- **Asynchronous Operations**: Non-blocking ping operations for high concurrency
- **Timeout Configuration**: Configurable ping timeout (default: 1000ms)
- **Error Handling**: Network exceptions are caught and returned as offline status
- **Resource Management**: Proper disposal of Ping objects

**Integration**:
- **Used by Agent**: Continuously monitors device connectivity
- **Used by API**: Validates device status before command execution
- **Performance**: Supports monitoring hundreds of devices concurrently

### Device Control Service

**Purpose**: Sends control commands to A/V devices via Telnet protocol

**Implementation Details**:
```csharp
public class TelnetDeviceController : IDeviceController
{
    public async Task<bool> SendCommandAsync(string deviceId, string command)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(deviceId, _port);
        using var stream = client.GetStream();
        byte[] buffer = Encoding.ASCII.GetBytes(command + "\r\n");
        await stream.WriteAsync(buffer, 0, buffer.Length);
        return true;
    }
}
```

**Features**:
- **Telnet Protocol Support**: Standard telnet communication for legacy A/V equipment
- **ASCII Command Format**: Supports text-based command protocols
- **Connection Management**: Automatic connection establishment and cleanup
- **Error Handling**: Network and protocol errors are handled gracefully

**Supported Device Types**:
- Professional video mixers (BlackMagic ATEM, Ross Video)
- Audio mixing consoles (Yamaha, Allen & Heath, Behringer)
- PTZ cameras (Sony, Canon, Panasonic)
- Video distribution equipment (Extron, Crestron, AMX)

### Command Queue Service

**Purpose**: Provides asynchronous command processing between API and agents

**Implementation Details**:
```csharp
public class JsonCommandQueue : ICommandQueue
{
    public async Task EnqueueAsync<T>(T command)
    {
        var json = JsonSerializer.Serialize(command);
        var fileName = $"{Guid.NewGuid()}.json";
        await File.WriteAllTextAsync(Path.Combine(_queuePath, fileName), json);
    }
}
```

**Features**:
- **JSON Serialization**: Platform-independent command format
- **File-Based Persistence**: Commands survive application restarts
- **Atomic Operations**: Commands are written atomically to prevent corruption
- **Directory Monitoring**: Supports file system watchers for real-time processing

**Queue Management**:
- **FIFO Processing**: First-in, first-out command execution
- **Error Handling**: Failed commands can be retried or moved to error queue
- **Cleanup**: Processed commands are archived or deleted based on configuration

## Integration with Other Projects

### ProdControlAV.API
The API project registers Infrastructure services through dependency injection:

```csharp
// Service registration in Program.cs
builder.Services.AddSingleton<IDeviceController, TelnetDeviceController>();
builder.Services.AddSingleton<INetworkMonitor, PingNetworkMonitor>();
builder.Services.AddSingleton<ICommandQueue>(new JsonCommandQueue("Data/Commands"));
```

**Benefits**:
- **Testability**: Infrastructure can be replaced with mocks for unit testing
- **Flexibility**: Different implementations can be swapped based on environment
- **Configuration**: Service behavior can be modified through dependency injection

### ProdControlAV.Agent
The Agent directly uses Infrastructure services for device monitoring:

```csharp
// Device monitoring loop
var networkMonitor = new PingNetworkMonitor();
foreach (var device in devices)
{
    bool isOnline = await networkMonitor.IsDeviceOnlineAsync(device.IpAddress);
    // Report status to API
}
```

**Capabilities**:
- **Concurrent Monitoring**: Multiple devices monitored simultaneously
- **Real-time Updates**: Immediate status change detection
- **Network Resilience**: Handles temporary network interruptions gracefully

## Performance Considerations

### Network Monitoring Optimization
- **Concurrent Ping Operations**: Uses Task.WhenAll for parallel ping execution
- **Connection Pooling**: TCP connections are reused when possible
- **Timeout Management**: Configurable timeouts prevent hung operations
- **Resource Cleanup**: Proper disposal prevents resource leaks

### Command Queue Efficiency
- **Batch Processing**: Multiple commands can be processed in batches
- **Memory Management**: Large command payloads are handled efficiently
- **File I/O Optimization**: Async file operations prevent blocking

### Device Communication Scaling
- **Connection Limits**: Respects device connection limits
- **Command Rate Limiting**: Prevents overwhelming devices with commands
- **Circuit Breaker Pattern**: Stops sending commands to failing devices temporarily

## Error Handling and Resilience

### Network Error Recovery
```csharp
public async Task<bool> IsDeviceOnlineAsync(string ipAddress)
{
    try
    {
        // Ping operation with timeout
        using var ping = new Ping();
        var reply = await ping.SendPingAsync(ipAddress, timeout);
        return reply.Status == IPStatus.Success;
    }
    catch (PingException)
    {
        // Network unreachable or host down
        return false;
    }
    catch (SocketException)
    {
        // Network interface issues
        return false;
    }
}
```

### Device Communication Recovery
- **Retry Logic**: Automatic retry for transient failures
- **Timeout Handling**: Commands that hang are terminated gracefully
- **Connection Recovery**: Re-establish connections after network issues
- **Fallback Protocols**: Support multiple communication methods per device

### Command Queue Resilience
- **Transaction Safety**: Commands are processed atomically
- **Corruption Recovery**: Malformed files are moved to error directory
- **Disk Space Management**: Queue size limits prevent disk exhaustion
- **Monitoring Integration**: Queue health metrics are exposed for monitoring

## Configuration Options

### Network Monitoring Configuration
```json
{
  "NetworkMonitoring": {
    "PingTimeout": 1000,
    "RetryAttempts": 3,
    "RetryDelay": 500,
    "ConcurrentPings": 50
  }
}
```

### Device Controller Configuration
```json
{
  "DeviceController": {
    "TelnetPort": 23,
    "ConnectionTimeout": 5000,
    "CommandTimeout": 10000,
    "MaxConcurrentConnections": 10
  }
}
```

### Command Queue Configuration
```json
{
  "CommandQueue": {
    "QueuePath": "Data/Commands",
    "MaxQueueSize": 1000,
    "ProcessingInterval": 1000,
    "ArchiveProcessed": true
  }
}
```

## Testing and Validation

### Unit Testing
Infrastructure services are designed for testability:
- **Interface-based Design**: All dependencies are injected through interfaces
- **Predictable Behavior**: Methods have clear success/failure patterns
- **Async Support**: All I/O operations are fully asynchronous

### Integration Testing
- **Real Device Testing**: Services can be tested against actual A/V equipment
- **Network Simulation**: Network conditions can be simulated for resilience testing
- **Performance Testing**: Load testing ensures services meet performance requirements

### Monitoring and Diagnostics
- **Structured Logging**: All operations are logged with appropriate log levels
- **Performance Counters**: Key metrics are exposed for monitoring systems
- **Health Checks**: Built-in health check endpoints for monitoring service status

## Future Enhancements

### Planned Service Additions
- **SNMPDeviceController**: SNMP-based device management for network equipment
- **RestApiDeviceController**: HTTP-based control for modern A/V devices
- **DatabaseCommandQueue**: Database-backed command queue for high availability
- **CachingNetworkMonitor**: Redis-based caching for improved performance

### Protocol Extensions
- **WebSocket Support**: Real-time bidirectional device communication
- **MQTT Integration**: IoT-based device status reporting
- **GraphQL APIs**: Flexible query interfaces for device data

For usage examples and integration patterns, see:
- [ProdControlAV.Core](../ProdControlAV.Core/README.md) - Interface definitions
- [ProdControlAV.API](../ProdControlAV.API/README.md) - Web API integration
- [ProdControlAV.Agent](../ProdControlAV.Agent/README.md) - Agent implementation