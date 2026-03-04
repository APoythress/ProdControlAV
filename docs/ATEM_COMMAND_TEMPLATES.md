# ATEM Command Templates

## Overview

This document describes the pre-seeded command templates for Blackmagic Design ATEM video
switchers. These templates are automatically populated into the `CommandTemplates` table by
`CommandTemplateSeeder.GetAtemCommandTemplates()` so that any user who adds an ATEM device
immediately has a ready-to-use command library without manual data entry.

All ATEM templates are executed via `AtemUdpConnection` (native UDP, port 9910) rather than
the legacy LibAtem wrapper.

---

## Key Differences from HyperDeck Templates

| Field | HyperDeck | ATEM |
|-------|-----------|------|
| `CommandType` | `"REST"` | `"ATEM"` |
| `HttpMethod` | `"GET"`, `"PUT"`, etc. | `"ATEM"` |
| `Endpoint` / `CommandData` | REST path (e.g. `/transports/1`) | **Not used** — `null` |
| `AtemFunction` | `null` | Function name (see table below) |
| `AtemInputId` | `null` | Input source number (1-based) |
| `AtemTransitionRate` | `null` | Mix rate in frames (`null` = device default) |
| `AtemMacroId` | `null` | Macro slot number (1-based) |
| `AtemChannel` | `null` | Aux output channel (0-based) |

> **`CommandData` is always `null` for ATEM commands.** The ATEM execution pipeline reads
> `AtemFunction` and the companion ATEM fields instead.

---

## ATEM Function Reference

### Command `AtemFunction` Values and Their Parameters

| Template Name | `AtemFunction` | `AtemInputId` | `AtemChannel` | `AtemTransitionRate` | `AtemMacroId` | `CommandData` | Notes |
|---|---|---|---|---|---|---|---|
| Cut to Input 1–8 | `CutToProgram` | 1–8 | — | — | — | `null` | Instantaneous program bus cut |
| Fade to Input 1–8 (30 frames) | `FadeToProgram` | 1–8 | — | 30 | — | `null` | Mix transition at 30 frames; omit to use device default |
| Set Preview to Input 1–8 | `SetPreview` | 1–8 | — | — | — | `null` | Routes input to preview bus |
| Set Aux _N_ to Input _M_ | `SetAux` | 1–4 | 0–3 | — | — | `null` | Channel is 0-based (Aux 1 = channel 0) |
| Run Macro 1–5 | `RunMacro` | — | — | — | 1–5 | `null` | Executes saved macro slot |
| List Macros | `ListMacros` | — | — | — | — | `null` | Returns macro list from state cache |
| Get Program Input | `GetProgramInput` | — | — | — | — | `null` | Read-only; returns from local snapshot |
| Get Preview Input | `GetPreviewInput` | — | — | — | — | `null` | Read-only; returns from local snapshot |
| Get Aux 1–2 Source | `GetAuxSource` | — | 0–1 | — | — | `null` | Read-only; returns from local snapshot |

---

## Template Categories

| Category | Templates Seeded | Description |
|---|---|---|
| **Program Switching** | 16 | 8× `CutToProgram` + 8× `FadeToProgram` (inputs 1–8) |
| **Preview Routing** | 8 | 8× `SetPreview` (inputs 1–8) |
| **Aux Routing** | 16 | 4 channels × 4 inputs (`SetAux`) |
| **Macros** | 6 | 5× `RunMacro` (IDs 1–5) + 1× `ListMacros` |
| **Status** | 4 | `GetProgramInput`, `GetPreviewInput`, `GetAuxSource` ch 0 & 1 |
| **Total** | **50** | |

---

## Method → `AtemFunction` Mapping

| `AtemUdpConnection` Method | `AtemFunction` value | Required fields |
|---|---|---|
| `CutToProgramAsync(inputId)` | `"CutToProgram"` | `AtemInputId` |
| `FadeToProgramAsync(inputId, rate)` | `"FadeToProgram"` | `AtemInputId`; optional `AtemTransitionRate` |
| `SetPreviewAsync(inputId)` | `"SetPreview"` | `AtemInputId` |
| `SetAuxAsync(channel, inputId)` | `"SetAux"` | `AtemChannel` (0-based), `AtemInputId` |
| `RunMacroAsync(macroId)` | `"RunMacro"` | `AtemMacroId` |
| `ListMacrosAsync()` | `"ListMacros"` | _(none)_ |
| `AtemStateSnapshot.GetProgramInput(me)` | `"GetProgramInput"` | _(none; ME 0 assumed)_ |
| `AtemStateSnapshot.GetPreviewInput(me)` | `"GetPreviewInput"` | _(none; ME 0 assumed)_ |
| `AtemStateSnapshot.GetAuxSource(channel)` | `"GetAuxSource"` | `AtemChannel` (0-based) |

---

## Database GUID Prefix Scheme

Pre-seeded ATEM template GUIDs follow a structured pattern to avoid collisions with HyperDeck
templates (prefix `10000000-…`):

| Category | GUID prefix | Example |
|---|---|---|
| Program Switching – Cut | `20000000-0000-0000-0001-` | `20000000-0000-0000-0001-000000000001` |
| Program Switching – Fade | `20000000-0000-0000-0002-` | `20000000-0000-0000-0002-000000000001` |
| Preview Routing | `20000000-0000-0000-0003-` | `20000000-0000-0000-0003-000000000001` |
| Aux Routing (ch 0–3) | `20000000-0000-0000-0004-` … `0007-` | `20000000-0000-0000-0004-000000000001` |
| Macros | `20000000-0000-0000-0008-` | `20000000-0000-0000-0008-000000000001` |
| Status | `20000000-0000-0000-0009-` | `20000000-0000-0000-0009-000000000001` |

---

## Usage Example

```csharp
// Retrieve all ATEM templates for display in the UI
var atemTemplates = dbContext.CommandTemplates
    .Where(t => t.DeviceType == "ATEM" && t.IsActive)
    .OrderBy(t => t.Category)
    .ThenBy(t => t.DisplayOrder)
    .ToList();

// Create a Command from a template for a specific device
var template = atemTemplates.First(t => t.AtemFunction == "CutToProgram" && t.AtemInputId == 2);
var command = new Command
{
    CommandId          = Guid.NewGuid(),
    TenantId           = tenantId,
    DeviceId           = deviceId,
    CommandName        = template.Name,
    Description        = template.Description,
    CommandType        = "ATEM",
    AtemFunction       = template.AtemFunction,    // "CutToProgram"
    AtemInputId        = template.AtemInputId,     // 2
    AtemTransitionRate = template.AtemTransitionRate,
    AtemMacroId        = template.AtemMacroId,
    AtemChannel        = template.AtemChannel,
    // CommandData is intentionally left null for ATEM commands
};
```
