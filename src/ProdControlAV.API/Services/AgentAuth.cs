using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProdControlAV.Core.Models;

namespace ProdControlAV.API.Services;

public interface IAgentAuth
{
    Task<(Agent? agent, string? error)> ValidateAsync(string agentKey, CancellationToken ct);
    string HashAgentKey(string agentKey);
}

public sealed class AgentAuth : IAgentAuth
{
    private readonly AppDbContext _db;
    public AgentAuth(AppDbContext db) => _db = db;

    public async Task<(Agent? agent, string? error)> ValidateAsync(string agentKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(agentKey)) return (null, "missing_agent_key");
        var hash = HashAgentKey(agentKey);
        var agent = await _db.Agents.FirstOrDefaultAsync(a => a.AgentKeyHash == hash, ct);
        return agent is null ? (null, "invalid_agent_key") : (agent, null);
    }

    public string HashAgentKey(string agentKey)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(agentKey)));
    }
}