# JWT Authentication Implementation

This document describes the JWT authentication implementation for secure communication between ProdControlAV Agents and the API.

## Overview

The JWT authentication flow replaces static agent keys with time-limited Bearer tokens, providing enhanced security and token rotation capabilities.

## API Changes

### New Authentication Endpoint

**POST /api/agents/auth**

Authenticates an agent using its agent key and returns a JWT token.

```http
POST /api/agents/auth
Content-Type: application/json

{
    "agentKey": "your-agent-key-here"
}
```

Response:
```http
HTTP/1.1 200 OK
Content-Type: application/json

{
    "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "expiresAt": "2023-12-01T14:30:00.000Z",
    "tokenType": "Bearer"
}
```

### Updated Agent Endpoints

All agent endpoints now require JWT Bearer authentication:

- **GET /api/agents/devices** - Returns devices for the agent's tenant
- **POST /api/agents/status** - Upload device status readings
- **POST /api/agents/commands/next** - Poll for pending commands
- **POST /api/agents/commands/complete** - Mark commands as completed

### JWT Token Format

The JWT contains the following claims:

- `sub`: Agent ID
- `tenantId`: Agent's tenant ID
- `agentName`: Agent display name
- `iat`: Issued at timestamp
- `exp`: Expiry timestamp (30 minutes from issue)
- `jti`: Unique token ID

## Agent Changes

### JWT Authentication Service

The agent now includes a `JwtAuthService` that:

- Authenticates with the API on startup using the agent key
- Stores JWT tokens in memory (never persisted to disk)
- Automatically refreshes tokens before expiry (2-minute buffer)
- Handles authentication failures gracefully

### Service Integration

All agent services have been updated:

- **DeviceSource**: Uses JWT for device list retrieval
- **StatusPublisher**: Uses JWT for status uploads  
- **CommandService**: Uses JWT for command polling and completion
- **HeartbeatService**: Still uses agent key for backward compatibility

### Configuration

No configuration changes are required. Agents continue to use their existing agent keys, which are now used to obtain JWT tokens.

## Security Features

### Token Lifecycle
- **30-minute expiry**: Tokens automatically expire after 30 minutes
- **Automatic refresh**: Tokens are refreshed 2 minutes before expiry
- **Memory storage**: Tokens are never written to disk
- **Unique tokens**: Each token contains a unique ID (jti claim)

### Error Handling
- **Graceful degradation**: Authentication failures are logged but don't crash the agent
- **Retry logic**: Failed token refresh attempts are logged and retried
- **Fallback behavior**: Services continue to function with cached data when tokens are unavailable

## Deployment

### API Deployment

The API requires JWT configuration in `appsettings.json`:

```json
{
  "Jwt": {
    "Issuer": "ProdControlAV.API",
    "Audience": "ProdControlAV.Agents", 
    "Key": "your-secret-signing-key-must-be-32-chars-minimum",
    "ExpiryMinutes": 30
  }
}
```

⚠️ **Security Note**: The JWT signing key should be stored securely (Azure Key Vault, etc.) in production.

### Agent Deployment

No changes required. Agents use existing configuration and automatically adopt JWT authentication.

### Backward Compatibility

- Heartbeat endpoint still accepts agent keys for compatibility
- Existing agent keys remain valid and are used to obtain JWT tokens
- No breaking changes to agent configuration

## Testing

### Unit Tests

JWT functionality is covered by:
- `JwtServiceTests` - Token generation and validation
- `CommandServiceTests` - Service integration with JWT auth
- `DeviceSourceTests` - Device retrieval with JWT auth

### Integration Testing

To test the complete flow:

1. Start the API with valid JWT configuration
2. Start an agent with a valid agent key
3. Verify the agent successfully obtains JWT tokens
4. Confirm all agent API calls use Bearer authentication
5. Test token refresh behavior over time

## Troubleshooting

### Common Issues

**Agent fails to authenticate**
- Check agent key is valid in database
- Verify API JWT configuration is correct
- Check network connectivity between agent and API

**Token refresh failures**
- Check agent logs for JWT authentication errors
- Verify API is accessible from agent
- Confirm JWT signing key hasn't changed

**API returns 401 Unauthorized**
- Verify token hasn't expired
- Check JWT configuration matches between API instances
- Confirm clock synchronization between agent and API hosts
- Ensure global middleware checks for both `tenant_id` (cookie auth) and `tenantId` (JWT auth) claims

### Logging

Enable debug logging to see JWT operations:

**Agent logs:**
- JWT token refresh attempts and results
- Authentication failures with error details
- Token expiry warnings

**API logs:**
- JWT token validation results
- Authentication endpoint usage
- Failed authentication attempts

## Security Considerations

- JWT signing keys should be rotated regularly
- Monitor for unusual authentication patterns
- Implement rate limiting on the authentication endpoint
- Use HTTPS for all agent-API communication
- Consider shorter token lifetimes for high-security environments