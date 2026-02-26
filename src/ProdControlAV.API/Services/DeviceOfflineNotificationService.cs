using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProdControlAV.Core.Interfaces;
using ProdControlAV.Core.Models;
using ProdControlAV.Infrastructure.Services;

namespace ProdControlAV.API.Services;

/// <summary>
/// Background service that monitors device status by scanning the Devices Azure Table.
/// No SQL DB access is made unless an SMS notification must actually be sent.
/// </summary>
public class DeviceOfflineNotificationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DeviceOfflineNotificationService> _logger;
    private readonly TableServiceClient _tableServiceClient;

    private const int PollingIntervalSeconds = 30;
    private const int OfflineCooldownMinutes = 15;

    public DeviceOfflineNotificationService(
        IServiceProvider serviceProvider,
        ILogger<DeviceOfflineNotificationService> logger,
        TableServiceClient tableServiceClient)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _tableServiceClient = tableServiceClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DeviceOfflineNotificationService started");

        // Allow the app to fully initialise before the first poll.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndNotifyAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DeviceOfflineNotificationService");
            }

            await Task.Delay(TimeSpan.FromSeconds(PollingIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("DeviceOfflineNotificationService stopped");
    }

    // -------------------------------------------------------------------------
    // Table Storage scan (no SQL)
    // -------------------------------------------------------------------------

    private async Task CheckAndNotifyAsync(CancellationToken ct)
    {
        var devicesTable = _tableServiceClient.GetTableClient("Devices");

        // Scan ALL device rows and group by tenant (partition key).
        var byTenant = new Dictionary<string, List<TableEntity>>();
        var query = devicesTable.QueryAsync<TableEntity>(cancellationToken: ct);
        await foreach (var entity in query)
        {
            if (!byTenant.TryGetValue(entity.PartitionKey, out var list))
            {
                list = new List<TableEntity>();
                byTenant[entity.PartitionKey] = list;
            }
            list.Add(entity);
        }

        foreach (var (partitionKey, devices) in byTenant)
        {
            if (!Guid.TryParse(partitionKey, out var tenantId))
                continue;

            foreach (var device in devices)
            {
                if (!Guid.TryParse(device.RowKey, out var deviceId))
                    continue;

                try
                {
                    await ProcessDeviceAsync(tenantId, deviceId, device, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing device {DeviceId} for tenant {TenantId}", deviceId, tenantId);
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Per-device logic (Table Storage only until send branch)
    // -------------------------------------------------------------------------

    private async Task ProcessDeviceAsync(Guid tenantId, Guid deviceId, TableEntity entity, CancellationToken ct)
    {
        var status = entity.ContainsKey("Status") ? Convert.ToString(entity["Status"]) : null;

        DateTimeOffset? lastSeenUtc = ReadDateTimeOffset(entity, "LastSeenUtc");
        DateTimeOffset? lastSentSmsUtc = ReadDateTimeOffset(entity, "LastSentSMSUtc");
        // DateTimeOffset.MinValue is the sentinel meaning "null/cleared"
        if (lastSentSmsUtc == DateTimeOffset.MinValue)
            lastSentSmsUtc = null;

        bool smsAlertsEnabled = true;
        if (entity.TryGetValue("SmsAlertsEnabled", out var smsEnabledVal) && smsEnabledVal != null)
        {
            if (smsEnabledVal is bool b) smsAlertsEnabled = b;
            else
            {
                var s = Convert.ToString(smsEnabledVal);
                if (bool.TryParse(s, out var pb)) smsAlertsEnabled = pb;
                else if (int.TryParse(s, out var pi)) smsAlertsEnabled = pi != 0;
            }
        }

        string deviceName = entity.ContainsKey("Name") ? (Convert.ToString(entity["Name"]) ?? deviceId.ToString()) : deviceId.ToString();

        bool isOnline = string.Equals(status, "ONLINE", StringComparison.OrdinalIgnoreCase);

        if (isOnline)
        {
            await HandleOnlineDeviceAsync(tenantId, deviceId, deviceName, lastSeenUtc, ct);
        }
        else if (string.Equals(status, "OFFLINE", StringComparison.OrdinalIgnoreCase))
        {
            await HandleOfflineDeviceAsync(tenantId, deviceId, deviceName,
                smsAlertsEnabled, lastSeenUtc, lastSentSmsUtc, ct);
        }
        // Unknown/null status – ignore
    }

    // -------------------------------------------------------------------------
    // ONLINE handling
    // -------------------------------------------------------------------------

    private async Task HandleOnlineDeviceAsync(
        Guid tenantId,
        Guid deviceId,
        string deviceName,
        DateTimeOffset? lastSeenUtc,
        CancellationToken ct)
    {
        // Read DeviceSmsState to determine whether an OFFLINE SMS was previously sent.
        var smsStateTable = _tableServiceClient.GetTableClient("DeviceSmsState");
        var smsStateStore = new TableDeviceSmsStateStore(smsStateTable);

        var state = await smsStateStore.GetAsync(tenantId, deviceId, ct);

        // Always clear LastSentSMSUtc on the Devices table when device is ONLINE.
        var devicesTable = _tableServiceClient.GetTableClient("Devices");
        var deviceStore = new TableDeviceStore(devicesTable);
        await deviceStore.UpdateSmsLastSentAsync(tenantId, deviceId, null, ct);

        if (state == null || !string.Equals(state.LastSentType, "OFFLINE", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Device {DeviceId} ({DeviceName}) is ONLINE – no prior OFFLINE notification; suppressing ONLINE SMS",
                deviceId, deviceName);
            return;
        }

        // Optional safety check: device must have been seen after the last SMS was sent.
        if (lastSeenUtc.HasValue && state.LastSentUtc.HasValue && lastSeenUtc.Value <= state.LastSentUtc.Value)
        {
            _logger.LogDebug("Device {DeviceId} ({DeviceName}) ONLINE: LastSeenUtc not after LastSentUtc – suppressing ONLINE SMS",
                deviceId, deviceName);
            return;
        }

        _logger.LogInformation("Device {DeviceId} ({DeviceName}) has come back ONLINE after OFFLINE notification – sending ONLINE SMS",
            deviceId, deviceName);

        var sent = await SendSmsToTenantUsersAsync(tenantId, deviceId, deviceName, "ONLINE", lastSeenUtc, ct);

        if (sent)
        {
            var now = DateTimeOffset.UtcNow;
            // Update DeviceSmsState
            await smsStateStore.UpsertAsync(tenantId, deviceId, "ONLINE", now, ct);

            // Write to SmsNotificationLog
            var logTable = _tableServiceClient.GetTableClient("SmsNotificationLog");
            var logStore = new TableSmsNotificationLogStore(logTable);
            await logStore.AppendAsync(tenantId, deviceId, "ONLINE", now, null, null, ct);

            // Increment TenantSmsUsage
            var usageTable = _tableServiceClient.GetTableClient("TenantSmsUsage");
            var usageStore = new TableTenantSmsUsageStore(usageTable);
            await usageStore.IncrementAsync(tenantId, "ONLINE", ct);
        }
    }

    // -------------------------------------------------------------------------
    // OFFLINE handling
    // -------------------------------------------------------------------------

    private async Task HandleOfflineDeviceAsync(
        Guid tenantId,
        Guid deviceId,
        string deviceName,
        bool smsAlertsEnabled,
        DateTimeOffset? lastSeenUtc,
        DateTimeOffset? lastSentSmsUtc,
        CancellationToken ct)
    {
        if (!smsAlertsEnabled)
        {
            _logger.LogDebug("Device {DeviceId} ({DeviceName}) is OFFLINE but SMS alerts are disabled – opt-out",
                deviceId, deviceName);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        bool shouldSend = false;

        if (lastSentSmsUtc == null)
        {
            // Never sent an SMS for this device – send now.
            _logger.LogInformation("Device {DeviceId} ({DeviceName}) is OFFLINE – sending first notification",
                deviceId, deviceName);
            shouldSend = true;
        }
        else if (lastSentSmsUtc.Value > lastSeenUtc.GetValueOrDefault())
        {
            // Already notified since the device was last seen – cooldown active.
            _logger.LogDebug("Device {DeviceId} ({DeviceName}) OFFLINE – already notified since last seen (LastSentSMSUtc={LastSent}); suppressing",
                deviceId, deviceName, lastSentSmsUtc.Value);
        }
        else if ((now - lastSentSmsUtc.Value).TotalMinutes >= OfflineCooldownMinutes
                 && lastSentSmsUtc.Value < lastSeenUtc.GetValueOrDefault())
        {
            // Cooldown period elapsed and device was seen since last notification.
            _logger.LogInformation("Device {DeviceId} ({DeviceName}) OFFLINE – cooldown elapsed; sending repeat notification",
                deviceId, deviceName);
            shouldSend = true;
        }
        else
        {
            // Cooldown not yet elapsed.
            var remaining = OfflineCooldownMinutes - (now - lastSentSmsUtc.Value).TotalMinutes;
            _logger.LogDebug("Device {DeviceId} ({DeviceName}) OFFLINE – cooldown not met ({Remaining:F0}m remaining); suppressing",
                deviceId, deviceName, remaining);
        }

        if (!shouldSend)
            return;

        var sent = await SendSmsToTenantUsersAsync(tenantId, deviceId, deviceName, "OFFLINE", lastSeenUtc, ct);

        if (sent)
        {
            // Update LastSentSMSUtc in Devices table
            var devicesTable = _tableServiceClient.GetTableClient("Devices");
            var deviceStore = new TableDeviceStore(devicesTable);
            await deviceStore.UpdateSmsLastSentAsync(tenantId, deviceId, now, ct);

            // Update DeviceSmsState
            var smsStateTable = _tableServiceClient.GetTableClient("DeviceSmsState");
            var smsStateStore = new TableDeviceSmsStateStore(smsStateTable);
            await smsStateStore.UpsertAsync(tenantId, deviceId, "OFFLINE", now, ct);

            // Write to SmsNotificationLog
            var logTable = _tableServiceClient.GetTableClient("SmsNotificationLog");
            var logStore = new TableSmsNotificationLogStore(logTable);
            await logStore.AppendAsync(tenantId, deviceId, "OFFLINE", now, null, null, ct);

            // Increment TenantSmsUsage
            var usageTable = _tableServiceClient.GetTableClient("TenantSmsUsage");
            var usageStore = new TableTenantSmsUsageStore(usageTable);
            await usageStore.IncrementAsync(tenantId, "OFFLINE", ct);
        }
    }

    // -------------------------------------------------------------------------
    // SQL access – only called when an SMS must actually be sent
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves the SQL DbContext, looks up Pro-plan users with SMS enabled,
    /// sends the SMS, and returns true if at least one message was dispatched.
    /// </summary>
    private async Task<bool> SendSmsToTenantUsersAsync(
        Guid tenantId,
        Guid deviceId,
        string deviceName,
        string type,
        DateTimeOffset? lastSeenUtc,
        CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var smsService = scope.ServiceProvider.GetRequiredService<ISmsService>();
        var dataProtection = scope.ServiceProvider.GetRequiredService<IDataProtectionService>();

        var usersToNotify = await db.Users
            .Where(u => u.TenantId == tenantId
                && u.SubscriptionPlan == SubscriptionPlan.Pro
                && u.SmsNotificationsEnabled
                && u.PhoneNumber != null)
            .ToListAsync(ct);

        if (usersToNotify.Count == 0)
        {
            _logger.LogDebug("No users to notify for tenant {TenantId} device {DeviceId}", tenantId, deviceId);
            return false;
        }

        string message = string.Equals(type, "ONLINE", StringComparison.OrdinalIgnoreCase)
            ? $"PROD-CONTROL: {deviceName} is back ONLINE"
            : $"PROD-CONTROL: Alert – {deviceName} is OFFLINE! Last seen: {FormatLastSeen(lastSeenUtc)}";

        bool anySent = false;
        foreach (var user in usersToNotify)
        {
            try
            {
                var phoneNumber = dataProtection.Unprotect(user.PhoneNumber!);
                var wasSent = await smsService.SendSmsAsync(phoneNumber, message, ct);
                if (wasSent)
                {
                    _logger.LogInformation("SMS sent to user {UserId} – {Type} notification for device {DeviceName}",
                        user.UserId, type, deviceName);
                    anySent = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send SMS to user {UserId}", user.UserId);
            }
        }

        return anySent;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static DateTimeOffset? ReadDateTimeOffset(TableEntity entity, string key)
    {
        if (!entity.TryGetValue(key, out var v) || v == null)
            return null;
        if (v is DateTimeOffset dto) return dto;
        if (v is DateTime dt) return new DateTimeOffset(dt);
        if (v is string s && DateTimeOffset.TryParse(s, out var p)) return p;
        return null;
    }

    private static string FormatLastSeen(DateTimeOffset? lastSeen)
    {
        if (!lastSeen.HasValue) return "unknown";
        var elapsed = DateTimeOffset.UtcNow - lastSeen.Value;
        if (elapsed.TotalMinutes < 1) return "moments ago";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
        return lastSeen.Value.ToString("MMM dd HH:mm UTC");
    }
}
