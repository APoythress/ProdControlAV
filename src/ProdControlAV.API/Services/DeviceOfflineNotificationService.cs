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
/// Background service that monitors device status changes and agent availability,
/// sending SMS notifications when devices go offline or agents become unresponsive
/// for users on Pro plan who have opted in
/// </summary>
public class DeviceOfflineNotificationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DeviceOfflineNotificationService> _logger;
    
    // Track device status to detect transitions from online to offline
    private readonly ConcurrentDictionary<Guid, bool> _lastKnownStatus = new();
    
    // Track agent last seen times to detect agent offline
    private readonly ConcurrentDictionary<Guid, DateTimeOffset?> _lastKnownAgentSeen = new();
    
    // Rate limiting: track last notification time per device/agent to prevent spam
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastNotificationTime = new();
    
    private const int PollingIntervalSeconds = 30; // Check every 30 seconds
    private const int MinNotificationIntervalMinutes = 60; // Don't spam - wait at least 1 hour between notifications for same device/agent
    private const int AgentOfflineThresholdMinutes = 20; // Agent considered offline if not seen in 20 minutes

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
        var agentAuthStore = scope.ServiceProvider.GetRequiredService<IAgentAuthStore>();
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
                await CheckTenantAgentsAsync(tenantId, agentAuthStore, db, smsService, dataProtection, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking devices/agents for tenant {TenantId}", tenantId);
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

        // Get device names and SMS alert settings from SQL
        var deviceIds = deviceStatuses.Select(d => d.DeviceId).ToList();
        var devices = await db.Devices
            .Where(d => d.TenantId == tenantId && deviceIds.Contains(d.Id))
            .Select(d => new { d.Id, d.Name, d.SmsAlertsEnabled })
            .ToListAsync(ct);

        var deviceInfoMap = devices.ToDictionary(d => d.Id, d => new { d.Name, d.SmsAlertsEnabled });

        // Check each device for offline transitions
        foreach (var deviceStatus in deviceStatuses)
        {
            var deviceInfo = deviceInfoMap.GetValueOrDefault(deviceStatus.DeviceId);
            if (deviceInfo == null)
            {
                continue; // Device not found or deleted
            }
            
            // Skip if SMS alerts are disabled for this device
            if (!deviceInfo.SmsAlertsEnabled)
            {
                _logger.LogDebug("Skipping device {DeviceId} ({DeviceName}) - SMS alerts disabled", 
                    deviceStatus.DeviceId, deviceInfo.Name);
                continue;
            }
            
            await CheckDeviceAndNotifyAsync(
                tenantId,
                deviceStatus,
                deviceInfo.Name,
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

    private async Task CheckTenantAgentsAsync(
        Guid tenantId,
        IAgentAuthStore agentAuthStore,
        AppDbContext db,
        ISmsService smsService,
        IDataProtectionService dataProtection,
        CancellationToken ct)
    {
        // Get all agents for this tenant from Table Storage
        var agents = new List<AgentAuthDto>();
        await foreach (var agent in agentAuthStore.GetAgentsForTenantAsync(tenantId, ct))
        {
            agents.Add(agent);
        }

        if (agents.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        foreach (var agent in agents)
        {
            try
            {
                await CheckAgentAndNotifyAsync(agent, tenantId, now, db, smsService, dataProtection, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking agent {AgentId} for tenant {TenantId}", agent.AgentId, tenantId);
            }
        }
    }

    private async Task CheckAgentAndNotifyAsync(
        AgentAuthDto agent,
        Guid tenantId,
        DateTimeOffset now,
        AppDbContext db,
        ISmsService smsService,
        IDataProtectionService dataProtection,
        CancellationToken ct)
    {
        var agentId = agent.AgentId;
        var currentLastSeen = agent.LastSeenUtc;

        // Check if we've seen this agent before
        var previousLastSeen = _lastKnownAgentSeen.GetOrAdd(agentId, currentLastSeen);
        _lastKnownAgentSeen[agentId] = currentLastSeen;

        // If agent has never been seen or last seen is null, skip
        if (!currentLastSeen.HasValue)
        {
            return;
        }

        // Calculate time since last seen
        var timeSinceLastSeen = now - currentLastSeen.Value;
        
        // Determine agent states
        var isCurrentlyOffline = timeSinceLastSeen.TotalMinutes >= AgentOfflineThresholdMinutes;
        var wasOfflineBefore = previousLastSeen.HasValue && 
                               (now - previousLastSeen.Value).TotalMinutes >= AgentOfflineThresholdMinutes;
        var hasLastSeenChanged = !previousLastSeen.HasValue || previousLastSeen.Value != currentLastSeen.Value;
        var isNewOfflineTransition = isCurrentlyOffline && (!wasOfflineBefore || hasLastSeenChanged);

        // Only notify if agent is offline and this is a new offline state or transition
        if (isCurrentlyOffline && isNewOfflineTransition)
        {
            // Check rate limiting
            if (_lastNotificationTime.TryGetValue(agentId, out var lastNotification))
            {
                var timeSinceLastNotification = now - lastNotification;
                if (timeSinceLastNotification.TotalMinutes < MinNotificationIntervalMinutes)
                {
                    _logger.LogDebug("Skipping agent notification for {AgentId} - notified {Minutes} minutes ago",
                        agentId, (int)timeSinceLastNotification.TotalMinutes);
                    return;
                }
            }

            _logger.LogInformation("Agent {AgentId} ({AgentName}) is offline (not seen for {Minutes} minutes). Checking for notification subscribers.",
                agentId, agent.Name, (int)timeSinceLastSeen.TotalMinutes);

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

            // Format last seen in UTC with explicit timezone indicator to avoid confusion
            // Using 12-hour format as requested, but showing UTC time
            var lastSeenFormatted = currentLastSeen.Value.ToString("hh:mm:ss tt") + " UTC";
            var message = $"PROD-CONTROL: Alert - {agent.Name} is offline! Last seen: {lastSeenFormatted}";

            foreach (var user in usersToNotify)
            {
                try
                {
                    var phoneNumber = dataProtection.Unprotect(user.PhoneNumber!);
                    var sent = await smsService.SendSmsAsync(phoneNumber, message, ct);

                    if (sent)
                    {
                        _logger.LogInformation("SMS notification sent to user {UserId} for offline agent {AgentName}",
                            user.UserId, agent.Name);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send SMS notification to user {UserId} for agent {AgentId}", user.UserId, agentId);
                }
            }

            // Update last notification time for this agent
            _lastNotificationTime[agentId] = now;
        }
    }
}
