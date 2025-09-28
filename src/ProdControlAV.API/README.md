# ProdControlAV.API - Web API Backend

The API project is an ASP.NET Core Web API that serves as the central backend for the ProdControlAV system. It provides REST endpoints, manages authentication and multi-tenancy, handles data persistence through Entity Framework Core, and hosts the Blazor WebAssembly frontend application.

## Architecture Role

ProdControlAV.API serves as the **Application Layer** and **Presentation Layer**:
- **REST API**: Exposes business logic through HTTP endpoints
- **Authentication & Authorization**: Multi-tenant user management and access control
- **Data Persistence**: Entity Framework Core with Azure SQL Database
- **Static File Hosting**: Serves the compiled Blazor WebAssembly application
- **API Documentation**: Swagger/OpenAPI integration for development

## Project Structure

### Controllers (`/Controllers`)

REST API endpoints organized by functional area:

#### Core Device Management
- **`DevicesController.cs`** - Device CRUD operations and status management
  - `GET /api/devices/devices` - List all devices for current tenant
  - `POST /api/devices` - Create new device
  - `PUT /api/devices/{id}` - Update device configuration
  - `DELETE /api/devices/{id}` - Remove device

- **`DeviceManagerController.cs`** - Advanced device management operations
- **`StatusController.cs`** - Real-time device status reporting

#### Agent Management
- **`AgentsController.cs`** - Raspberry Pi agent registration and management
  - `POST /api/agents/register` - Register new monitoring agent
  - `GET /api/agents` - List agents for current tenant
  - `PUT /api/agents/{id}/heartbeat` - Agent heartbeat and status reporting

- **`CommandController.cs`** - Agent command distribution
  - `GET /api/commands/pending/{agentId}` - Retrieve pending commands for agent
  - `POST /api/commands/{commandId}/acknowledge` - Acknowledge command execution

#### Authentication & Tenancy
- **`AuthController.cs`** - User authentication and session management
  - `POST /api/auth/login` - User authentication
  - `POST /api/auth/logout` - Session termination
  - `GET /api/auth/profile` - Current user information

- **`TenantsController.cs`** - Multi-tenant organization management
- **`SecurityController.cs`** - CSRF token generation and security features

#### External Integrations
- **`EdgeController.cs`** - Edge device communication and heartbeat
- **`PicoBridgeController.cs`** - Pico Bridge integration for specialized A/V equipment

### Data Layer (`/Data`)

Entity Framework Core database context and configuration:

#### Database Context
- **`AppDbContext.cs`** - Main database context with multi-tenant filtering
  - Automatic tenant isolation through `CurrentTenantId`
  - DbSets for all Core entities (Device, Agent, Tenant, etc.)
  - Entity configuration and relationships

- **`DesignTimeDbContextFactory.cs`** - Design-time context for migrations
  - Supports Entity Framework tooling
  - Configuration for different environments

#### Multi-Tenant Data Access
```csharp
public class AppDbContext : DbContext
{
    private readonly ITenantProvider _tenant;
    protected Guid CurrentTenantId => _tenant.TenantId;

    // All queries are automatically filtered by tenant
    public Task<List<Device>> GetDevicesAsync() =>
        Devices.Where(d => d.TenantId == CurrentTenantId).ToListAsync();
}
```

### Authentication & Authorization (`/Auth`)

Multi-tenant security implementation:

#### Tenant Providers
- **`ClaimsTenantProvider.cs`** - Extracts tenant ID from user claims
- **`CompositeTenantProvider.cs`** - Combines multiple tenant resolution strategies
- **`TenantAuthorization.cs`** - Authorization policies and requirement handlers

#### Security Features
- **Cookie-based Authentication**: Secure HTTP-only cookies with anti-forgery protection
- **Multi-tenant Authorization**: Row-level security ensuring tenant data isolation
- **API Key Authentication**: Agent authentication using secure API keys
- **CSRF Protection**: Anti-forgery token validation for state-changing operations

### Middleware & Services (`/Services`)

Cross-cutting concerns and infrastructure:

- **`ApiKeyMiddleware.cs`** - API key validation for agent requests
- **Custom Services**: Integration with Infrastructure layer services
- **Health Checks**: Application and database health monitoring

## API Endpoints

### Device Management APIs

#### List Devices
```http
GET /api/devices/devices
Authorization: Cookie (user) or API Key (agent)
```
Returns all devices accessible to the current tenant.

