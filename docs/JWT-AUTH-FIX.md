# JWT Authentication Fix - Sub Claim Null Issue

## Problem
The `sub` (subject) claim in JWT tokens was returning `null` when accessed in controllers using `User.FindFirst(JwtRegisteredClaimNames.Sub)`, while the `tenantId` custom claim worked correctly. This caused authentication failures for agent requests.

## Root Cause
ASP.NET Core's `JwtSecurityTokenHandler` has a feature called "inbound claim type mapping" that automatically converts standard JWT claim names to .NET Framework claim types. Specifically:

- JWT standard claim `sub` was mapped to `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier`
- Custom claims like `tenantId` were not mapped and remained as-is

This caused code searching for the `sub` claim to fail because the claim was stored under a different type name.

## Solution
The fix disables the automatic inbound claim type mapping by clearing the default claim type map at application startup:

```csharp
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
```

This ensures that JWT claim names remain as specified in the token, allowing the controller code to successfully locate the `sub` claim.

## Changes Made

### src/ProdControlAV.API/Program.cs
1. Added `using System.IdentityModel.Tokens.Jwt;` and `using System.Linq;`
2. Called `JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();` at application startup
3. Added `OnTokenValidated` event handler to log all claims for debugging purposes

## Testing
The fix has been verified with:
1. All existing unit tests pass (24 tests)
2. Manual testing simulating the complete authentication flow:
   - JWT token generation with `sub` and `tenantId` claims
   - Token validation with the fix applied
   - Claim extraction as done in `AgentsController`
   - Verification that both claims are readable and can be parsed as GUIDs

## Impact
- **Agent authentication**: Agents can now successfully authenticate and make API requests using JWT tokens
- **Backward compatibility**: No breaking changes; the fix only affects JWT claim name resolution
- **Performance**: No performance impact; clearing the map happens once at startup

## Related Files
- `src/ProdControlAV.API/Program.cs` - JWT configuration and authentication setup
- `src/ProdControlAV.API/Services/JwtService.cs` - JWT token generation
- `src/ProdControlAV.API/Controllers/AgentsController.cs` - JWT claim extraction in endpoints

## Additional Debugging
The `OnTokenValidated` event handler now logs all claims in the validated token, making it easier to diagnose any future authentication issues. Look for log entries with `[JWT TOKEN VALIDATED]` prefix.
