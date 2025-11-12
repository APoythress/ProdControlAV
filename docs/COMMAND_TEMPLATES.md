# Command Template System

## Overview

The Command Template System provides a static repository of pre-defined commands that users can select and apply to their devices, eliminating the need to manually create each command. The system is initially populated with HyperDeck REST API commands based on the Blackmagic Design HyperDeck REST API specification.

## Features

- **Static Command Library**: Pre-defined command templates organized by category
- **Device-Type Specific**: Templates are tagged with device types (e.g., "HyperDeck")
- **Category Organization**: Commands grouped into logical categories for easy browsing
- **Custom Naming**: Users can customize the name when applying a template
- **Multi-Tenant Safe**: Template application respects tenant boundaries
- **API-First Design**: RESTful API endpoints for browsing and applying templates

## Architecture

### Database Schema

The `CommandTemplates` table stores the pre-defined commands with the following structure:

| Column | Type | Description |
|--------|------|-------------|
| Id | Guid | Primary key |
| Category | String | Category name (e.g., "Transport Control") |
| Name | String | Display name of the command |
| Description | String | Detailed description |
| HttpMethod | String | HTTP method (GET, POST, PUT, DELETE) |
| Endpoint | String | API endpoint or command path |
| Payload | String | Optional JSON payload for POST/PUT requests |
| DeviceType | String | Device type this template applies to |
| DisplayOrder | Int | Order for display in UI |
| IsActive | Boolean | Whether the template is active |

### Command Categories

#### 1. Transport Control (8 commands)
- **Play**: Start playback from current position
- **Stop**: Stop playback or recording
- **Record**: Start recording on current clip
- **Next Clip**: Skip to next clip
- **Previous Clip**: Skip to previous clip
- **Go to Start**: Jump to beginning of current clip
- **Shuttle Forward**: Fast forward playback (2x speed)
- **Shuttle Reverse**: Fast reverse playback (-2x speed)

#### 2. Status & Info (4 commands)
- **Get Transport Info**: Current transport state and position
- **Get Device Info**: Device model and firmware information
- **Get Clips**: List all clips on the active disk
- **Get Disk List**: Information about installed disks

#### 3. Configuration (6 commands)
- **Select Disk Slot 1/2**: Activate disk slot for playback/recording
- **Set Loop Mode On/Off**: Enable/disable loop playback
- **Set Single Clip Mode**: Enable single clip playback mode
- **Set Timeline Mode**: Enable timeline playback mode

#### 4. Clip Management (2 commands)
- **Delete Active Clip**: Delete currently selected clip
- **Format Disk**: Format the active disk (WARNING: Deletes all clips)

## API Endpoints

### List All Templates

```http
GET /api/command-templates
```

Optional query parameters:
- `deviceType`: Filter by device type (e.g., "HyperDeck")

**Response**: Array of `CommandTemplate` objects

### Get Specific Template

```http
GET /api/command-templates/{id}
```

**Response**: Single `CommandTemplate` object

### Apply Template to Device

```http
POST /api/command-templates/{templateId}/apply
Authorization: Bearer {token}
Content-Type: application/json

{
  "deviceId": "guid-of-device",
  "customName": "Optional custom name"
}
```

**Response**: Created `DeviceAction` object

This endpoint:
1. Validates the template exists and is active
2. Verifies the device exists and belongs to the user's tenant
3. Creates a new `DeviceAction` from the template
4. Uses the template name or custom name if provided
5. Creates an outbox entry for Table Storage synchronization

## Database Seeding

### Recommended: Admin API Endpoint (Production)

The preferred method for production environments is to use the admin API endpoint, which allows authorized administrators to trigger seeding from the frontend:

```http
POST /api/admin/seed-command-templates
Authorization: Admin role required
```

This approach:
- Requires Admin role in UserTenants table
- Is accessible from the frontend admin panel
- Provides immediate feedback and statistics
- Is fully logged and auditable
- Does not require direct database access

See [Admin Seeding Guide](ADMIN_SEEDING.md) for complete documentation.

### Alternative: Seeder Script (Development)

For development or initial setup, a console application is provided:

```bash
cd scripts/SeedCommandTemplates
dotnet run -- "Server=your-server;Database=ProdControlAV;User Id=user;Password=password"
```

### Programmatic Seeding

