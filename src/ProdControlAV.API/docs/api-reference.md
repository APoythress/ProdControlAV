# API Reference Documentation

Comprehensive reference for all ProdControlAV.API REST endpoints.

## Base URL

- **Development**: `https://localhost:5001`
- **Production**: `https://your-production-domain.com`

## Authentication

### Cookie Authentication (Web Users)
Used by the Blazor WebAssembly frontend application.

```http
POST /api/auth/login
Content-Type: application/json

{
    "username": "user@example.com",
    "password": "your-password",
    "rememberMe": true
}
```

**Response**: Sets secure HTTP-only authentication cookie.

### API Key Authentication (Agents)
Used by Raspberry Pi monitoring agents.

```http
GET /api/devices/devices
X-API-Key: your-32-plus-character-api-key-here
```

**Note**: API keys must be at least 32 characters and are registered per tenant.

## Device Management

### List Devices
Get all devices for the current tenant.

```http
GET /api/devices/devices
```

**Authentication**: Cookie or API Key required

**Response**:
```json
[
    {
        "id": "123e4567-e89b-12d3-a456-426614174000",
        "name": "Studio Camera 1",
        "model": "PTZ-200",
        "brand": "Sony",
        "type": "Camera",
        "ip": "192.168.1.100",
        "port": 80,
        "location": "Studio A",
        "status": true,
        "lastChecked": "2024-01-01T12:00:00Z",
        "lastResponse": "OK"
    }
]
```

### Get Device by ID
Retrieve specific device details.

```http
GET /api/devices/{deviceId}
```

**Parameters**:
- `deviceId` (UUID): Device identifier

**Response**: Single device object (same structure as list)

### Create Device
Add a new device to the system.

```http
POST /api/devices
Content-Type: application/json

{
    "name": "New Camera",
    "model": "PTZ-300",
    "brand": "Canon",
    "type": "Camera",
    "ip": "192.168.1.101",
    "port": 80,
    "location": "Studio B",
    "allowTelNet": false
}
```

**Authentication**: Cookie required (web users only)

**Response**: Created device object with assigned ID

### Update Device
Modify existing device configuration.

```http
PUT /api/devices/{deviceId}
Content-Type: application/json

{
    "id": "123e4567-e89b-12d3-a456-426614174000",
    "name": "Updated Camera Name",
    "model": "PTZ-300",
    "brand": "Canon",
    "type": "Camera",
    "ip": "192.168.1.101",
    "port": 80,
    "location": "Studio B",
    "allowTelNet": false
}
```

**Authentication**: Cookie required (web users only)

### Delete Device
Remove device from the system.

```http
DELETE /api/devices/{deviceId}
```

**Authentication**: Cookie required (web users only)

**Response**: `204 No Content` on success

### Update Device Status
Report device status (used by agents).

```http
PUT /api/devices/{deviceId}/status
Content-Type: application/json

{
    "isOnline": true,
    "responseTime": 15,
    "lastResponse": "OK",
    "timestamp": "2024-01-01T12:00:00Z"
}
```

**Authentication**: API Key required

### Get Device Actions
List available actions for devices.

```http
GET /api/devices/actions
```

**Response**:
```json
[
    {
        "id": "123e4567-e89b-12d3-a456-426614174000",
        "name": "Power On",
        "command": "POWER ON",
        "deviceType": "Camera",
        "description": "Turn on the device"
    }
]
```

## Agent Management

### Register Agent
Register a new monitoring agent.

```http
POST /api/agents/register
Content-Type: application/json
X-API-Key: your-api-key

{
    "name": "Pi-Studio-A",
    "version": "1.0.0",
    "capabilities": ["ping", "telnet", "tcp"],
    "location": "Studio A Equipment Rack",
    "description": "Primary monitoring agent for Studio A"
}
```

**Response**:
```json
{
    "id": "123e4567-e89b-12d3-a456-426614174000",
    "name": "Pi-Studio-A",
    "version": "1.0.0",
    "status": "active",
    "registeredAt": "2024-01-01T12:00:00Z"
}
```

### List Agents
Get all agents for the current tenant.

```http
GET /api/agents
```

**Authentication**: Cookie or API Key required

**Response**:
```json
[
    {
        "id": "123e4567-e89b-12d3-a456-426614174000",
        "name": "Pi-Studio-A",
        "version": "1.0.0",
        "status": "active",
        "lastHeartbeat": "2024-01-01T12:00:00Z",
        "location": "Studio A Equipment Rack",
        "deviceCount": 15,
        "uptime": 86400
    }
]
```

### Agent Heartbeat
Update agent status and health information.

```http
PUT /api/agents/{agentId}/heartbeat
Content-Type: application/json
X-API-Key: your-api-key

{
    "status": "healthy",
    "deviceCount": 15,
    "uptime": 86400,
    "memoryUsage": 45.2,
    "cpuUsage": 12.5,
    "networkLatency": 25,
    "errors": [],
    "timestamp": "2024-01-01T12:00:00Z"
}
```

**Response**: `200 OK` with updated agent status

## Command Management

### Get Pending Commands
Retrieve commands waiting for execution (used by agents).

```http
GET /api/commands/pending/{agentId}
X-API-Key: your-api-key
```

**Response**:
```json
[
    {
        "id": "123e4567-e89b-12d3-a456-426614174000",
        "deviceId": "456e7890-e89b-12d3-a456-426614174000",
        "command": "POWER ON",
        "parameters": {},
        "createdAt": "2024-01-01T12:00:00Z",
        "priority": "normal",
        "timeout": 30
    }
]
```

### Acknowledge Command
Report command execution result.

