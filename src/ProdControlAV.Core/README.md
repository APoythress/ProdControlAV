# ProdControlAV.Core - Domain Models and Interfaces

The Core project contains the domain models, entities, and service interfaces that define the business logic and contracts used across the entire ProdControlAV system. This project has no dependencies on external frameworks and represents the pure business domain.

## Architecture Role

ProdControlAV.Core serves as the **Domain Layer** in Clean Architecture:
- **No External Dependencies**: Contains only business logic and contracts
- **Shared Across Projects**: Referenced by API, Infrastructure, Agent, and WebApp
- **Domain-Driven Design**: Models represent real-world A/V production concepts
- **Interface Segregation**: Small, focused interfaces for specific capabilities

## Project Structure

### Models (`/Models`)

Domain entities that represent the core business concepts:

#### Device Management
- **`Device.cs`** - A/V equipment entity (cameras, mixers, switches, etc.)
  - Properties: Id, Name, Model, Brand, Type, IP, Port, Location
  - Multi-tenant support via TenantId
  - Status tracking with LastChecked and LastResponse

- **`DeviceStatus.cs`** - Real-time status information for devices
- **`DeviceStatusLog.cs`** - Historical device status changes for auditing
- **`DeviceAction.cs`** - Commands or actions that can be performed on devices

#### Multi-Tenancy
- **`Tenant.cs`** - Organization/client isolation entity
- **`UserTenant.cs`** - Many-to-many relationship between users and tenants
- **`AppUser.cs`** - User entity for authentication and authorization

#### Agent Management
- **`Agent.cs`** - Raspberry Pi monitoring agent registration
- **`AgentCommand.cs`** - Commands sent from API to agents
- **`AgentDtos.cs`** - Data transfer objects for agent communication

### Interfaces (`/Interfaces`)

Service contracts that define system capabilities:

#### Device Control
- **`IDeviceController`** - Contract for sending commands to A/V devices
  - `SendCommandAsync()` - Execute commands on specific devices
  - `GetStatusAsync()` - Retrieve current device status

#### Network Monitoring
- **`INetworkMonitor`** - Contract for device connectivity monitoring
- **`IDeviceStatusRepository`** - Contract for persisting device status information
- **`InMemoryDeviceStatusRepository`** - In-memory implementation for testing

#### Command Processing
- **`ICommandQueue`** - Contract for asynchronous command processing between API and agents

### Configuration (`/Configuration`)
- **`AppVersion.cs`** - Application versioning information

## Integration with Other Projects

### ProdControlAV.API
- **Entity Framework Models**: Device entities are mapped to database tables
- **Controller DTOs**: Models are serialized/deserialized in REST APIs
- **Dependency Injection**: Interfaces are implemented by Infrastructure services
- **Multi-tenant Filtering**: Tenant entities enable data isolation

### ProdControlAV.Infrastructure
- **Service Implementations**: All Core interfaces are implemented here
- **External Integrations**: Device communication, network monitoring
- **Repository Patterns**: Data access implementations for Core interfaces

### ProdControlAV.Agent
- **Device Monitoring**: Uses Device models for target definition
- **Command Execution**: Implements device control interfaces
- **Status Reporting**: Creates DeviceStatus and DeviceStatusLog entries

### ProdControlAV.WebApp
- **UI Models**: Device entities displayed in web interface
- **Real-time Updates**: Device status models for live monitoring
- **Command Interface**: Uses device action models for user interactions

## Domain Concepts

### Multi-Tenancy Design
Every entity includes a `TenantId` property for data isolation:
```csharp
public Guid TenantId { get; set; } // Ensures data separation per organization
```

### Device Types
The system supports various A/V equipment categories:
- **Cameras**: PTZ cameras, fixed cameras, streaming encoders
- **Audio Mixers**: Digital audio consoles, matrix processors
- **Video Mixers**: Live production switchers, video routers
- **Display Systems**: Projectors, LED walls, broadcast monitors
- **Network Equipment**: Managed switches, media converters

### Device Communication
Two primary communication methods are supported:
- **Telnet Control**: For devices supporting telnet command interfaces
- **REST API Control**: For modern devices with HTTP-based APIs
- **SNMP Monitoring**: For network-managed devices (future enhancement)

### Status Monitoring
Device health is tracked through multiple mechanisms:
- **ICMP Ping**: Basic network connectivity testing
- **TCP Port Probes**: Service-specific connectivity validation
- **Device-Specific Queries**: Protocol-specific status requests

## Data Models

### Device Entity Relationships
```
Tenant (1) ←→ (n) Device
Device (1) ←→ (n) DeviceStatusLog
Device (1) ←→ (n) DeviceAction
Agent (1) ←→ (n) Device (monitoring)
```

### Command Flow
```
WebApp → API → AgentCommand → Agent → DeviceController → Device
Device → DeviceStatus → Agent → API → DeviceStatusLog → Database
```

## Interface Design Principles

### Single Responsibility
Each interface focuses on a single aspect of the system:
- `IDeviceController`: Only device command execution
- `INetworkMonitor`: Only connectivity monitoring
- `ICommandQueue`: Only asynchronous message handling

### Async-First Design
All I/O operations are asynchronous to support high-concurrency scenarios:
```csharp
Task<bool> SendCommandAsync(string deviceId, string command);
Task<string?> GetStatusAsync(string deviceId);
```

### Testability
Interfaces enable comprehensive unit testing through mocking:
- No concrete dependencies in business logic
- Predictable method signatures
- Clear success/failure patterns

## Usage Examples

### Device Monitoring
```csharp
// Check device connectivity
bool isOnline = await networkMonitor.PingDeviceAsync(device.Ip);

// Update device status
await deviceStatusRepository.UpdateStatusAsync(device.Id, isOnline);
```

### Device Control
```csharp
// Send command to A/V device
bool success = await deviceController.SendCommandAsync(device.Id, "POWER ON");

// Log the action
await commandQueue.EnqueueAsync(new AgentCommand 
{ 
    DeviceId = device.Id, 
    Command = "POWER ON",
    Timestamp = DateTimeOffset.UtcNow
});
```

## Validation and Business Rules

### Device Validation
- IP addresses must be valid IPv4 format
- Port numbers must be in valid range (1-65535)
- Device names must be unique within a tenant
- Telnet is only allowed for devices that support it securely

### Multi-tenant Rules
- All queries must be filtered by TenantId
- Users can only access devices within their authorized tenants
- Cross-tenant data access is strictly prohibited

### Command Security
- Device commands are validated against allowed command lists
- Sensitive operations require additional authorization
- All commands are logged for audit purposes

## Future Extensibility

The Core domain is designed to support planned enhancements:
- **Device Templates**: Predefined configurations for common device types
- **Scripting Engine**: Custom command sequences and automation
- **Integration APIs**: Webhooks and external system notifications
- **Advanced Monitoring**: Performance metrics and trending analysis

For implementation details of these interfaces, see:
- [ProdControlAV.Infrastructure](../ProdControlAV.Infrastructure/README.md) - Service implementations
- [ProdControlAV.API](../ProdControlAV.API/README.md) - Web API and database integration
- [ProdControlAV.Agent](../ProdControlAV.Agent/README.md) - Device monitoring implementation