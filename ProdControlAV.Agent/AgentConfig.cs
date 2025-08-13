public sealed class AgentConfig
{
    public string ApiBase { get; set; } = "https://your-api.example.com"; // HTTPS only
    public Guid? TenantId { get; set; }                  // central-DB tenant
    public string AgentKey { get; set; } = string.Empty;                   // one-time issued, stored securely
    public int IntervalSeconds { get; set; } = 15;                         // main loop cadence
}