```http
POST /api/commands/{commandId}/acknowledge
Content-Type: application/json
X-API-Key: your-api-key

{
    "success": true,
    "response": "Command executed successfully",
    "executedAt": "2024-01-01T12:00:05Z",
    "duration": 1250,
    "error": null
}
```

**Response**: `200 OK`

### Queue Command
Queue a command for agent execution (used by web interface).

```http
POST /api/commands/queue
Content-Type: application/json

{
    "deviceId": "456e7890-e89b-12d3-a456-426614174000",
    "command": "POWER OFF",
    "parameters": {},
    "priority": "high",
    "timeout": 30
}
```

**Authentication**: Cookie required

**Response**:
```json
{
    "id": "123e4567-e89b-12d3-a456-426614174000",
    "status": "queued",
    "estimatedExecution": "2024-01-01T12:00:10Z"
}
```

## Status and Monitoring

### Get Device Status History
Retrieve historical status information for a device.

```http
GET /api/status/device/{deviceId}/history?from=2024-01-01&to=2024-01-02&limit=100
```

**Parameters**:
- `from` (ISO date): Start date for history
- `to` (ISO date): End date for history  
- `limit` (integer): Maximum number of records (default: 100)

**Response**:
```json
[
    {
        "timestamp": "2024-01-01T12:00:00Z",
        "status": "online",
        "responseTime": 15,
        "details": "ICMP ping successful"
    }
]
```

### Get System Status
Overall system health and statistics.

```http
GET /api/status/system
```

**Response**:
```json
{
    "totalDevices": 45,
    "onlineDevices": 42,
    "offlineDevices": 3,
    "activeAgents": 3,
    "lastUpdate": "2024-01-01T12:00:00Z",
    "averageResponseTime": 18.5
}
```

## Authentication Management

### Login
Authenticate user and establish session.

```http
POST /api/auth/login
Content-Type: application/json

{
    "username": "user@example.com",
    "password": "user-password",
    "rememberMe": true
}
```

**Response**: 
- `200 OK`: Authentication successful, cookie set
- `401 Unauthorized`: Invalid credentials
- `403 Forbidden`: Account locked or disabled

### Logout
Terminate user session.

```http
POST /api/auth/logout
```

**Response**: `200 OK` and authentication cookie cleared

### Get User Profile
Retrieve current user information.

```http
GET /api/auth/profile
```

**Authentication**: Cookie required

**Response**:
```json
{
    "id": "123e4567-e89b-12d3-a456-426614174000",
    "username": "user@example.com",
    "email": "user@example.com",
    "tenants": [
        {
            "id": "456e7890-e89b-12d3-a456-426614174000",
            "name": "Studio Organization",
            "role": "admin"
        }
    ],
    "permissions": ["devices.read", "devices.write", "agents.read"]
}
```

## Tenant Management

### List Tenants
Get tenants accessible to current user.

```http
GET /api/tenants
```

**Authentication**: Cookie required

**Response**:
```json
[
    {
        "id": "456e7890-e89b-12d3-a456-426614174000",
        "name": "Studio Organization",
        "description": "Main studio operations",
        "deviceCount": 45,
        "agentCount": 3,
        "userCount": 12
    }
]
```

## Security Endpoints

### Get CSRF Token
Retrieve anti-forgery token for state-changing operations.

```http
GET /api/security/token
```

**Authentication**: Cookie required

**Response**:
```json
{
    "token": "CfDJ8KuRqH7dF0Q...",
    "expires": "2024-01-01T13:00:00Z"
}
```

## Error Responses

### Standard Error Format
```json
{
    "error": "error_code",
    "message": "Human readable error message",
    "details": "Additional error information",
    "timestamp": "2024-01-01T12:00:00Z",
    "traceId": "00-1234567890abcdef-fedcba0987654321-00"
}
```

### Common HTTP Status Codes

- **200 OK**: Request successful
- **201 Created**: Resource created successfully
- **204 No Content**: Operation successful, no response body
- **400 Bad Request**: Invalid request data or parameters
- **401 Unauthorized**: Authentication required or invalid
- **403 Forbidden**: Access denied for current user/tenant
- **404 Not Found**: Resource does not exist or access denied
- **409 Conflict**: Resource already exists or constraint violation
- **422 Unprocessable Entity**: Validation errors
- **429 Too Many Requests**: Rate limit exceeded
- **500 Internal Server Error**: Server error occurred

### Validation Errors
```json
{
    "error": "validation_failed",
    "message": "One or more validation errors occurred",
    "errors": {
        "name": ["Device name is required"],
        "ip": ["Invalid IP address format"]
    }
}
```

## Rate Limiting

API endpoints are rate limited per user/agent:

- **Web Users**: 1000 requests per hour per user
- **Agents**: 10000 requests per hour per API key
- **Authentication**: 10 attempts per 15 minutes per IP

Rate limit headers are included in responses:
```http
X-RateLimit-Limit: 1000
X-RateLimit-Remaining: 999
X-RateLimit-Reset: 1640995200
```

## Webhooks (Future Enhancement)

### Register Webhook
```http
POST /api/webhooks
Content-Type: application/json

{
    "url": "https://your-server.com/webhook",
    "events": ["device.status_changed", "device.offline"],
    "secret": "webhook-secret-for-verification"
}
```

## OpenAPI/Swagger Documentation

Interactive API documentation is available at:
- **Development**: `https://localhost:5001/swagger`
- **Production**: `https://your-production-domain.com/swagger`

The Swagger interface allows you to:
- Explore all available endpoints
- Test API calls directly from the browser
- Download OpenAPI specification
- Generate client SDKs