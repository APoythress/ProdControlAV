using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _apiKey;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        
        // Try configuration first, then environment variable
        _apiKey = config["Api:AgentApiKey"] 
                  ?? Environment.GetEnvironmentVariable("PRODCONTROL_AGENT_APIKEY")
                  ?? throw new InvalidOperationException(
                      "Agent API Key must be provided either in configuration (Api:AgentApiKey) " +
                      "or via environment variable (PRODCONTROL_AGENT_APIKEY)");
        
        // Validate API key format
        if (_apiKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Agent API Key must be at least 32 characters long for security");
        }
    }

    public async Task Invoke(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var providedKey) ||
            providedKey != _apiKey)
        {
            context.Response.StatusCode = 401;
            // Do not reveal any info about the key
            await context.Response.WriteAsync("Unauthorized");
            return;
        }
        await _next(context);
    }
}