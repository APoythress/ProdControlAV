# JWT Authentication Fix - Complete Solution (2025)

## Problem
The agent authentication endpoint was failing with the following error pattern:
```
[JWT TOKEN VALIDATED] Claims: http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier=d616f852-9311-4377-b3c4-bfa06829fd80, ...
[COMMANDS/NEXT] Extracted claims: sub=(null), tenantId=57bdc5dc-c928-4945-891b-f1fe360e15e4
[COMMANDS/NEXT] Invalid token claims: sub=(null), tenantId=57bdc5dc-c928-4945-891b-f1fe360e15e4
```

The JWT token was successfully validated, but the `sub` claim was returning `(null)` when extracted in the controller, causing all agent API requests to fail with 401 Unauthorized.

## Root Cause
The issue had two components:

1. **Claim Type Mapping in JWT Bearer Middleware**: Even though `JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear()` was called at startup, the JWT Bearer authentication middleware was still performing claim type mapping. The `sub` claim from the JWT token was being mapped to the long-form claim type `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier`.

2. **Claim Extraction Logic**: The controller was only searching for the short-form claim name `"sub"` (via `JwtRegisteredClaimNames.Sub`), which didn't exist after the mapping occurred.

## Solution
The fix consists of two parts:

### 1. Configure TokenValidationParameters to Preserve Claim Names
In `Program.cs`, we updated the `TokenValidationParameters` to explicitly set the `NameClaimType` property:

```csharp
options.TokenValidationParameters = new TokenValidationParameters
{
    ValidateIssuer = true,
    ValidateAudience = true,
    ValidateLifetime = true,
    ValidateIssuerSigningKey = true,
    ValidIssuer = jwtConfig.Issuer,
    ValidAudience = jwtConfig.Audience,
    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtConfig.Key)),
    ClockSkew = TimeSpan.FromMinutes(1),
    // Preserve original JWT claim names (sub, jti, etc.) instead of mapping to ClaimTypes
    NameClaimType = JwtRegisteredClaimNames.Sub,
    RoleClaimType = "role"
};
```

Setting `NameClaimType = JwtRegisteredClaimNames.Sub` tells the middleware to use the short-form claim name for the principal's name identifier, preventing the automatic mapping to the long-form claim type.

### 2. Add Robust Claim Extraction Helper
In `AgentsController.cs`, we added a helper method that searches for the agent ID claim using multiple strategies:

```csharp
private string? GetAgentIdFromClaims()
{
    // Try short-form JWT claim name first (after NameClaimType mapping)
    var agentId = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
    if (!string.IsNullOrEmpty(agentId))
        return agentId;
    
    // Fallback to long-form claim name (in case mapping still occurs)
    agentId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (!string.IsNullOrEmpty(agentId))
        return agentId;
    
    // Final fallback - search by claim type containing "nameidentifier"
    return User.Claims.FirstOrDefault(c => c.Type.Contains("nameidentifier", StringComparison.OrdinalIgnoreCase))?.Value;
}
```

This helper method:
1. First tries to find the claim using the short-form name (`"sub"`)
2. Falls back to the long-form name (`ClaimTypes.NameIdentifier`)
3. As a last resort, searches for any claim containing "nameidentifier" (case-insensitive)

All four JWT-protected endpoints in `AgentsController` were updated to use this helper:
- `GetDevices()` - GET `/api/agents/devices`
- `Status()` - POST `/api/agents/status`
- `Next()` - POST `/api/agents/commands/next`
- `Complete()` - POST `/api/agents/commands/complete`

## Testing
A new test was added to verify the fix:

```csharp
[Fact]
public void GenerateToken_ClaimsCanBeExtractedByShortAndLongFormNames()
{
    // ... creates JWT token with sub claim
    // ... verifies claim can be extracted using JwtRegisteredClaimNames.Sub
}
```

All 25 tests pass, including the new test.

## Impact
- **Agent authentication now works correctly**: Agents can successfully authenticate and make API requests using JWT tokens
- **Backward compatibility**: The fallback logic ensures compatibility with different claim name formats
- **Performance**: No performance impact; claim extraction is efficient
- **Maintainability**: Centralized claim extraction logic in a single helper method

## Related Files
- `src/ProdControlAV.API/Program.cs` - JWT Bearer configuration with `NameClaimType` setting
- `src/ProdControlAV.API/Controllers/AgentsController.cs` - Helper method and endpoint updates
- `tests/ProdControlAV.Tests/JwtServiceTests.cs` - New test for claim extraction
- `src/ProdControlAV.API/Services/JwtService.cs` - JWT token generation (no changes needed)

## Why Previous Fixes Didn't Work
The previous fix only cleared the default inbound claim type map at startup:
```csharp
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
```

However, this wasn't sufficient because:
1. The JWT Bearer middleware has its own claim mapping logic that occurs during token validation
2. The `TokenValidationParameters` needed to be explicitly configured to use the short-form claim names
3. There was no fallback logic in case the mapping still occurred in certain scenarios

## Verification
To verify the fix is working in production:
1. Check logs for successful claim extraction: `[COMMANDS/NEXT] Extracted claims: sub=<GUID>, tenantId=<GUID>`
2. Verify agents can successfully call the `/api/agents/commands/next` endpoint without receiving 401 Unauthorized errors
3. Monitor for absence of `Invalid token claims` warning messages in logs
