# ATEM Switch Input Control Feature

## Overview

This feature enables users to control Blackmagic Design ATEM video switchers directly from the ProdControlAV dashboard. Users with the "ATEM-Control" permission can switch inputs on ATEM devices using CUT (instant) or AUTO (fade) transitions.

## Features

- **Permission-Based Access**: Only users with "ATEM-Control" permission can access ATEM control features
- **Multiple Destinations**: Support for Program, Aux1, Aux2, and Aux3 outputs
- **Transition Types**: CUT (instant switch) and AUTO (fade transition)
- **Input Pagination**: Displays up to 8 inputs per page with support for up to 5 pages (40 inputs max)
- **Live Source Indication**: Shows which input is currently live on the selected destination
- **Responsive Design**: Works on desktop and mobile devices

## User Interface

### Dashboard Integration

ATEM devices with control enabled display a purple "CONTROL" badge on their device card:

- Click the device card to view device details (existing functionality)
- Click the "CONTROL" badge to open the ATEM control modal

### Control Modal

The ATEM control modal provides:

1. **Destination Selector**: Dropdown to select the output destination (Program, Aux1, Aux2, Aux3)
2. **Current Live Source**: Display showing which input is currently active on the selected destination
3. **Input Grid**: 4x2 grid showing available inputs with their names and numbers
4. **Pagination**: Navigate through pages if more than 8 inputs are available
5. **Control Buttons**:
   - **CUT**: Instantly switch to the selected input (no transition)
   - **AUTO**: Switch to the selected input with a fade transition

## Setup and Configuration

### 1. Database Migration

Run the migration script to add the UserPermissions table:

```bash
# Run the SQL migration script
# File location: docs/migrations/ATEM-Control-Permission.sql
```

The script creates:
- `UserPermissions` table with proper indexes
- Foreign key relationship to Users table
- Unique constraint on UserId + Permission combination

### 2. Grant Permissions to Users

Grant the "ATEM-Control" permission to specific users:

```sql
-- Grant to specific users by email
INSERT INTO [dbo].[UserPermissions] ([Id], [UserId], [Permission], [CreatedAt])
SELECT 
    NEWID(),
    u.[UserId],
    'ATEM-Control',
    SYSDATETIMEOFFSET()
FROM [dbo].[Users] u
WHERE u.[Email] IN ('user@example.com')
AND NOT EXISTS (
    SELECT 1 FROM [dbo].[UserPermissions] up 
    WHERE up.[UserId] = u.[UserId] 
    AND up.[Permission] = 'ATEM-Control'
);
```

Or grant to all Admin/DevAdmin users:

```sql
-- Grant to all users with Admin or DevAdmin role
INSERT INTO [dbo].[UserPermissions] ([Id], [UserId], [Permission], [CreatedAt])
SELECT 
    NEWID(),
    ut.[UserId],
    'ATEM-Control',
    SYSDATETIMEOFFSET()
FROM [dbo].[UserTenants] ut
WHERE ut.[Role] IN ('Admin', 'DevAdmin')
AND NOT EXISTS (
    SELECT 1 FROM [dbo].[UserPermissions] up 
    WHERE up.[UserId] = ut.[UserId] 
    AND up.[Permission] = 'ATEM-Control'
);
```

### 3. Configure ATEM Devices

Ensure ATEM devices have the following configuration in the database:

```sql
UPDATE Devices
SET 
    Type = 'ATEM',  -- or 'Switcher'
    AtemEnabled = 1,
    AtemTransitionDefaultRate = 30,  -- Default transition rate in frames (optional)
    AtemTransitionDefaultType = 'mix'  -- Default transition type (optional)
WHERE Id = 'your-device-id';
```

### 4. ATEM Network Configuration

For the ATEM control to work with real hardware (not mock data):

1. Ensure the ATEM switcher is accessible on the network
2. The Agent service must be able to reach the ATEM IP address
3. Default ATEM port is 9910
4. Implement LibAtem integration in `AtemConnectionService.cs` (currently stubbed with TODOs)

