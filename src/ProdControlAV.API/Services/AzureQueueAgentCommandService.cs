using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ProdControlAV.Core.Interfaces;
using ProdControlAV.Core.Models;

namespace ProdControlAV.API.Services;

/// <summary>
/// Azure Queue Storage implementation for agent command delivery
/// </summary>
public class AzureQueueAgentCommandService : IAgentCommandQueueService
{
    private readonly string _connectionString;
    private readonly ILogger<AzureQueueAgentCommandService> _logger;
    private readonly int _maxDequeueCount;
    
    public AzureQueueAgentCommandService(
        IConfiguration configuration,
        ILogger<AzureQueueAgentCommandService> logger)
    {
        _connectionString = configuration["Storage:QueueConnectionString"] 
            ?? throw new InvalidOperationException("Storage:QueueConnectionString not configured");
        _logger = logger;
        _maxDequeueCount = configuration.GetValue<int>("Storage:MaxDequeueCount", 5);
    }
    
    /// <summary>
    /// Get the queue name for a specific tenant and agent
    /// Pattern: pcav-{tenantId}-{agentId}
    /// </summary>
    private static string GetQueueName(Guid tenantId, Guid agentId)
    {
        // Azure Queue Storage names must be lowercase, 3-63 chars, alphanumeric + hyphens
        return $"pcav-{tenantId:N}-{agentId:N}".ToLowerInvariant();
    }
    
    /// <summary>
    /// Get the poison queue name for failed messages
    /// </summary>
    private static string GetPoisonQueueName(Guid tenantId, Guid agentId)
    {
        return $"pcav-poison-{tenantId:N}-{agentId:N}".ToLowerInvariant();
    }
    
    private async Task<QueueClient> GetQueueClientAsync(Guid tenantId, Guid agentId, CancellationToken ct)
    {
        var queueName = GetQueueName(tenantId, agentId);
        var client = new QueueClient(_connectionString, queueName);
        await client.CreateIfNotExistsAsync(cancellationToken: ct);
        return client;
    }
    
    private async Task<QueueClient> GetPoisonQueueClientAsync(Guid tenantId, Guid agentId, CancellationToken ct)
    {
        var queueName = GetPoisonQueueName(tenantId, agentId);
        var client = new QueueClient(_connectionString, queueName);
        await client.CreateIfNotExistsAsync(cancellationToken: ct);
        return client;
    }
    
    public async Task EnqueueCommandAsync(AgentCommand command, CancellationToken ct = default)
    {
        try
        {
            var client = await GetQueueClientAsync(command.TenantId, command.AgentId, ct);
            
            var message = new
            {
                commandId = command.Id,
                tenantId = command.TenantId,
                agentId = command.AgentId,
                deviceId = command.DeviceId,
                verb = command.Verb,
                payload = command.Payload,
                dueUtc = command.DueUtc
            };
            
            var json = JsonSerializer.Serialize(message);
            var base64Message = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            
            // Calculate visibility delay based on DueUtc
            TimeSpan? visibilityTimeout = null;
            if (command.DueUtc.HasValue)
            {
                var delay = command.DueUtc.Value - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    // Azure Queue Storage supports max 7 days visibility timeout
                    visibilityTimeout = delay > TimeSpan.FromDays(7) ? TimeSpan.FromDays(7) : delay;
                }
            }
            
            await client.SendMessageAsync(base64Message, visibilityTimeout: visibilityTimeout, cancellationToken: ct);
            
            _logger.LogInformation(
                "Enqueued command {CommandId} for agent {AgentId} with visibility delay {VisibilityDelay}",
                command.Id, command.AgentId, visibilityTimeout);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue command {CommandId} for agent {AgentId}", 
                command.Id, command.AgentId);
            throw;
        }
    }
    
    public async Task<CommandMessage?> ReceiveCommandAsync(
        Guid agentId, 
        Guid tenantId, 
        TimeSpan? visibilityTimeout = null, 
        CancellationToken ct = default)
    {
        try
        {
            var client = await GetQueueClientAsync(tenantId, agentId, ct);
            var timeout = visibilityTimeout ?? TimeSpan.FromSeconds(60);
            
            var response = await client.ReceiveMessageAsync(timeout, ct);
            
            if (response.Value == null)
            {
                return null;
            }
            
            var queueMessage = response.Value;
            var messageText = Encoding.UTF8.GetString(Convert.FromBase64String(queueMessage.MessageText));
            var messageData = JsonSerializer.Deserialize<JsonElement>(messageText);
            
            return new CommandMessage
            {
                MessageId = queueMessage.MessageId,
                PopReceipt = queueMessage.PopReceipt,
                DequeueCount = (int)queueMessage.DequeueCount,
                CommandId = Guid.Parse(messageData.GetProperty("commandId").GetString()!),
                TenantId = Guid.Parse(messageData.GetProperty("tenantId").GetString()!),
                AgentId = Guid.Parse(messageData.GetProperty("agentId").GetString()!),
                DeviceId = Guid.Parse(messageData.GetProperty("deviceId").GetString()!),
                Verb = messageData.GetProperty("verb").GetString()!,
                Payload = messageData.TryGetProperty("payload", out var payload) ? payload.GetString() : null,
                DueUtc = messageData.TryGetProperty("dueUtc", out var due) && due.ValueKind != JsonValueKind.Null
                    ? due.GetDateTime() 
                    : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to receive command for agent {AgentId}", agentId);
            return null;
        }
    }
    
    public async Task DeleteCommandAsync(
        Guid agentId, 
        Guid tenantId, 
        string messageId, 
        string popReceipt, 
        CancellationToken ct = default)
    {
        try
        {
            var client = await GetQueueClientAsync(tenantId, agentId, ct);
            await client.DeleteMessageAsync(messageId, popReceipt, ct);
            
            _logger.LogInformation("Deleted message {MessageId} from queue for agent {AgentId}", 
                messageId, agentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete message {MessageId} for agent {AgentId}", 
                messageId, agentId);
            throw;
        }
    }
    
    public async Task MoveToPoisonQueueAsync(
        Guid agentId, 
        Guid tenantId, 
        AgentCommand command, 
        CancellationToken ct = default)
    {
        try
        {
            var poisonClient = await GetPoisonQueueClientAsync(tenantId, agentId, ct);
            
            var message = new
            {
                commandId = command.Id,
                tenantId = command.TenantId,
                agentId = command.AgentId,
                deviceId = command.DeviceId,
                verb = command.Verb,
                payload = command.Payload,
                dueUtc = command.DueUtc,
                failedAt = DateTime.UtcNow
            };
            
            var json = JsonSerializer.Serialize(message);
            var base64Message = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            
            await poisonClient.SendMessageAsync(base64Message, cancellationToken: ct);
            
            _logger.LogWarning(
                "Moved command {CommandId} to poison queue for agent {AgentId}",
                command.Id, agentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to move command {CommandId} to poison queue for agent {AgentId}", 
                command.Id, agentId);
            throw;
        }
    }
}
