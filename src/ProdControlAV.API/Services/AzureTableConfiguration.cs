using Azure.Data.Tables;

/// <summary>
/// Self-created hosted service to ensure Azure Table Storage tables exist
/// - IF they don't exist, it creates them. If they do exist, it does nothing.
/// - This is a simple way to ensure tables are created at app startup without needing separate deployment
/// </summary>
public sealed class AzureTableConfiguration : IHostedService
{
    private readonly TableServiceClient _svc;
    private readonly ILogger<AzureTableConfiguration> _logger;

    public AzureTableConfiguration(TableServiceClient svc, ILogger<AzureTableConfiguration> logger)
    {
        _svc = svc;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        // List all tables in alphabetical order:
        await EnsureAsync("AgentAuth", ct);
        await EnsureAsync("AtemState", ct);
        await EnsureAsync("CommandHistory", ct);
        await EnsureAsync("CommandQueue", ct);
        await EnsureAsync("Devices", ct);
        await EnsureAsync("DeviceActions", ct);
        await EnsureAsync("DeviceSmsState", ct);
        await EnsureAsync("DeviceStatus", ct);
        await EnsureAsync("SmsNotificationLog", ct);
        await EnsureAsync("TenantSmsUsage", ct);
        
    }

    private async Task EnsureAsync(string tableName, CancellationToken ct)
    {
        try
        {
            var table = _svc.GetTableClient(tableName);
            await table.CreateIfNotExistsAsync(ct);
            _logger.LogInformation("Ensured Azure Table exists: {Table}", tableName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to ensure Azure Table exists: {Table}", tableName);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}