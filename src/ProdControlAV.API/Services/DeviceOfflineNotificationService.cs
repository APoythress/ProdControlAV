using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProdControlAV.Core.Interfaces;
using ProdControlAV.Core.Models;
using ProdControlAV.Infrastructure.Services;

namespace ProdControlAV.API.Services;

/// <summary>
/// Background service that monitors device status changes and sends SMS notifications
/// when devices go offline for users on Pro plan who have opted in
/// </summary>
public class DeviceOfflineNotificationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DeviceOfflineNotificationService> _logger;
    
    // Track device status to detect transitions from online to offline
    private readonly ConcurrentDictionary<Guid, bool> _lastKnownStatus = new();
    
    // Rate limiting: track last notification time per device to prevent spam
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastNotificationTime = new();
    
    private const int PollingIntervalSeconds = 30; // Check every 30 seconds
    private const int MinNotificationIntervalMinutes = 60; // Don't spam - wait at least 1 hour between notifications for same device

    public DeviceOfflineNotificationService(
        IServiceProvider serviceProvider,
        ILogger<DeviceOfflineNotificationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DeviceOfflineNotificationService started");

        // Wait a bit for system to initialize
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckDeviceStatusAndNotifyAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking device status for notifications");
            }

            await Task.Delay(TimeSpan.FromSeconds(PollingIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("DeviceOfflineNotificationService stopped");
    }

    private async Task CheckDeviceStatusAndNotifyAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var deviceStatusStore = scope.ServiceProvider.GetRequiredService<IDeviceStatusStore>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var smsService = scope.ServiceProvider.GetRequiredService<ISmsService>();
        var dataProtection = scope.ServiceProvider.GetRequiredService<IDataProtectionService>();

        // Get all tenants (we need to check devices across all tenants)
        var tenants = await db.Tenants.Select(t => t.TenantId).ToListAsync(ct);

        foreach (var tenantId in tenants)
        {
            try
            {
                await CheckTenantDevicesAsync(tenantId, deviceStatusStore, db, smsService, dataProtection, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking devices for tenant {TenantId}", tenantId);
            }
        }
    }

    private async Task CheckTenantDevicesAsync(
        Guid tenantId,
        IDeviceStatusStore deviceStatusStore,
        AppDbContext db,
        ISmsService smsService,
        IDataProtectionService dataProtection,
        CancellationToken ct)
    {
        // Get current device status from Table Storage
        var deviceStatuses = new List<DeviceStatusDto>();
        await foreach (var status in deviceStatusStore.GetAllForTenantAsync(tenantId, ct))
        {
            deviceStatuses.Add(status);
        }

        if (deviceStatuses.Count == 0)
        {
            return;
        }

        // Get device names from SQL (we need names for the notification message)
        var deviceIds = deviceStatuses.Select(d => d.DeviceId).ToList();
        var devices = await db.Devices
            .Where(d => d.TenantId == tenantId && deviceIds.Contains(d.Id))
            .Select(d => new { d.Id, d.Name })
            .ToListAsync(ct);

        var deviceNameMap = devices.ToDictionary(d => d.Id, d => d.Name);

        // Check each device for offline transitions
        foreach (var deviceStatus in deviceStatuses)
        {
            await CheckDeviceAndNotifyAsync(
                tenantId,
                deviceStatus,
                deviceNameMap.GetValueOrDefault(deviceStatus.DeviceId, "Unknown Device"),
                db,
                smsService,
                dataProtection,
                ct);
        }
    }

    private async Task CheckDeviceAndNotifyAsync(
        Guid tenantId,
        DeviceStatusDto deviceStatus,
        string deviceName,
        AppDbContext db,
        ISmsService smsService,
        IDataProtectionService dataProtection,
        CancellationToken ct)
    {
        var deviceId = deviceStatus.DeviceId;
        var isOnline = deviceStatus.Status.Equals("online", StringComparison.OrdinalIgnoreCase);

        // Check if this is a transition from online to offline
        var wasOnline = _lastKnownStatus.GetOrAdd(deviceId, isOnline);
        _lastKnownStatus[deviceId] = isOnline;

        // Only notify on transition from online to offline
        if (wasOnline && !isOnline)
        {
            _logger.LogInformation("Device {DeviceId} ({DeviceName}) went offline. Checking for notification subscribers.", 
                deviceId, deviceName);

            // Check rate limiting - don't send notifications too frequently for the same device
            if (_lastNotificationTime.TryGetValue(deviceId, out var lastNotification))
            {
                var timeSinceLastNotification = DateTimeOffset.UtcNow - lastNotification;
                if (timeSinceLastNotification.TotalMinutes < MinNotificationIntervalMinutes)
                {
                    _logger.LogDebug("Skipping notification for device {DeviceId} - notified {Minutes} minutes ago",
                        deviceId, (int)timeSinceLastNotification.TotalMinutes);
                    return;
                }
            }

            // Get all users for this tenant who are on Pro plan and have SMS enabled
            var usersToNotify = await db.Users
                .Where(u => u.TenantId == tenantId 
                    && u.SubscriptionPlan == SubscriptionPlan.Pro
                    && u.SmsNotificationsEnabled
                    && u.PhoneNumber != null)
                .ToListAsync(ct);

            if (usersToNotify.Count == 0)
            {
                _logger.LogDebug("No users to notify for tenant {TenantId}", tenantId);
                return;
            }

            // Send SMS to each user
            var lastSeenTime = deviceStatus.LastSeenUtc;
            var message = $"PROD-CONTROL: Alert - {deviceName} is offline! Last seen: {FormatLastSeen(lastSeenTime)}";

            foreach (var user in usersToNotify)
            {
                try
                {
                    var phoneNumber = dataProtection.Unprotect(user.PhoneNumber!);
                    var sent = await smsService.SendSmsAsync(phoneNumber, message, ct);
                    
                    if (sent)
                    {
                        _logger.LogInformation("SMS notification sent to user {UserId} for offline device {DeviceName}", 
                            user.UserId, deviceName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send SMS notification to user {UserId}", user.UserId);
                }
            }

            // Update last notification time
            _lastNotificationTime[deviceId] = DateTimeOffset.UtcNow;
        }
    }

    private static string FormatLastSeen(DateTimeOffset lastSeen)
    {
        var elapsed = DateTimeOffset.UtcNow - lastSeen;
        
        if (elapsed.TotalMinutes < 1)
            return "moments ago";
        if (elapsed.TotalMinutes < 60)
            return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalHours < 24)
            return $"{(int)elapsed.TotalHours}h ago";
        
        return lastSeen.ToString("MMM dd HH:mm UTC");
    }
}
