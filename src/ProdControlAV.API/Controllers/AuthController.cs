using System.Security.Claims;
using System.Text;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.Memory;
using ProdControlAV.API.Models;

namespace ProdControlAV.API.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IFido2 _fido2;
    private readonly ILogger<AuthController> _logger;
    private readonly IMemoryCache _cache;

    public AuthController(AppDbContext db, IFido2 fido2, ILogger<AuthController> logger, IMemoryCache cache)
    {
        _db = db;
        _fido2 = fido2;
        _logger = logger;
        _cache = cache;
    }

    [HttpGet("login")]
    public IActionResult Login([FromQuery] string? returnUrl = "/")
    {
        return Challenge(new AuthenticationProperties { RedirectUri = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl }, "Apple");
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    // Switch active tenant for current session (must be a member)
    [Authorize]
    [HttpPost("switch-tenant")]
    public async Task<IActionResult> SwitchTenant([FromBody] SwitchTenantRequest req, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var memberships = await _db.UserTenants
            .Where(x => x.UserId.ToString() == userId)
            .Select(x => x.TenantId)
            .ToListAsync(ct);

        if (!memberships.Contains(req.TenantId))
            return Forbid();

        var email = User.FindFirstValue(ClaimTypes.Email) ?? "";
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Email, email),
            new Claim("tenant_ids", string.Join(" ", memberships)),
            new Claim("tenant_id", req.TenantId)
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = true });
        return NoContent();
    }

    public record SwitchTenantRequest(string TenantId);

    // Passkeys (WebAuthn) - Registration
    public record BeginRegisterRequest(string Email, string? DisplayName);
    public record CompleteRegisterRequest(string Email, AuthenticatorAttestationRawResponse AttestationResponse);

    [HttpPost("passkeys/register/options")]
    public async Task<ActionResult<CredentialCreateOptions>> BeginRegister([FromBody] BeginRegisterRequest req, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email, ct);
        if (user is null)
        {
            user = new AppUser { Email = req.Email, DisplayName = req.DisplayName ?? req.Email };
            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);
        }

        var fidoUser = new Fido2User
        {
            Id = Encoding.UTF8.GetBytes(user.Id.ToString()),
            Name = user.Email,
            DisplayName = user.DisplayName ?? user.Email
        };

        var existingCreds = await _db.PasskeyCredentials
            .Where(c => c.UserId == user.Id)
            .Select(c => new PublicKeyCredentialDescriptor(c.DescriptorId))
            .ToListAsync(ct);

        // Keep selection minimal for broad compatibility
        var authSelection = new AuthenticatorSelection
        {
            AuthenticatorAttachment = AuthenticatorAttachment.Platform,
            UserVerification = UserVerificationRequirement.Required
        };

        var exts = new AuthenticationExtensionsClientInputs();

        // Use RequestNewCredential for compatibility across Fido2 versions
        var options = _fido2.RequestNewCredential(fidoUser, existingCreds, authSelection, AttestationConveyancePreference.None, exts);

        // Cache options for completion step (keyed by email)
        _cache.Set($"fido2.register.{user.Email}", options, TimeSpan.FromMinutes(5));

        return Ok(options);
    }

    [HttpPost("passkeys/register")]
    public async Task<IActionResult> CompleteRegister([FromBody] CompleteRegisterRequest req, CancellationToken ct)
    {
        if (!_cache.TryGetValue<CredentialCreateOptions>($"fido2.register.{req.Email}", out var options))
            return BadRequest(new { error = "registration_options_expired" });

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email, ct);
        if (user is null) return BadRequest(new { error = "user_not_found" });

        IsCredentialIdUniqueToUserAsyncDelegate isUnique = async (args, ct2) =>
        {
            var exists = await _db.PasskeyCredentials.AnyAsync(c => c.DescriptorId == args.CredentialId, ct2);
            return !exists;
        };
        
        // TODO - figure out request token binding instead of passing null
        var res = await _fido2.MakeNewCredentialAsync(req.AttestationResponse, options, isUnique, null, ct);

        var cred = new PasskeyCredential
        {
            UserId = user.Id,
            DescriptorId = res.Result.CredentialId,
            PublicKey = res.Result.PublicKey,
            SignatureCounter = res.Result.Counter,
            CredType = res.Result.CredType,
            AaGuid = res.Result.Aaguid
        };

        _db.PasskeyCredentials.Add(cred);
        await _db.SaveChangesAsync(ct);

        return Ok(new { ok = true });
    }

    // Passkeys (WebAuthn) - Authentication
    public record BeginAuthRequest(string Email);
    public record CompleteAuthRequest(AuthenticatorAssertionRawResponse AssertionResponse, string Email);

    [HttpPost("passkeys/assertion/options")]
    public async Task<ActionResult<AssertionOptions>> BeginAuth([FromBody] BeginAuthRequest req, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email, ct);
        if (user is null) return BadRequest(new { error = "user_not_found" });

        var creds = await _db.PasskeyCredentials
            .Where(c => c.UserId == user.Id)
            .Select(c => new PublicKeyCredentialDescriptor(c.DescriptorId))
            .ToListAsync(ct);

        var exts = new AuthenticationExtensionsClientInputs();

        var options = _fido2.GetAssertionOptions(creds, UserVerificationRequirement.Required, exts);

        _cache.Set($"fido2.assertion.{req.Email}", options, TimeSpan.FromMinutes(5));
        return Ok(options);
    }

    [HttpPost("passkeys/assertion")]
    public async Task<IActionResult> CompleteAuth([FromBody] CompleteAuthRequest req, CancellationToken ct)
    {
        if (!_cache.TryGetValue<AssertionOptions>($"fido2.assertion.{req.Email}", out var options))
            return BadRequest(new { error = "assertion_options_expired" });

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email, ct);
        if (user is null) return BadRequest(new { error = "user_not_found" });

        // Load stored credential for asserted credential ID
        var cred = await _db.PasskeyCredentials
            .FirstOrDefaultAsync(x => x.DescriptorId == req.AssertionResponse.Id, ct);

        if (cred is null) return BadRequest(new { error = "credential_not_found" });

        // Delegate indicating user handle ownership; return true for RP-managed users
        IsUserHandleOwnerOfCredentialIdAsync isOwner = (args, token) => Task.FromResult(true);

        // TODO - same here - figure out requestTokenBinding here instead of passing null
        // Verify assertion using stored public key and signature counter
        var result = await _fido2.MakeAssertionAsync(
            req.AssertionResponse,
            options,
            cred.PublicKey,
            cred.SignatureCounter,
            isOwner,
            null,
            ct
        );

        // Update the signature counter if needed
        cred.SignatureCounter = result.Counter;
        await _db.SaveChangesAsync(ct);

        // Successful assertion -> sign-in the user
        var memberships = await _db.UserTenants
            .Where(m => m.UserId == user.Id)
            .Select(m => m.TenantId)
            .ToListAsync(ct);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
        };

        if (memberships.Any())
        {
            claims.Add(new Claim("tenant_ids", string.Join(" ", memberships)));
            claims.Add(new Claim("tenant_id", memberships[0]));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = true });

        return Ok(new { ok = true });
    }
}
