# Admin Seeding Guide

## Overview

The Command Template system can now be seeded via an admin API endpoint, allowing administrators to manually trigger the seeding process from the frontend instead of running database scripts.

## Admin Role

To use admin endpoints, a user must have the "Admin" role in the `UserTenants` table for their current tenant.

### Setting a User as Admin

To grant admin privileges to a user, update their role in the `UserTenants` table:

```sql
UPDATE UserTenants
SET Role = 'Admin'
WHERE UserId = '<user-guid>' 
  AND TenantId = '<tenant-guid>';
```

## Admin API Endpoints

### Seed Command Templates

**Endpoint:** `POST /api/admin/seed-command-templates`

**Authorization:** Requires Admin role

**Description:** Populates the CommandTemplates table with pre-defined HyperDeck commands. This operation is idempotent - running it multiple times will not create duplicates.

**Request:**
```http
POST /api/admin/seed-command-templates HTTP/1.1
Host: localhost:5001
Cookie: prodcontrolav.auth=<your-auth-cookie>
```

**Response (Success):**
```json
{
  "success": true,
  "message": "Command templates seeded successfully",
  "totalTemplates": 20,
  "newTemplates": 20,
  "categories": [
    { "category": "Clip Management", "count": 2 },
    { "category": "Configuration", "count": 6 },
    { "category": "Status & Info", "count": 4 },
    { "category": "Transport Control", "count": 8 }
  ]
}
```

**Response (Error):**
```json
{
  "success": false,
  "message": "Failed to seed command templates",
  "error": "Error details here"
}
```

### Get Command Template Statistics

**Endpoint:** `GET /api/admin/command-template-stats`

**Authorization:** Requires Admin role

**Description:** Retrieves statistics about command templates in the system.

**Request:**
```http
GET /api/admin/command-template-stats HTTP/1.1
Host: localhost:5001
Cookie: prodcontrolav.auth=<your-auth-cookie>
```

**Response:**
```json
{
  "totalTemplates": 20,
  "activeTemplates": 20,
  "inactiveTemplates": 0,
  "byCategory": [
    { "category": "Clip Management", "count": 2 },
    { "category": "Configuration", "count": 6 },
    { "category": "Status & Info", "count": 4 },
    { "category": "Transport Control", "count": 8 }
  ],
  "byDeviceType": [
    { "deviceType": "HyperDeck", "count": 20 }
  ]
}
```

## Frontend Integration

### Example JavaScript/TypeScript Code

```typescript
// Check if templates are already seeded
async function checkTemplateStats() {
  const response = await fetch('/api/admin/command-template-stats', {
    method: 'GET',
    credentials: 'include'
  });
  
  if (response.ok) {
    const stats = await response.json();
    return stats;
  } else if (response.status === 403) {
    alert('You do not have admin privileges');
  }
  return null;
}

// Seed command templates
async function seedCommandTemplates() {
  const response = await fetch('/api/admin/seed-command-templates', {
    method: 'POST',
    credentials: 'include'
  });
  
  if (response.ok) {
    const result = await response.json();
    console.log(`Seeded ${result.newTemplates} new templates`);
    console.log('Categories:', result.categories);
    return result;
  } else if (response.status === 403) {
    alert('You do not have admin privileges');
  } else {
    const error = await response.json();
    console.error('Seeding failed:', error.message);
  }
  return null;
}

// Example usage in a component
async function initializeAdmin() {
  // Check current stats
  const stats = await checkTemplateStats();
  
  if (stats && stats.totalTemplates === 0) {
    console.log('No templates found. Click the seed button to populate.');
  } else if (stats) {
    console.log(`Found ${stats.totalTemplates} templates already seeded.`);
  }
}
```

### Example Blazor Component

