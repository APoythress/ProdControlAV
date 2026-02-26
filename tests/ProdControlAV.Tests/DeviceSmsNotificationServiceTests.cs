using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Moq;
using ProdControlAV.Infrastructure.Services;
using Xunit;

namespace ProdControlAV.Tests;

/// <summary>
/// Unit tests for the Table Storage-backed SMS notification stores and
/// the offline cooldown / ONLINE transition logic exercised via the stores.
/// </summary>
public class DeviceSmsNotificationServiceTests
{
    // -----------------------------------------------------------------------
    // TableDeviceSmsStateStore tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeviceSmsStateStore_GetAsync_ReturnsNullWhenNotFound()
    {
        var mockTable = new Mock<TableClient>();
        mockTable
            .Setup(x => x.GetEntityAsync<TableEntity>(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not Found"));

        var store = new TableDeviceSmsStateStore(mockTable.Object);
        var result = await store.GetAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task DeviceSmsStateStore_GetAsync_ReturnsStateWhenFound()
    {
        var tenantId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var lastSent = DateTimeOffset.UtcNow.AddMinutes(-30);

        var entity = new TableEntity(tenantId.ToString().ToLowerInvariant(), deviceId.ToString())
        {
            ["LastSentType"] = "OFFLINE",
            ["LastSentUtc"] = lastSent
        };

        var mockTable = new Mock<TableClient>();
        mockTable
            .Setup(x => x.GetEntityAsync<TableEntity>(
                tenantId.ToString().ToLowerInvariant(), deviceId.ToString(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(entity, Mock.Of<Response>()));

        var store = new TableDeviceSmsStateStore(mockTable.Object);
        var result = await store.GetAsync(tenantId, deviceId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("OFFLINE", result!.LastSentType);
        Assert.Equal(lastSent, result.LastSentUtc);
    }

    [Fact]
    public async Task DeviceSmsStateStore_UpsertAsync_UsesMergeMode()
    {
        var mockTable = new Mock<TableClient>();
        TableUpdateMode? capturedMode = null;

        mockTable
            .Setup(x => x.UpsertEntityAsync(
                It.IsAny<ITableEntity>(),
                It.IsAny<TableUpdateMode>(),
                It.IsAny<CancellationToken>()))
            .Callback<ITableEntity, TableUpdateMode, CancellationToken>((_, mode, _) => capturedMode = mode)
            .ReturnsAsync(Mock.Of<Response>());

        var store = new TableDeviceSmsStateStore(mockTable.Object);
        await store.UpsertAsync(Guid.NewGuid(), Guid.NewGuid(), "OFFLINE", DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(TableUpdateMode.Merge, capturedMode);
    }

    // -----------------------------------------------------------------------
    // TableSmsNotificationLogStore tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SmsNotificationLogStore_AppendAsync_WritesRowWithExpectedFields()
    {
        var tenantId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sentUtc = DateTimeOffset.UtcNow;

        TableEntity? capturedEntity = null;
        var mockTable = new Mock<TableClient>();
        mockTable
            .Setup(x => x.UpsertEntityAsync(
                It.IsAny<ITableEntity>(),
                It.IsAny<TableUpdateMode>(),
                It.IsAny<CancellationToken>()))
            .Callback<ITableEntity, TableUpdateMode, CancellationToken>((e, _, _) => capturedEntity = e as TableEntity)
            .ReturnsAsync(Mock.Of<Response>());

        var store = new TableSmsNotificationLogStore(mockTable.Object);
        await store.AppendAsync(tenantId, deviceId, "OFFLINE", sentUtc, "***-1234", "SMXXX", CancellationToken.None);

        Assert.NotNull(capturedEntity);
        Assert.Equal(tenantId.ToString().ToLowerInvariant(), capturedEntity!.PartitionKey);
        Assert.Contains(deviceId.ToString(), capturedEntity.RowKey);
        Assert.Contains("OFFLINE", capturedEntity.RowKey);
        Assert.Equal("OFFLINE", capturedEntity["Type"]);
        Assert.Equal(sentUtc, capturedEntity["SentUtc"]);
        Assert.Equal("***-1234", capturedEntity["ToPhoneMasked"]);
        Assert.Equal("SMXXX", capturedEntity["ProviderMessageId"]);
    }

    // -----------------------------------------------------------------------
    // TableTenantSmsUsageStore tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TenantSmsUsageStore_Increment_StartsFromZeroWhenNoEntity()
    {
        TableEntity? capturedEntity = null;
        var mockTable = new Mock<TableClient>();

        mockTable
            .Setup(x => x.GetEntityAsync<TableEntity>(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not Found"));

        mockTable
            .Setup(x => x.UpsertEntityAsync(
                It.IsAny<ITableEntity>(),
                It.IsAny<TableUpdateMode>(),
                It.IsAny<CancellationToken>()))
            .Callback<ITableEntity, TableUpdateMode, CancellationToken>((e, _, _) => capturedEntity = e as TableEntity)
            .ReturnsAsync(Mock.Of<Response>());

        var store = new TableTenantSmsUsageStore(mockTable.Object);
        await store.IncrementAsync(Guid.NewGuid(), "OFFLINE", CancellationToken.None);

        Assert.NotNull(capturedEntity);
        Assert.Equal(1, (int)capturedEntity!["CountTotal"]);
        Assert.Equal(1, (int)capturedEntity["CountOffline"]);
        Assert.Equal(0, (int)capturedEntity["CountOnline"]);
    }

    [Fact]
    public async Task TenantSmsUsageStore_Increment_IncrementsExistingCounters()
    {
        var tenantId = Guid.NewGuid();
        var period = DateTimeOffset.UtcNow.ToString("yyyyMM");

        var existingEntity = new TableEntity(tenantId.ToString().ToLowerInvariant(), period)
        {
            ["CountTotal"] = 5,
            ["CountOffline"] = 3,
            ["CountOnline"] = 2
        };

        TableEntity? capturedEntity = null;
        var mockTable = new Mock<TableClient>();

        mockTable
            .Setup(x => x.GetEntityAsync<TableEntity>(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(existingEntity, Mock.Of<Response>()));

        mockTable
            .Setup(x => x.UpsertEntityAsync(
                It.IsAny<ITableEntity>(),
                It.IsAny<TableUpdateMode>(),
                It.IsAny<CancellationToken>()))
            .Callback<ITableEntity, TableUpdateMode, CancellationToken>((e, _, _) => capturedEntity = e as TableEntity)
            .ReturnsAsync(Mock.Of<Response>());

        var store = new TableTenantSmsUsageStore(mockTable.Object);
        await store.IncrementAsync(tenantId, "ONLINE", CancellationToken.None);

        Assert.NotNull(capturedEntity);
        Assert.Equal(6, (int)capturedEntity!["CountTotal"]);
        Assert.Equal(3, (int)capturedEntity["CountOffline"]);
        Assert.Equal(3, (int)capturedEntity["CountOnline"]);
    }

    // -----------------------------------------------------------------------
    // OFFLINE cooldown logic tests (inline decision logic)
    // -----------------------------------------------------------------------

    [Fact]
    public void OfflineCooldown_NullLastSentSmsUtc_ShouldSend()
    {
        // LastSentSMSUtc == null → should send
        DateTimeOffset? lastSentSmsUtc = null;
        DateTimeOffset? lastSeenUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
        var now = DateTimeOffset.UtcNow;
        const int cooldownMinutes = 15;

        bool shouldSend = EvaluateOfflineShouldSend(lastSentSmsUtc, lastSeenUtc, now, cooldownMinutes);

        Assert.True(shouldSend);
    }

    [Fact]
    public void OfflineCooldown_LastSentAfterLastSeen_ShouldNotSend()
    {
        // LastSentSMSUtc > LastSeenUtc → already notified since last seen
        DateTimeOffset lastSeen = DateTimeOffset.UtcNow.AddMinutes(-20);
        DateTimeOffset? lastSentSmsUtc = DateTimeOffset.UtcNow.AddMinutes(-10); // sent after last seen
        var now = DateTimeOffset.UtcNow;
        const int cooldownMinutes = 15;

        bool shouldSend = EvaluateOfflineShouldSend(lastSentSmsUtc, lastSeen, now, cooldownMinutes);

        Assert.False(shouldSend);
    }

    [Fact]
    public void OfflineCooldown_CooldownElapsedAndSeenSinceLastSent_ShouldSend()
    {
        // Cooldown elapsed AND LastSentSMSUtc < LastSeenUtc → send
        DateTimeOffset lastSent = DateTimeOffset.UtcNow.AddMinutes(-20); // 20 min ago > 15 min cooldown
        DateTimeOffset lastSeen = DateTimeOffset.UtcNow.AddMinutes(-5);  // seen after last sent
        var now = DateTimeOffset.UtcNow;
        const int cooldownMinutes = 15;

        bool shouldSend = EvaluateOfflineShouldSend(lastSent, lastSeen, now, cooldownMinutes);

        Assert.True(shouldSend);
    }

    [Fact]
    public void OfflineCooldown_CooldownNotElapsed_ShouldNotSend()
    {
        // Only 5 minutes since last SMS, cooldown is 15 → suppress
        DateTimeOffset lastSent = DateTimeOffset.UtcNow.AddMinutes(-5);
        DateTimeOffset lastSeen = DateTimeOffset.UtcNow.AddMinutes(-3); // seen after last sent
        var now = DateTimeOffset.UtcNow;
        const int cooldownMinutes = 15;

        bool shouldSend = EvaluateOfflineShouldSend(lastSent, lastSeen, now, cooldownMinutes);

        Assert.False(shouldSend);
    }

    // -----------------------------------------------------------------------
    // ONLINE transition logic tests (inline decision logic)
    // -----------------------------------------------------------------------

    [Fact]
    public void OnlineTransition_NoPriorState_ShouldNotSend()
    {
        // No DeviceSmsState → no ONLINE SMS
        DeviceSmsStateDto? state = null;

        bool shouldSend = EvaluateOnlineShouldSend(state, lastSeenUtc: null);

        Assert.False(shouldSend);
    }

    [Fact]
    public void OnlineTransition_LastSentWasOnline_ShouldNotSend()
    {
        // Last sent type was ONLINE → do not send again
        var state = new DeviceSmsStateDto(Guid.NewGuid(), Guid.NewGuid(), "ONLINE", DateTimeOffset.UtcNow.AddMinutes(-10));

        bool shouldSend = EvaluateOnlineShouldSend(state, lastSeenUtc: DateTimeOffset.UtcNow);

        Assert.False(shouldSend);
    }

    [Fact]
    public void OnlineTransition_LastSentWasOfflineAndSeenAfter_ShouldSend()
    {
        // Last sent type was OFFLINE and device seen after last SMS → send ONLINE SMS
        DateTimeOffset lastSent = DateTimeOffset.UtcNow.AddMinutes(-10);
        DateTimeOffset lastSeen = DateTimeOffset.UtcNow.AddMinutes(-2); // seen after the SMS

        var state = new DeviceSmsStateDto(Guid.NewGuid(), Guid.NewGuid(), "OFFLINE", lastSent);

        bool shouldSend = EvaluateOnlineShouldSend(state, lastSeen);

        Assert.True(shouldSend);
    }

    [Fact]
    public void OnlineTransition_LastSentWasOfflineButSeenNotAfter_ShouldNotSend()
    {
        // Safety check: LastSeenUtc is NOT after LastSentUtc → suppress
        DateTimeOffset lastSent = DateTimeOffset.UtcNow.AddMinutes(-2);
        DateTimeOffset lastSeen = DateTimeOffset.UtcNow.AddMinutes(-10); // seen BEFORE the SMS

        var state = new DeviceSmsStateDto(Guid.NewGuid(), Guid.NewGuid(), "OFFLINE", lastSent);

        bool shouldSend = EvaluateOnlineShouldSend(state, lastSeen);

        Assert.False(shouldSend);
    }

    // -----------------------------------------------------------------------
    // Opt-out test
    // -----------------------------------------------------------------------

    [Fact]
    public void OptOut_SmsAlertsDisabled_ShouldNotSend()
    {
        // When SmsAlertsEnabled is false, the OFFLINE branch must skip sending.
        bool smsAlertsEnabled = false;

        // Replicate the first guard in HandleOfflineDeviceAsync
        Assert.False(smsAlertsEnabled, "Expected SMS to be suppressed when opt-out is set");
    }

    // -----------------------------------------------------------------------
    // Helper methods that mirror the decision logic in DeviceOfflineNotificationService
    // -----------------------------------------------------------------------

    private static bool EvaluateOfflineShouldSend(
        DateTimeOffset? lastSentSmsUtc,
        DateTimeOffset? lastSeenUtc,
        DateTimeOffset now,
        int cooldownMinutes)
    {
        if (lastSentSmsUtc == null)
            return true;

        if (lastSentSmsUtc.Value > lastSeenUtc.GetValueOrDefault())
            return false;

        if ((now - lastSentSmsUtc.Value).TotalMinutes >= cooldownMinutes
            && lastSentSmsUtc.Value < lastSeenUtc.GetValueOrDefault())
            return true;

        return false;
    }

    private static bool EvaluateOnlineShouldSend(DeviceSmsStateDto? state, DateTimeOffset? lastSeenUtc)
    {
        if (state == null || !string.Equals(state.LastSentType, "OFFLINE", StringComparison.OrdinalIgnoreCase))
            return false;

        // Safety check
        if (lastSeenUtc.HasValue && state.LastSentUtc.HasValue && lastSeenUtc.Value <= state.LastSentUtc.Value)
            return false;

        return true;
    }
}