## API Endpoints

### Get ATEM State

```http
GET /api/atem/{deviceId}/state
Authorization: Required (Cookie or JWT)
Permission: ATEM-Control
```

Response:
```json
{
  "inputs": [
    { "inputId": 1, "name": "Camera 1", "type": "SDI" },
    { "inputId": 2, "name": "Camera 2", "type": "SDI" }
  ],
  "destinations": [
    { "id": "Program", "name": "Program", "currentInputId": 1 },
    { "id": "Aux1", "name": "Aux 1", "currentInputId": 2 }
  ]
}
```

### Execute CUT Transition

```http
POST /api/atem/{deviceId}/cut
Authorization: Required (Cookie or JWT)
Permission: ATEM-Control
Content-Type: application/json

{
  "destination": "Program",
  "inputId": 3
}
```

### Execute AUTO Transition

```http
POST /api/atem/{deviceId}/auto
Authorization: Required (Cookie or JWT)
Permission: ATEM-Control
Content-Type: application/json

{
  "destination": "Program",
  "inputId": 3
}
```

## Architecture

### Backend Components

- **UserPermission Model**: Stores user permissions in the database
- **PermissionAuthorization**: Authorization handler for checking permissions
- **AtemController**: API endpoints for ATEM control operations
- **AtemDtos**: Data transfer objects for ATEM state and control requests

### Frontend Components

- **AtemControlModal.razor**: Main control modal component
- **Dashboard.razor**: Updated to show ATEM control badge
- **Custom CSS**: Styling for input grid, badges, and control buttons

### Permission System

The permission system is flexible and extensible:

1. Users can have multiple permissions
2. DevAdmin users automatically have all permissions
3. Permissions are checked on every API request
4. Database queries are optimized with proper indexes

## Development Notes

### Mock Data

The current implementation uses mock data for development:

- 8 sample inputs (Camera 1-4, HDMI 1-2, Graphics, Media Player)
- 4 destinations (Program, Aux 1-3)
- Mock current source tracking

### TODO: LibAtem Integration

To connect to real ATEM hardware:

1. Update `AtemConnectionService.cs` in the Agent project
2. Implement LibAtem client initialization in `ConnectAsync()`
3. Replace mock data with actual ATEM state queries
4. Implement CUT/AUTO commands using LibAtem API
5. Add proper error handling for network/device issues

See TODOs in `src/ProdControlAV.Agent/Services/AtemConnectionService.cs`

## Security Considerations

- All endpoints require authentication (Cookie or JWT)
- ATEM-Control permission is checked on every request
- DevAdmin users bypass permission checks (by design)
- SQL injection protection via parameterized queries
- XSS protection via Blazor's automatic escaping

## Troubleshooting

### User Can't See Control Badge

1. Verify device Type is "ATEM" or "Switcher"
2. Verify AtemEnabled is set to true
3. Check user has "ATEM-Control" permission in UserPermissions table

### Permission Denied Error

1. Verify user has "ATEM-Control" permission
2. Check DevAdmin status if user should have full access
3. Review API logs for authorization failures

### Control Commands Not Working

1. Check ATEM device is online and reachable
2. Verify LibAtem integration is implemented (currently mock data)
3. Review Agent logs for connection errors
4. Test ATEM network connectivity

## Future Enhancements

Potential improvements for future releases:

- Add preview/program bus control
- Support for macros
- Transition settings (rate, type) per-action
- Multi-viewer support
- Upstream/downstream keyer control
- Audio mixer control
- Save/recall presets
- Permission caching for performance
- WebSocket for real-time state updates

## References

- [LibAtem Documentation](https://github.com/LibAtem/LibAtem)
- [ATEM Software Developers Kit](https://www.blackmagicdesign.com/developer/product/atem)
- [ProdControlAV Documentation](../README.md)
