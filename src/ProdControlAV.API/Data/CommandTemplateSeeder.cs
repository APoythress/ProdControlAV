using System;
using System.Collections.Generic;
using ProdControlAV.Core.Models;

namespace ProdControlAV.API.Data;

/// <summary>
/// Seeds the database with pre-defined HyperDeck REST API command templates
/// </summary>
public static class CommandTemplateSeeder
{
    /// <summary>
    /// Gets the list of HyperDeck command templates to seed
    /// Based on the HyperDeck REST API specification
    /// </summary>
    public static List<CommandTemplate> GetHyperDeckCommandTemplates()
    {
        var templates = new List<CommandTemplate>();
        int order = 1;

        // Transport Control Commands
        templates.AddRange(new[]
        {
            new CommandTemplate
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                Category = "Transport Control",
                Name = "Play",
                Description = "Start playback from current position",
                HttpMethod = "PUT",
                Endpoint = "/transports/1",
                Payload = "{\"play\":true}",
                DeviceType = "HyperDeck",
                DisplayOrder = order++,
                IsActive = true
            },
            new CommandTemplate
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000002"),
                Category = "Transport Control",
                Name = "Stop",
                Description = "Stop playback or recording",
                HttpMethod = "PUT",
                Endpoint = "/transports/1",
                Payload = "{\"stop\":true}",
                DeviceType = "HyperDeck",
                DisplayOrder = order++,
                IsActive = true
            },
            new CommandTemplate
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000003"),
                Category = "Transport Control",
                Name = "Record",
                Description = "Start recording on current clip",
                HttpMethod = "PUT",
                Endpoint = "/transports/1",
                Payload = "{\"record\":true}",
                DeviceType = "HyperDeck",
                DisplayOrder = order++,
                IsActive = true
            },
            new CommandTemplate
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000004"),
                Category = "Transport Control",
                Name = "Next Clip",
                Description = "Skip to next clip",
                HttpMethod = "PUT",
                Endpoint = "/transports/1",
                Payload = "{\"goto\":\"next\"}",
                DeviceType = "HyperDeck",
                DisplayOrder = order++,
                IsActive = true
            },
            new CommandTemplate
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000005"),
                Category = "Transport Control",
                Name = "Previous Clip",
                Description = "Skip to previous clip",
                HttpMethod = "PUT",
                Endpoint = "/transports/1",
                Payload = "{\"goto\":\"prev\"}",
                DeviceType = "HyperDeck",
                DisplayOrder = order++,
                IsActive = true
            },
            new CommandTemplate
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000006"),
                Category = "Transport Control",
                Name = "Go to Start",
                Description = "Jump to beginning of current clip",
                HttpMethod = "PUT",
                Endpoint = "/transports/1",
                Payload = "{\"goto\":\"start\"}",
                DeviceType = "HyperDeck",
                DisplayOrder = order++,
                IsActive = true
            },
            new CommandTemplate
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000007"),
                Category = "Transport Control",
                Name = "Shuttle Forward",
                Description = "Fast forward playback",
                HttpMethod = "PUT",
                Endpoint = "/transports/1",
                Payload = "{\"speed\":200}",
                DeviceType = "HyperDeck",
                DisplayOrder = order++,
                IsActive = true
            },
            new CommandTemplate
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000008"),
                Category = "Transport Control",
                Name = "Shuttle Reverse",
                Description = "Fast reverse playback",
                HttpMethod = "PUT",
                Endpoint = "/transports/1",
                Payload = "{\"speed\":-200}",
                DeviceType = "HyperDeck",
                DisplayOrder = order++,
                IsActive = true
            }
        });

        // Status Query Commands
        templates.AddRange(new[]
        {
            new CommandTemplate
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000101"),
                Category = "Status & Info",
                Name = "Get Transport Info",
                Description = "Get current transport state and position",
                HttpMethod = "GET",
                Endpoint = "/transports/1",
                DeviceType = "HyperDeck",
                DisplayOrder = order++,
                IsActive = true
            },
            new CommandTemplate
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000102"),
                Category = "Status & Info",
                Name = "Get Device Info",
                Description = "Get device model and firmware information",
                HttpMethod = "GET",
                Endpoint = "/system",
                DeviceType = "HyperDeck",
                DisplayOrder = order++,
                IsActive = true
            },
            new CommandTemplate
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000103"),
                Category = "Status & Info",
                Name = "Get Clips",
                Description = "List all clips on the active disk",
                HttpMethod = "GET",
                Endpoint = "/clips",
                DeviceType = "HyperDeck",
                DisplayOrder = order++,
                IsActive = true
            },
            new CommandTemplate
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000104"),
                Category = "Status & Info",
                Name = "Get Disk List",
                Description = "Get information about installed disks",
                HttpMethod = "GET",
                Endpoint = "/disks",
                DeviceType = "HyperDeck",
                DisplayOrder = order++,
                IsActive = true
            }
        });

        // Configuration Commands
        templates.AddRange(new[]
        {
            new CommandTemplate
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000201"),
                Category = "Configuration",
                Name = "Select Disk Slot 1",
                Description = "Activate disk slot 1 for playback/recording",
                HttpMethod = "PUT",
                Endpoint = "/disks/active",
                Payload = "{\"slotId\":1}",
                DeviceType = "HyperDeck",
                DisplayOrder = order++,
                IsActive = true
            },
            new CommandTemplate
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000202"),
                Category = "Configuration",
                Name = "Select Disk Slot 2",
                Description = "Activate disk slot 2 for playback/recording",
                HttpMethod = "PUT",
                Endpoint = "/disks/active",
                Payload = "{\"slotId\":2}",
                DeviceType = "HyperDeck",
                DisplayOrder = order++,
                IsActive = true
            },
            new CommandTemplate
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000203"),
                Category = "Configuration",
                Name = "Set Loop Mode On",
                Description = "Enable loop playback",
                HttpMethod = "PUT",
                Endpoint = "/transports/1",
                Payload = "{\"loop\":true}",
                DeviceType = "HyperDeck",
                DisplayOrder = order++,
                IsActive = true
            },
            new CommandTemplate
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000204"),
                Category = "Configuration",
                Name = "Set Loop Mode Off",
                Description = "Disable loop playback",
                HttpMethod = "PUT",
                Endpoint = "/transports/1",
                Payload = "{\"loop\":false}",
                DeviceType = "HyperDeck",
                DisplayOrder = order++,
                IsActive = true
            },
            new CommandTemplate
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000205"),
                Category = "Configuration",
                Name = "Set Single Clip Mode",
                Description = "Enable single clip playback mode",
                HttpMethod = "PUT",
                Endpoint = "/transports/1",
                Payload = "{\"singleClip\":true}",
                DeviceType = "HyperDeck",
                DisplayOrder = order++,
                IsActive = true
            },
            new CommandTemplate
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000206"),
                Category = "Configuration",
                Name = "Set Timeline Mode",
                Description = "Enable timeline playback mode",
                HttpMethod = "PUT",
                Endpoint = "/transports/1",
                Payload = "{\"singleClip\":false}",
                DeviceType = "HyperDeck",
                DisplayOrder = order++,
                IsActive = true
            }
        });

        // Clip Management Commands
        templates.AddRange(new[]
        {
            new CommandTemplate
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000301"),
                Category = "Clip Management",
                Name = "Delete Active Clip",
                Description = "Delete the currently selected clip",
                HttpMethod = "DELETE",
                Endpoint = "/clips/active",
                DeviceType = "HyperDeck",
                DisplayOrder = order++,
                IsActive = true
            },
            new CommandTemplate
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000302"),
                Category = "Clip Management",
                Name = "Format Disk",
                Description = "Format the active disk (WARNING: Deletes all clips)",
                HttpMethod = "POST",
                Endpoint = "/disks/active/format",
                DeviceType = "HyperDeck",
                DisplayOrder = order++,
                IsActive = true
            }
        });

        return templates;
    }

    /// <summary>
    /// Seeds the database with HyperDeck command templates if they don't exist
    /// </summary>
    public static void SeedCommandTemplates(AppDbContext context)
    {
        var templates = GetHyperDeckCommandTemplates();
        
        foreach (var template in templates)
        {
            // Check if template already exists
            var existing = context.CommandTemplates.Find(template.Id);
            if (existing == null)
            {
                context.CommandTemplates.Add(template);
            }
        }
        
        context.SaveChanges();
    }
}
