using System;
using System.Collections.Generic;
using ProdControlAV.Core.Models;

namespace ProdControlAV.API.Data;

/// <summary>
/// Seeds the database with pre-defined command templates for all supported device types.
/// </summary>
public static class CommandTemplateSeeder
{
    /// <summary>
    /// Gets the list of HyperDeck command templates to seed.
    /// Based on the HyperDeck REST API specification.
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
    /// Gets the list of pre-defined ATEM command templates to seed.
    ///
    /// For ATEM templates the <see cref="CommandTemplate.HttpMethod"/> is set to <c>"ATEM"</c>
    /// and <see cref="CommandTemplate.Endpoint"/> contains the function name so that consumers
    /// can identify the command type without inspecting <see cref="CommandTemplate.AtemFunction"/>.
    /// The ATEM-specific nullable fields (<see cref="CommandTemplate.AtemFunction"/>,
    /// <see cref="CommandTemplate.AtemInputId"/>, <see cref="CommandTemplate.AtemTransitionRate"/>,
    /// <see cref="CommandTemplate.AtemMacroId"/>, <see cref="CommandTemplate.AtemChannel"/>)
    /// carry the typed parameters that are copied to the corresponding <see cref="Command"/> fields
    /// when a user creates a command from this template.
    ///
    /// NOTE: <see cref="Command.CommandData"/> is <c>null</c> for all ATEM commands — the ATEM
    /// execution path reads <see cref="Command.AtemFunction"/> and the other ATEM fields instead.
    /// </summary>
    public static List<CommandTemplate> GetAtemCommandTemplates()
    {
        var templates = new List<CommandTemplate>();
        int order = 1;

        // ── Program Switching – Cut ───────────────────────────────────────────
        for (int input = 1; input <= 8; input++)
        {
            templates.Add(new CommandTemplate
            {
                Id = Guid.Parse($"20000000-0000-0000-0001-{input:D12}"),
                Category = "Program Switching",
                Name = $"Cut to Input {input}",
                Description = $"Immediately cut the program bus to input {input}",
                HttpMethod = "ATEM",
                Endpoint = "CutToProgram",
                DeviceType = "ATEM",
                DisplayOrder = order++,
                IsActive = true,
                AtemFunction = "CutToProgram",
                AtemInputId = input
            });
        }

        // ── Program Switching – Fade/Auto ─────────────────────────────────────
        for (int input = 1; input <= 8; input++)
        {
            templates.Add(new CommandTemplate
            {
                Id = Guid.Parse($"20000000-0000-0000-0002-{input:D12}"),
                Category = "Program Switching",
                Name = $"Fade to Input {input} (30 frames)",
                Description = $"Perform a mix/auto transition to input {input} at 30 frames",
                HttpMethod = "ATEM",
                Endpoint = "FadeToProgram",
                DeviceType = "ATEM",
                DisplayOrder = order++,
                IsActive = true,
                AtemFunction = "FadeToProgram",
                AtemInputId = input,
                AtemTransitionRate = 30
            });
        }

        // ── Preview Routing ───────────────────────────────────────────────────
        for (int input = 1; input <= 8; input++)
        {
            templates.Add(new CommandTemplate
            {
                Id = Guid.Parse($"20000000-0000-0000-0003-{input:D12}"),
                Category = "Preview Routing",
                Name = $"Set Preview to Input {input}",
                Description = $"Route input {input} to the preview bus",
                HttpMethod = "ATEM",
                Endpoint = "SetPreview",
                DeviceType = "ATEM",
                DisplayOrder = order++,
                IsActive = true,
                AtemFunction = "SetPreview",
                AtemInputId = input
            });
        }

        // ── Aux Routing – 4 channels × 4 inputs ──────────────────────────────
        for (int channel = 0; channel < 4; channel++)
        {
            for (int input = 1; input <= 4; input++)
            {
                templates.Add(new CommandTemplate
                {
                    Id = Guid.Parse($"20000000-0000-0000-{channel + 4:D4}-{input:D12}"),
                    Category = "Aux Routing",
                    Name = $"Set Aux {channel + 1} to Input {input}",
                    Description = $"Route input {input} to auxiliary output {channel + 1} (0-based channel index {channel})",
                    HttpMethod = "ATEM",
                    Endpoint = "SetAux",
                    DeviceType = "ATEM",
                    DisplayOrder = order++,
                    IsActive = true,
                    AtemFunction = "SetAux",
                    AtemInputId = input,
                    AtemChannel = channel
                });
            }
        }

        // ── Macros ────────────────────────────────────────────────────────────
        for (int macroId = 1; macroId <= 5; macroId++)
        {
            templates.Add(new CommandTemplate
            {
                Id = Guid.Parse($"20000000-0000-0000-0008-{macroId:D12}"),
                Category = "Macros",
                Name = $"Run Macro {macroId}",
                Description = $"Execute ATEM macro slot {macroId}",
                HttpMethod = "ATEM",
                Endpoint = "RunMacro",
                DeviceType = "ATEM",
                DisplayOrder = order++,
                IsActive = true,
                AtemFunction = "RunMacro",
                AtemMacroId = macroId
            });
        }

        templates.Add(new CommandTemplate
        {
            Id = Guid.Parse("20000000-0000-0000-0008-000000000010"),
            Category = "Macros",
            Name = "List Macros",
            Description = "Retrieve the list of available macros from the ATEM state cache",
            HttpMethod = "ATEM",
            Endpoint = "ListMacros",
            DeviceType = "ATEM",
            DisplayOrder = order++,
            IsActive = true,
            AtemFunction = "ListMacros"
        });

        // ── Status (read from local state cache – no round-trip) ──────────────
        templates.Add(new CommandTemplate
        {
            Id = Guid.Parse("20000000-0000-0000-0009-000000000001"),
            Category = "Status",
            Name = "Get Program Input",
            Description = "Read the current program-bus input from the local ATEM state cache (ME 0)",
            HttpMethod = "ATEM",
            Endpoint = "GetProgramInput",
            DeviceType = "ATEM",
            DisplayOrder = order++,
            IsActive = true,
            AtemFunction = "GetProgramInput"
        });

        templates.Add(new CommandTemplate
        {
            Id = Guid.Parse("20000000-0000-0000-0009-000000000002"),
            Category = "Status",
            Name = "Get Preview Input",
            Description = "Read the current preview-bus input from the local ATEM state cache (ME 0)",
            HttpMethod = "ATEM",
            Endpoint = "GetPreviewInput",
            DeviceType = "ATEM",
            DisplayOrder = order++,
            IsActive = true,
            AtemFunction = "GetPreviewInput"
        });

        for (int channel = 0; channel < 2; channel++)
        {
            templates.Add(new CommandTemplate
            {
                Id = Guid.Parse($"20000000-0000-0000-0009-{channel + 3:D12}"),
                Category = "Status",
                Name = $"Get Aux {channel + 1} Source",
                Description = $"Read the current source for auxiliary output {channel + 1} from the local ATEM state cache",
                HttpMethod = "ATEM",
                Endpoint = "GetAuxSource",
                DeviceType = "ATEM",
                DisplayOrder = order++,
                IsActive = true,
                AtemFunction = "GetAuxSource",
                AtemChannel = channel
            });
        }

        return templates;
    }

    /// <summary>
    /// Seeds the database with HyperDeck and ATEM command templates if they don't already exist.
    /// This method is idempotent: running it multiple times will not create duplicates.
    /// </summary>
    public static void SeedCommandTemplates(AppDbContext context)
    {
        var allTemplates = new List<CommandTemplate>();
        allTemplates.AddRange(GetHyperDeckCommandTemplates());
        allTemplates.AddRange(GetAtemCommandTemplates());

        foreach (var template in allTemplates)
        {
            if (context.CommandTemplates.Find(template.Id) == null)
            {
                context.CommandTemplates.Add(template);
            }
        }

        context.SaveChanges();
    }
}
