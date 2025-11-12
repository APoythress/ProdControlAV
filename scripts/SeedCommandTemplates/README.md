# Command Template Seeder

This script seeds the database with pre-defined HyperDeck REST API command templates that users can select to add to their devices.

## Usage

### Development (Local Database)

```bash
cd scripts/SeedCommandTemplates
dotnet run -- "Server=localhost;Database=ProdControlAV;Integrated Security=true;TrustServerCertificate=true"
```

### Production (Azure SQL)

```bash
cd scripts/SeedCommandTemplates
dotnet run -- "Server=your-server.database.windows.net;Database=ProdControlAV;User Id=your-user;Password=your-password;Encrypt=true"
```

## What Gets Seeded

The script populates the `CommandTemplates` table with common HyperDeck REST API commands organized into categories:

### Transport Control (8 commands)
- Play
- Stop
- Record
- Next Clip
- Previous Clip
- Go to Start
- Shuttle Forward
- Shuttle Reverse

### Status & Info (4 commands)
- Get Transport Info
- Get Device Info
- Get Clips
- Get Disk List

### Configuration (6 commands)
- Select Disk Slot 1
- Select Disk Slot 2
- Set Loop Mode On/Off
- Set Single Clip Mode
- Set Timeline Mode

### Clip Management (2 commands)
- Delete Active Clip
- Format Disk

## How to Use Command Templates

After seeding, users can:

1. Browse available command templates via API:
   ```
   GET /api/command-templates
   GET /api/command-templates?deviceType=HyperDeck
   ```

2. Apply a template to a device:
   ```
   POST /api/command-templates/{templateId}/apply
   {
     "deviceId": "guid-of-device",
     "customName": "Optional custom name"
   }
   ```

## Adding Custom Templates

To add more command templates based on the HyperDeck SDK:

1. Edit `src/ProdControlAV.API/Data/CommandTemplateSeeder.cs`
2. Add new `CommandTemplate` objects to the appropriate category
3. Re-run the seeder script

## Notes

- The script is idempotent - running it multiple times won't create duplicates
- Each template has a unique GUID that prevents duplicate insertions
- Templates are device-type specific (currently HyperDeck)
- Templates can be enabled/disabled via the `IsActive` flag
