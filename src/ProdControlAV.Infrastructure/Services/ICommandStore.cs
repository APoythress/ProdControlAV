using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProdControlAV.Infrastructure.Services
{
    /// <summary>
    /// Represents a command queued for execution in Table Storage
    /// </summary>
    public record CommandQueueDto(
        Guid CommandId,
        Guid TenantId,
        Guid DeviceId,
        string CommandName,
        string CommandType,
        string? CommandData,
        string? HttpMethod,
        string? RequestBody,
        string? RequestHeaders,
        DateTimeOffset QueuedUtc,
        Guid QueuedByUserId,
        string? DeviceIp = null,
        int? DevicePort = null,
        string? DeviceType = null,
        bool MonitorRecordingStatus = false,
        string? StatusEndpoint = null,
        int StatusPollingIntervalSeconds = 60,
        string Status = "Pending",
        int AttemptCount = 0,
        string? AtemFunction = null,
        int? AtemInputId = null,
        int? AtemTransitionRate = null,
        int? AtemMacroId = null);

    /// <summary>
    /// Represents command execution history in Table Storage
    /// </summary>
    public record CommandHistoryDto(
        Guid ExecutionId,
        Guid CommandId,
        Guid TenantId,
        Guid DeviceId,
        string CommandName,
        DateTimeOffset ExecutedUtc,
        bool Success,
        string? ErrorMessage = null,
        string? Response = null,
        int? HttpStatusCode = null,
        double? ExecutionTimeMs = null);

    /// <summary>
    /// Interface for managing command queue in Table Storage
    /// </summary>
    public interface ICommandQueueStore
    {
        /// <summary>
        /// Add a command to the execution queue
        /// </summary>
        Task EnqueueAsync(CommandQueueDto command, CancellationToken ct);
        
        /// <summary>
        /// Get pending commands for a specific device
        /// </summary>
        IAsyncEnumerable<CommandQueueDto> GetPendingForDeviceAsync(Guid tenantId, Guid deviceId, CancellationToken ct);
        
        /// <summary>
        /// Get all pending commands for a tenant
        /// </summary>
        IAsyncEnumerable<CommandQueueDto> GetPendingForTenantAsync(Guid tenantId, CancellationToken ct);
        
        /// <summary>
        /// Get processing commands that have been stuck for longer than the timeout
        /// </summary>
        IAsyncEnumerable<CommandQueueDto> GetStuckProcessingCommandsAsync(Guid tenantId, TimeSpan timeout, CancellationToken ct);
        
        /// <summary>
        /// Reset command from Processing back to Pending for retry
        /// </summary>
        Task ResetToPendingAsync(Guid tenantId, Guid commandId, CancellationToken ct);
        
        /// <summary>
        /// Mark command as processing and increment attempt count
        /// </summary>
        Task MarkAsProcessingAsync(Guid tenantId, Guid commandId, CancellationToken ct);
        
        /// <summary>
        /// Mark command as succeeded after successful execution
        /// </summary>
        Task MarkAsSucceededAsync(Guid tenantId, Guid commandId, CancellationToken ct);
        
        /// <summary>
        /// Mark command as failed after max retries or execution failure
        /// </summary>
        Task MarkAsFailedAsync(Guid tenantId, Guid commandId, CancellationToken ct);
        
        /// <summary>
        /// Remove command from queue after processing
        /// </summary>
        Task DequeueAsync(Guid tenantId, Guid commandId, CancellationToken ct);
    }

    /// <summary>
    /// Interface for managing command execution history in Table Storage
    /// </summary>
    public interface ICommandHistoryStore
    {
        /// <summary>
        /// Record command execution result
        /// </summary>
        Task RecordExecutionAsync(CommandHistoryDto history, CancellationToken ct);
        
        /// <summary>
        /// Get execution history for a command
        /// </summary>
        IAsyncEnumerable<CommandHistoryDto> GetHistoryForCommandAsync(Guid tenantId, Guid commandId, CancellationToken ct);
        
        /// <summary>
        /// Get execution history for a device
        /// </summary>
        IAsyncEnumerable<CommandHistoryDto> GetHistoryForDeviceAsync(Guid tenantId, Guid deviceId, CancellationToken ct);
        
        /// <summary>
        /// Get recent execution history for a tenant (last N days)
        /// </summary>
        IAsyncEnumerable<CommandHistoryDto> GetRecentHistoryForTenantAsync(Guid tenantId, int days, CancellationToken ct);
    }
}
