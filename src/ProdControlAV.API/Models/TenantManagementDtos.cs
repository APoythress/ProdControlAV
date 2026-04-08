using System.ComponentModel.DataAnnotations;

namespace ProdControlAV.API.Models;

/// <summary>
/// Request to update tenant name
/// </summary>
public record UpdateTenantNameRequest(
    [Required]
    [MaxLength(250)]
    string Name
);

/// <summary>
/// Request to update tenant subscription plan
/// </summary>
public record UpdateTenantSubscriptionRequest(
    [Required]
    int SubscriptionPlanId
);

/// <summary>
/// Request to update tenant status
/// </summary>
public record UpdateTenantStatusRequest(
    [Required]
    int TenantStatusId
);

/// <summary>
/// Response for regenerate slug operation
/// </summary>
public record RegenerateSlugResponse(
    string NewSlug
);

/// <summary>
/// Request to add a new agent for a tenant
/// </summary>
public record AddAgentRequest(
    [Required]
    [MaxLength(200)]
    string Name,
    
    [MaxLength(200)]
    string? LocationName
);

/// <summary>
/// DTO for agent information
/// </summary>
public record AgentDto(
    Guid Id,
    string Name,
    string? LocationName,
    DateTime? LastSeenUtc,
    string? Version
);

/// <summary>
/// Request to add a client note
/// </summary>
public record AddClientNoteRequest(
    [Required]
    [MaxLength(500)]
    string NoteText
);

/// <summary>
/// DTO for client note
/// </summary>
public record ClientNoteDto(
    Guid Id,
    string NoteText,
    DateTime CreatedUtc,
    string CreatedBy
);

/// <summary>
/// Response for paginated client notes
/// </summary>
public record ClientNotesResponse(
    List<ClientNoteDto> Notes,
    int TotalCount,
    int PageSize,
    int CurrentPage,
    int TotalPages
);

/// <summary>
/// DTO for tenant management details
/// </summary>
public record TenantManagementDto(
    Guid TenantId,
    string Name,
    string Slug,
    int? TenantStatusId,
    string? Status,
    int? SubscriptionPlanId,
    string? Subscription,
    DateTime CreatedUtc,
    List<AgentDto> Agents,
    List<ClientNoteDto> RecentNotes
);