```razor
@page "/admin/templates"
@inject HttpClient Http

<h3>Command Template Management</h3>

@if (stats != null)
{
    <div class="stats">
        <p>Total Templates: @stats.TotalTemplates</p>
        <p>Active Templates: @stats.ActiveTemplates</p>
        
        <h4>By Category:</h4>
        <ul>
            @foreach (var category in stats.ByCategory)
            {
                <li>@category.Category: @category.Count</li>
            }
        </ul>
    </div>
}

<button @onclick="SeedTemplates" disabled="@isSeeding">
    @if (isSeeding)
    {
        <span>Seeding...</span>
    }
    else
    {
        <span>Seed Command Templates</span>
    }
</button>

@if (!string.IsNullOrEmpty(message))
{
    <div class="alert alert-@(isSuccess ? "success" : "danger")">
        @message
    </div>
}

@code {
    private TemplateStats? stats;
    private bool isSeeding = false;
    private string message = "";
    private bool isSuccess = false;

    protected override async Task OnInitializedAsync()
    {
        await LoadStats();
    }

    private async Task LoadStats()
    {
        var response = await Http.GetAsync("/api/admin/command-template-stats");
        if (response.IsSuccessStatusCode)
        {
            stats = await response.Content.ReadFromJsonAsync<TemplateStats>();
        }
    }

    private async Task SeedTemplates()
    {
        isSeeding = true;
        message = "";
        
        try
        {
            var response = await Http.PostAsync("/api/admin/seed-command-templates", null);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<SeedResult>();
                message = $"Successfully seeded {result.NewTemplates} new templates!";
                isSuccess = true;
                await LoadStats(); // Refresh stats
            }
            else
            {
                message = "Failed to seed templates. Check permissions.";
                isSuccess = false;
            }
        }
        catch (Exception ex)
        {
            message = $"Error: {ex.Message}";
            isSuccess = false;
        }
        finally
        {
            isSeeding = false;
        }
    }

    public class TemplateStats
    {
        public int TotalTemplates { get; set; }
        public int ActiveTemplates { get; set; }
        public List<CategoryCount> ByCategory { get; set; } = new();
    }

    public class CategoryCount
    {
        public string Category { get; set; }
        public int Count { get; set; }
    }

    public class SeedResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int NewTemplates { get; set; }
    }
}
```

## Security Considerations

1. **Admin Role Required**: Only users with the "Admin" role can access these endpoints
2. **Tenant Isolation**: Admin privileges are per-tenant, ensuring data isolation
3. **Idempotent Operations**: Seeding can be run multiple times safely
4. **Logging**: All admin operations are logged for audit purposes

## Workflow

1. **Initial Setup**:
   - Assign Admin role to appropriate users via SQL
   - Users log in to the frontend with their credentials

2. **First-Time Seeding**:
   - Admin navigates to the admin panel
   - Clicks "Seed Command Templates" button
   - System populates CommandTemplates table
   - Success message displays with count of templates added

3. **Subsequent Access**:
   - Admin can view statistics to confirm templates are loaded
   - Can re-run seeding if needed (no duplicates will be created)
   - Regular users can browse and apply templates via `/api/command-templates` endpoints

## Troubleshooting

### "403 Forbidden" Error
- User does not have Admin role
- Check UserTenants table for correct Role assignment
- Ensure user is logged in to correct tenant

### "Templates not appearing after seeding"
- Check IsActive flag on templates (should be true)
- Verify database connection is successful
- Review API logs for errors during seeding

### "Duplicate templates"
- This should not happen - seeding is idempotent
- Each template has a fixed GUID preventing duplicates
- If you see duplicates, check the template IDs

## Alternative: Script-Based Seeding

If you prefer to seed via command line (not recommended for production):

```bash
cd scripts/SeedCommandTemplates
dotnet run -- "Server=localhost;Database=ProdControlAV;..."
```

However, the admin API approach is preferred because:
- No direct database access required
- Integrated with application authentication
- Logged and auditable
- Can be triggered from frontend UI
- Safer for production environments