#### Create Device
```http
POST /api/devices
Content-Type: application/json
Authorization: Cookie

{
    "name": "Camera 1",
    "model": "PTZ-200",
    "brand": "Sony", 
    "type": "Camera",
    "ip": "192.168.1.100",
    "port": 80,
    "location": "Studio A"
}
```

#### Update Device Status
```http
PUT /api/devices/{deviceId}/status
Content-Type: application/json
Authorization: API Key

{
    "isOnline": true,
    "lastResponse": "OK",
    "timestamp": "2024-01-01T12:00:00Z"
}
```

### Agent Management APIs

#### Register Agent
```http
POST /api/agents/register
Content-Type: application/json
Authorization: API Key

{
    "name": "Pi-Studio-A",
    "version": "1.0.0",
    "capabilities": ["ping", "telnet"],
    "location": "Studio A Equipment Rack"
}
```

#### Agent Heartbeat
```http
PUT /api/agents/{agentId}/heartbeat
Content-Type: application/json
Authorization: API Key

{
    "status": "healthy",
    "deviceCount": 15,
    "uptime": 86400,
    "memoryUsage": 45.2
}
```

### Command Distribution APIs

#### Get Pending Commands
```http
GET /api/commands/pending/{agentId}
Authorization: API Key
```
Returns list of commands waiting for execution by the specified agent.

#### Acknowledge Command
```http
POST /api/commands/{commandId}/acknowledge
Content-Type: application/json
Authorization: API Key

{
    "success": true,
    "response": "Command executed successfully",
    "executedAt": "2024-01-01T12:00:00Z"
}
```

## Authentication & Multi-Tenancy

### Cookie Authentication (Web Users)
- **Secure Cookies**: HTTP-only, secure, same-site protection
- **Session Management**: Configurable timeout and sliding expiration
- **User Claims**: User ID, tenant membership, roles, permissions
- **CSRF Protection**: Anti-forgery tokens for state-changing operations

### API Key Authentication (Agents)
```csharp
[HttpGet]
[ApiKeyAuth] // Custom attribute for API key validation
public async Task<IActionResult> GetDevices()
{
    // API key is validated by ApiKeyMiddleware
    // Tenant is resolved from the API key registration
}
```

### Multi-Tenant Data Isolation
All database queries are automatically filtered by tenant:
```csharp
// This query automatically includes: WHERE TenantId = @CurrentTenantId
var devices = await _context.Devices.ToListAsync();

// Cross-tenant access is prevented at the database level
var forbiddenDevices = await _context.Devices
    .Where(d => d.TenantId != _tenant.TenantId) // This would return empty
    .ToListAsync();
```

## Database Integration

### Entity Framework Configuration

#### Connection String
```json
{
    "ConnectionStrings": {
        "DefaultConnection": "Server=tcp:prodcontrol.database.windows.net,1433;Database=ProdControlAV;Encrypt=True;TrustServerCertificate=False;"
    }
}
```

#### Service Registration
```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
```

### Database Schema

#### Core Tables
- **Devices**: A/V equipment inventory with tenant isolation
- **DeviceStatusLogs**: Historical device status changes
- **DeviceActions**: Available commands per device type
- **Agents**: Registered monitoring agents
- **AgentCommands**: Command queue between API and agents

#### Multi-Tenancy Tables
- **Tenants**: Organization/client isolation entities
- **Users**: Authentication and user profile information
- **UserTenants**: Many-to-many relationship for user-tenant access

### Migration Management
```bash
# Create new migration
dotnet ef migrations add <MigrationName> --project src/ProdControlAV.API

# Update database
dotnet ef database update --project src/ProdControlAV.API

# Generate SQL script
dotnet ef migrations script --project src/ProdControlAV.API
```

## Swagger/OpenAPI Documentation

### Development API Explorer
Access interactive API documentation at: `https://localhost:5001/swagger`

### API Documentation Features
- **Interactive Testing**: Execute API calls directly from documentation
- **Schema Definitions**: Complete request/response model definitions
- **Authentication**: Integrated API key and cookie authentication testing
- **Multi-Environment Support**: Configure different base URLs for testing

### Configuration
```csharp
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ProdControlAV API",
        Version = "v1",
        Description = "API for monitoring and controlling A/V production equipment"
    });
    
    // Add API key authentication support
    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "X-API-Key"
    });
});
```

## Static File Hosting

### Blazor WebAssembly Integration
The API project hosts the compiled Blazor WebAssembly application:

