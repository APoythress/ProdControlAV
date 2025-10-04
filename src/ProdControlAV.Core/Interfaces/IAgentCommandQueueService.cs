using System;
using System.Threading;
using System.Threading.Tasks;
using ProdControlAV.Core.Models;

namespace ProdControlAV.Core.Interfaces;

/// <summary>
/// Service for managing agent commands in Azure Queue Storage
/// </summary>
public interface IAgentCommandQueueService
{
    /// <summary>
    /// Enqueue a command to the agent-specific queue with optional visibility delay
    /// </summary>
    /// <param name="command">The command to enqueue</param>
    /// <param name="ct">Cancellation token</param>
    Task EnqueueCommandAsync(AgentCommand command, CancellationToken ct = default);
    
    /// <summary>
    /// Receive the next command from the agent-specific queue
    /// </summary>
    /// <param name="agentId">The agent ID to receive commands for</param>
    /// <param name="tenantId">The tenant ID</param>
    /// <param name="visibilityTimeout">How long the message should be invisible after retrieval (default 60s)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Command envelope or null if no messages available</returns>
    Task<CommandMessage?> ReceiveCommandAsync(Guid agentId, Guid tenantId, TimeSpan? visibilityTimeout = null, CancellationToken ct = default);
    
    /// <summary>
    /// Delete a command from the queue after successful processing
    /// </summary>
    /// <param name="agentId">The agent ID</param>
    /// <param name="tenantId">The tenant ID</param>
    /// <param name="messageId">The message ID from ReceiveCommandAsync</param>
    /// <param name="popReceipt">The pop receipt from ReceiveCommandAsync</param>
    /// <param name="ct">Cancellation token</param>
    Task DeleteCommandAsync(Guid agentId, Guid tenantId, string messageId, string popReceipt, CancellationToken ct = default);
    
    /// <summary>
    /// Move a failed command to the poison queue
    /// </summary>
    /// <param name="agentId">The agent ID</param>
    /// <param name="tenantId">The tenant ID</param>
    /// <param name="command">The failed command</param>
    /// <param name="ct">Cancellation token</param>
    Task MoveToPoisonQueueAsync(Guid agentId, Guid tenantId, AgentCommand command, CancellationToken ct = default);
}

/// <summary>
/// Represents a command message retrieved from the queue
/// </summary>
public class CommandMessage
{
    public string MessageId { get; set; } = default!;
    public string PopReceipt { get; set; } = default!;
    public int DequeueCount { get; set; }
    public Guid CommandId { get; set; }
    public Guid TenantId { get; set; }
    public Guid AgentId { get; set; }
    public Guid DeviceId { get; set; }
    public string Verb { get; set; } = default!;
    public string? Payload { get; set; }
    public DateTime? DueUtc { get; set; }
}
