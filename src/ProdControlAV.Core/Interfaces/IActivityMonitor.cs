using System;
using System.Threading;
using System.Threading.Tasks;

namespace ProdControlAV.Core.Interfaces;

/// <summary>
/// Monitors system activity to determine when the system can enter idle mode
/// and suspend background SQL operations.
/// </summary>
public interface IActivityMonitor
{
    /// <summary>
    /// Records user activity (login, API request, etc.)
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="ct">Cancellation token</param>
    Task RecordUserActivityAsync(string userId, string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Records agent activity (heartbeat, status update, command poll, etc.)
    /// </summary>
    /// <param name="agentId">Agent identifier</param>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="ct">Cancellation token</param>
    Task RecordAgentActivityAsync(string agentId, string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Checks if the system is currently idle (no activity within the configured timeout)
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if system is idle, false if there is recent activity</returns>
    Task<bool> IsSystemIdleAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the timestamp of the most recent activity across all users and agents
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Timestamp of last activity, or null if no activity recorded</returns>
    Task<DateTimeOffset?> GetLastActivityAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the number of currently active users (activity within idle timeout)
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    Task<int> GetActiveUserCountAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the number of currently active agents (activity within idle timeout)
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    Task<int> GetActiveAgentCountAsync(CancellationToken ct = default);
}
