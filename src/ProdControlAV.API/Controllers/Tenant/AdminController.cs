using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProdControlAV.API.Data;

namespace ProdControlAV.API.Controllers;

/// <summary>
/// Admin-only endpoints for system management
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = "Admin")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<AdminController> _logger;

    public AdminController(AppDbContext db, ILogger<AdminController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Trigger seeding of command templates
    /// This populates the CommandTemplates table with pre-defined HyperDeck commands
    /// DevAdmin only - this is a migration/setup endpoint
    /// </summary>
    /// <returns>Summary of seeded templates</returns>
    [HttpPost("seed-command-templates")]
    [Authorize(Policy = "DevAdmin")]
    public IActionResult SeedCommandTemplates()
    {
        try
        {
            _logger.LogInformation("Admin triggered command template seeding");

            // Get count before seeding
            var countBefore = _db.CommandTemplates.Count();

            // Perform seeding
            CommandTemplateSeeder.SeedCommandTemplates(_db);

            // Get count after seeding
            var countAfter = _db.CommandTemplates.Count();
            var newTemplates = countAfter - countBefore;

            // Get summary by category
            var summary = _db.CommandTemplates
                .GroupBy(t => t.Category)
                .Select(g => new { Category = g.Key, Count = g.Count() })
                .OrderBy(g => g.Category)
                .ToList();

            _logger.LogInformation("Command template seeding completed. Added {NewTemplates} templates", newTemplates);

            return Ok(new
            {
                success = true,
                message = "Command templates seeded successfully",
                totalTemplates = countAfter,
                newTemplates = newTemplates,
                categories = summary
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding command templates");
            return StatusCode(500, new
            {
                success = false,
                message = "Failed to seed command templates",
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Get statistics about command templates in the system
    /// </summary>
    [HttpGet("command-template-stats")]
    public IActionResult GetCommandTemplateStats()
    {
        try
        {
            var totalTemplates = _db.CommandTemplates.Count();
            var activeTemplates = _db.CommandTemplates.Count(t => t.IsActive);
            
            var byCategory = _db.CommandTemplates
                .GroupBy(t => t.Category)
                .Select(g => new { Category = g.Key, Count = g.Count() })
                .OrderBy(g => g.Category)
                .ToList();

            var byDeviceType = _db.CommandTemplates
                .GroupBy(t => t.DeviceType)
                .Select(g => new { DeviceType = g.Key, Count = g.Count() })
                .OrderBy(g => g.DeviceType)
                .ToList();

            return Ok(new
            {
                totalTemplates,
                activeTemplates,
                inactiveTemplates = totalTemplates - activeTemplates,
                byCategory,
                byDeviceType
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving command template stats");
            return StatusCode(500, new
            {
                success = false,
                message = "Failed to retrieve statistics",
                error = ex.Message
            });
        }
    }
}
