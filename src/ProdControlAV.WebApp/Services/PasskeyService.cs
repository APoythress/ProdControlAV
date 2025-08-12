using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace ProdControlAV.WebApp.Services;

public sealed class PasskeyService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private readonly HttpClient _http;
    private IJSObjectReference? _module;

    public PasskeyService(IJSRuntime js, HttpClient http)
    {
        _js = js;
        _http = http;
    }

    private async Task<IJSObjectReference> ModuleAsync()
        => _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "/js/passkeys.js");

    // Returns true on success; throws or returns false on failure
    public async Task<bool> RegisterAsync(string email, string? displayName, CancellationToken ct = default)
    {
        var beginReq = new { Email = email, DisplayName = displayName };
        var beginResp = await _http.PostAsJsonAsync("/auth/passkeys/register/options", beginReq, cancellationToken: ct);
        if (!beginResp.IsSuccessStatusCode) return false;

        var options = await beginResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var module = await ModuleAsync();
        var attestation = await module.InvokeAsync<JsonElement>("createCredential", ct, options);

        var completeReq = new
        {
            Email = email,
            AttestationResponse = attestation
        };
        var completeResp = await _http.PostAsJsonAsync("/auth/passkeys/register", completeReq, cancellationToken: ct);
        return completeResp.IsSuccessStatusCode;
    }

    public async Task<bool> AuthenticateAsync(string email, CancellationToken ct = default)
    {
        var beginReq = new { Email = email };
        var beginResp = await _http.PostAsJsonAsync("/auth/passkeys/assertion/options", beginReq, cancellationToken: ct);
        if (!beginResp.IsSuccessStatusCode) return false;

        var options = await beginResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var module = await ModuleAsync();
        var assertion = await module.InvokeAsync<JsonElement>("getAssertion", ct, options);

        var completeReq = new
        {
            Email = email,
            AssertionResponse = assertion
        };
        var completeResp = await _http.PostAsJsonAsync("/auth/passkeys/assertion", completeReq, cancellationToken: ct);
        return completeResp.IsSuccessStatusCode;
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            await _module.DisposeAsync();
        }
    }
}
