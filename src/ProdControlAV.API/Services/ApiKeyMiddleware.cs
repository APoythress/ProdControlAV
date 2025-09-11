public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _apiKey;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        // Read from a nested section for clarity and security
        _apiKey = config["Api:AgentApiKey"];
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