```csharp
app.UseBlazorFrameworkFiles(); // Serves Blazor framework files
app.UseStaticFiles();          // Serves static content
app.MapFallbackToFile("index.html"); // SPA fallback routing
```

### Benefits
- **Single Deployment**: API and frontend deployed together
- **Same-Origin Policy**: No CORS issues between frontend and backend
- **Simplified Hosting**: One server for both API and web application
- **Performance**: Static files served efficiently by ASP.NET Core

## Configuration

### Application Settings
```json
{
    "ConnectionStrings": {
        "DefaultConnection": "<Azure SQL Connection String>"
    },
    "Authentication": {
        "Cookie": {
            "ExpireTimeSpan": "01:00:00",
            "SlidingExpiration": true,
            "HttpOnly": true,
            "SecurePolicy": "Always"
        }
    },
    "ApiKeys": {
        "ValidKeys": [
            {
                "Key": "<32+ character API key>",
                "Name": "Agent-Studio-A",
                "TenantId": "<tenant-guid>"
            }
        ]
    },
    "Cors": {
        "AllowedOrigins": ["https://localhost:5001"]
    }
}
```

### Environment-Specific Configuration
- **`appsettings.Development.json`**: Development-specific settings
- **`appsettings.Production.json`**: Production configuration
- **Environment Variables**: Sensitive configuration via environment variables

## Performance Optimization

### Database Optimization
- **Connection Pooling**: Automatic connection pool management
- **Query Optimization**: Includes/projections for efficient queries
- **Async Operations**: All database operations are asynchronous
- **No-Tracking Queries**: Read-only queries use `AsNoTracking()`

### Caching Strategies
- **Memory Caching**: Frequently accessed data cached in memory
- **Response Caching**: Static content cached with appropriate headers
- **Distributed Caching**: Redis integration for multi-instance deployments

### Monitoring & Diagnostics
- **Health Checks**: Built-in health check endpoints for monitoring
- **Structured Logging**: Comprehensive logging with correlation IDs
- **Performance Counters**: Built-in ASP.NET Core metrics
- **Application Insights**: Integration with Azure Application Insights

## Security Features

### Input Validation
- **Model Validation**: Data annotation validation for all DTOs
- **SQL Injection Prevention**: Entity Framework parameterized queries
- **XSS Protection**: Content Security Policy headers
- **Request Size Limits**: Configurable request body size limits

### Security Headers
```csharp
app.Use(async (context, next) =>
{
    context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Add("X-Frame-Options", "DENY");
    context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
    await next();
});
```

### Audit Logging
- **Authentication Events**: User login/logout tracking
- **Data Changes**: Entity Framework change tracking
- **Command Execution**: Device command audit trail
- **Security Events**: Failed authentication attempts, suspicious activity

## Integration with Other Projects

### ProdControlAV.Core Integration
- **Entity Mapping**: Core models mapped to database tables
- **Service Interfaces**: Infrastructure services registered for dependency injection
- **Business Logic**: Core interfaces implemented by Infrastructure services

### ProdControlAV.WebApp Integration
- **Static File Hosting**: Compiled WebAssembly application served directly
- **API Communication**: WebApp makes AJAX calls to API endpoints
- **Authentication**: Shared cookie authentication between API and WebApp

### ProdControlAV.Agent Integration
- **API Key Authentication**: Agents authenticate using registered API keys
- **Command Distribution**: API queues commands for agent execution
- **Status Reporting**: Agents report device status via API endpoints

## Development Workflow

### Local Development
```bash
# Start the API server (includes WebApp hosting)
cd src/ProdControlAV.API
dotnet run

# API available at: https://localhost:5001
# WebApp available at: https://localhost:5001 (same URL)
# Swagger documentation: https://localhost:5001/swagger
```

### Database Development
```bash
# Add new migration
dotnet ef migrations add <MigrationName>

# Update local database
dotnet ef database update

# Generate SQL script for production
dotnet ef migrations script --output migration.sql
```

### Testing
```bash
# Run unit tests
dotnet test

# Run integration tests (requires test database)
dotnet test --filter "Category=Integration"
```

For detailed information about related projects, see:
- [ProdControlAV.Core](../ProdControlAV.Core/README.md) - Domain models and interfaces
- [ProdControlAV.Infrastructure](../ProdControlAV.Infrastructure/README.md) - Service implementations
- [ProdControlAV.WebApp](../ProdControlAV.WebApp/README.md) - Blazor frontend application
- [ProdControlAV.Agent](../ProdControlAV.Agent/README.md) - Device monitoring agent