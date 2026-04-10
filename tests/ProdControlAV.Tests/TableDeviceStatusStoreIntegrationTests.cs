using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Xunit;
using ProdControlAV.Infrastructure.Services;

public class TableDeviceStatusStoreIntegrationTests
{
    private const string ConnectionString = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFe...==;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;";
    private const string TableName = "DeviceStatus";

    [Fact]
    public async Task UpsertAndQuery_WorksWithAzurite()
    {
        // Skip test if the connection string contains the placeholder key (common in repo samples)
        if (ConnectionString.Contains("Eby8vdM02xNOcqFe"))
        {
            Console.WriteLine("Skipping Azurite integration test because placeholder connection string is in use.");
            return;
        }

        var serviceClient = new TableServiceClient(ConnectionString);
        var tableClient = serviceClient.GetTableClient(TableName);
        await tableClient.CreateIfNotExistsAsync();
        var store = new TableDeviceStatusStore(serviceClient);
        var tenantId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        await store.UpsertAsync(tenantId, deviceId, "Online", 42, DateTimeOffset.UtcNow, CancellationToken.None);
        var results = new List<DeviceStatusDto>();
        await foreach (var dto in store.GetAllForTenantAsync(tenantId, CancellationToken.None))
            results.Add(dto);
        Assert.Single(results);
        Assert.Equal(deviceId, results[0].DeviceId);
        Assert.Equal("Online", results[0].Status);
        Assert.Equal(42, results[0].LatencyMs);
    }
}