You can also seed the database programmatically using the `CommandTemplateSeeder` class:

```csharp
using ProdControlAV.API.Data;

// In your application startup or migration
CommandTemplateSeeder.SeedCommandTemplates(dbContext);
```

The seeder is idempotent - running it multiple times won't create duplicates.

## Adding New Templates

To add more command templates:

1. Edit `src/ProdControlAV.API/Data/CommandTemplateSeeder.cs`
2. Add new `CommandTemplate` objects to the `GetHyperDeckCommandTemplates()` method
3. Follow the existing pattern:
   ```csharp
   new CommandTemplate
   {
       Id = Guid.Parse("10000000-0000-0000-0000-000000000XXX"),
       Category = "Your Category",
       Name = "Command Name",
       Description = "What the command does",
       HttpMethod = "GET|POST|PUT|DELETE",
       Endpoint = "/api/endpoint",
       Payload = "{\"optional\":\"json\"}",  // For POST/PUT
       DeviceType = "HyperDeck",
       DisplayOrder = order++,
       IsActive = true
   }
   ```
4. Re-run the seeder script or migration

### Template ID Guidelines

- Use predictable GUIDs for easier management
- Format: `10000000-0000-0000-0000-0000000000XX`
- XX = incremental number per category
- Category 1 (Transport): 001-099
- Category 2 (Status): 101-199
- Category 3 (Configuration): 201-299
- Category 4 (Clip Management): 301-399

## Integration with Existing System

### DeviceAction Creation

When a template is applied:
1. A `DeviceAction` is created with the template's properties
2. The action is associated with the specified device and tenant
3. An `OutboxEntry` is created for projection to Table Storage
4. The action becomes immediately available to the user

### Multi-Tenancy

- CommandTemplates are **NOT** tenant-specific (global/static data)
- DeviceActions created from templates **ARE** tenant-specific
- Tenant validation happens during the apply operation
- Users can only apply templates to their own devices

## Testing

The system includes comprehensive unit tests:

### CommandTemplateSeederTests
- Verifies template creation
- Tests idempotency
- Validates all templates have required data
- Checks category organization

### CommandTemplateControllerTests
- Tests listing and filtering templates
- Validates template application
- Checks tenant isolation
- Tests custom naming
- Verifies error handling

Run tests:
```bash
dotnet test --filter "FullyQualifiedName~CommandTemplate"
```

## Future Enhancements

### Planned Features
1. **Template Variables**: Support for parameterized templates (e.g., speed value, clip number)
2. **User Templates**: Allow users to create and share custom templates
3. **Device Type Discovery**: Auto-detect device type and suggest relevant templates
4. **Template Import/Export**: Import templates from SDK documents or export custom templates
5. **Template Versioning**: Track template changes over time
6. **Additional Device Types**: ATEM switchers, PTZ cameras, audio mixers, etc.

### SDK Document Processing

The current templates are based on the HyperDeck REST API specification. To add templates from other device SDKs:

1. **Manual Entry**: Add templates to `CommandTemplateSeeder.cs`
2. **Future: SDK Parser**: Create a parser to extract commands from PDF/HTML documentation
3. **Future: Import Tool**: Build a UI tool to import commands from SDK documents

## Troubleshooting

### Templates Not Appearing
- Check if templates are marked as `IsActive = true`
- Verify the database has been seeded
- Check for filtering by device type

### Apply Template Fails
- Ensure the device exists and belongs to the user's tenant
- Verify the template is active
- Check authorization headers are present
- Review API logs for detailed error messages

### Seeder Script Issues
- Verify connection string is correct
- Ensure database exists and is accessible
- Check for network connectivity to database server
- Review error messages for specific SQL errors

## Security Considerations

- Templates are read-only for end users
- Only administrators should have access to the seeder script
- Template application requires authentication and tenant validation
- SQL injection protection through parameterized queries
- Template payloads should be validated before execution

## Performance

- Templates are static data, cached effectively by EF Core
- Indexed by DeviceType, Category, and DisplayOrder for fast queries
- Minimal database overhead (20 templates = ~5KB)
- No performance impact on existing operations

## License & Attribution

HyperDeck command templates are based on the Blackmagic Design HyperDeck REST API specification. Users should consult the official documentation for complete API details and licensing information.
