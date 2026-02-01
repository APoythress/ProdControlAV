# UI/UX Changes - Architecture Planning Guide

## Overview

This document guides coding agents through UI/UX changes in ProdControlAV's Blazor WebAssembly frontend. The application emphasizes real-time monitoring, responsive design, and multi-tenant data isolation.

## Core UI/UX Principles

### 1. Performance First
- **Minimize API calls**: Cache data, use SignalR for real-time updates
- **Lazy loading**: Load components and data on demand
- **Efficient rendering**: Use `@key` directives, avoid unnecessary re-renders
- **Table Storage reads**: Dashboard loads from fast Table Storage, not SQL

### 2. Real-Time Updates
- **Auto-refresh**: Dashboard polls every 30 seconds for device status
- **Visual indicators**: Show loading states, errors, and success feedback
- **Optimistic updates**: Update UI immediately, sync with server in background
- **No SQL queries**: All dashboard reads from Azure Table Storage

### 3. Multi-Tenant UI
- **Tenant context**: Always show current tenant name
- **Data isolation**: Never show data from other tenants
- **Tenant switching**: Support users with multiple tenant memberships
- **Scoped navigation**: All routes scoped to current tenant

### 4. Responsive Design
- **Mobile-first**: Design for mobile, enhance for desktop
- **Grid layouts**: Use CSS Grid for flexible layouts
- **Touch-friendly**: Large touch targets (44x44px minimum)
- **Breakpoints**: sm: 640px, md: 768px, lg: 1024px, xl: 1280px

## Project Structure

```
src/ProdControlAV.WebApp/
├── Pages/                    # Routable pages
│   ├── Dashboard.razor       # Main device monitoring
│   ├── Settings.razor        # Device configuration
│   ├── Commands.razor        # Command execution
│   └── DeviceManagement/     # Device CRUD operations
├── Components/               # Reusable components
│   ├── Ui/                   # UI primitives (cards, buttons, modals)
│   └── Shared/               # Shared business components
├── Services/                 # Client-side services
│   ├── ApiClient.cs          # HTTP client wrapper
│   └── CacheService.cs       # Client-side caching
├── Models/                   # DTOs and view models
└── wwwroot/                  # Static assets
    ├── css/                  # Stylesheets
    └── js/                   # JavaScript interop
```

## Adding a New Page

### Step-by-Step Process

1. **Create Page Component** in `Pages/`
   ```razor
   @page "/your-page"
   @using System.Net.Http.Json
   @inject HttpClient Http
   @inject IJSRuntime JsRuntime
   
   <PageTitle>Your Page - ProdControlAV</PageTitle>
   
   <div class="container mt-5">
       <h3 class="text-center mb-4">Your Page Title</h3>
       
       @if (_isLoading)
       {
           <div class="text-center">
               <div class="spinner-border" role="status">
                   <span class="visually-hidden">Loading...</span>
               </div>
           </div>
       }
       else if (_data != null)
       {
           <!-- Your UI here -->
       }
   </div>
   
   @code {
       private bool _isLoading = true;
       private YourData? _data;
       
       protected override async Task OnInitializedAsync()
       {
           await LoadDataAsync();
       }
       
       private async Task LoadDataAsync()
       {
           try
           {
               _isLoading = true;
               _data = await Http.GetFromJsonAsync<YourData>("api/your-endpoint");
           }
           catch (Exception ex)
           {
               Console.Error.WriteLine($"Error loading data: {ex.Message}");
               await JsRuntime.InvokeVoidAsync("alert", "Failed to load data");
           }
           finally
           {
               _isLoading = false;
           }
       }
   }
   ```

2. **Add Navigation Link** in `Shared/NavMenu.razor`
   ```razor
   <li class="nav-item">
       <a class="nav-link" href="/your-page">
           <span class="icon">🔧</span>
           Your Page
       </a>
   </li>
   ```

3. **Create Required DTOs** in `Models/`
   ```csharp
   public record YourData(Guid Id, string Name, DateTimeOffset CreatedUtc);
   ```

4. **Test Navigation**
   - Build: `dotnet build`
   - Run: `cd src/ProdControlAV.API && dotnet run`
   - Navigate to: https://localhost:5001/your-page

## Adding a New UI Component

### Reusable Component Pattern

Create components in `Components/Ui/` for reusable UI elements:

```razor
@* Components/Ui/UiYourComponent.razor *@

<div class="your-component @CssClass">
    @ChildContent
</div>

@code {
    [Parameter]
    public string CssClass { get; set; } = "";
    
    [Parameter]
    public RenderFragment? ChildContent { get; set; }
    
    [Parameter]
    public EventCallback OnClick { get; set; }
}
```

### Using the Component

```razor
<UiYourComponent CssClass="mt-3" OnClick="@HandleClick">
    <p>Content goes here</p>
</UiYourComponent>

@code {
    private async Task HandleClick()
    {
        // Handle click event
    }
}
```

## Common UI Patterns

### 1. Loading State

```razor
@if (_isLoading)
{
    <div class="text-center py-5">
        <div class="spinner-border text-primary" role="status">
            <span class="visually-hidden">Loading...</span>
        </div>
        <p class="mt-2 text-muted">Loading data...</p>
    </div>
}
else if (_data != null)
{
    <!-- Render data -->
}
else
{
    <div class="alert alert-warning" role="alert">
        No data available
    </div>
}
```

### 2. Error Handling

```razor
@code {
    private string? _errorMessage;
    
    private async Task LoadDataAsync()
    {
        try
        {
            _errorMessage = null;
            _data = await Http.GetFromJsonAsync<YourData>("api/endpoint");
        }
        catch (HttpRequestException ex)
        {
            _errorMessage = $"Network error: {ex.Message}";
        }
        catch (Exception ex)
        {
            _errorMessage = $"Unexpected error: {ex.Message}";
            Console.Error.WriteLine(ex);
        }
    }
}

@if (!string.IsNullOrEmpty(_errorMessage))
{
    <div class="alert alert-danger alert-dismissible fade show" role="alert">
        <strong>Error:</strong> @_errorMessage
        <button type="button" class="btn-close" @onclick="@(() => _errorMessage = null)"></button>
    </div>
}
```

### 3. Form Handling

```razor
<EditForm Model="@_deviceForm" OnValidSubmit="@HandleSubmit">
    <DataAnnotationsValidator />
    <ValidationSummary />
    
    <div class="mb-3">
        <label for="deviceName" class="form-label">Device Name</label>
        <InputText id="deviceName" class="form-control" @bind-Value="_deviceForm.Name" />
        <ValidationMessage For="@(() => _deviceForm.Name)" />
    </div>
    
    <div class="mb-3">
        <label for="deviceIp" class="form-label">IP Address</label>
        <InputText id="deviceIp" class="form-control" @bind-Value="_deviceForm.Ip" />
        <ValidationMessage For="@(() => _deviceForm.Ip)" />
    </div>
    
    <button type="submit" class="btn btn-primary" disabled="@_isSubmitting">
        @if (_isSubmitting)
        {
            <span class="spinner-border spinner-border-sm me-2" role="status"></span>
        }
        Submit
    </button>
</EditForm>

@code {
    private DeviceForm _deviceForm = new();
    private bool _isSubmitting;
    
    private async Task HandleSubmit()
    {
        try
        {
            _isSubmitting = true;
            var response = await Http.PostAsJsonAsync("api/devices", _deviceForm);
            response.EnsureSuccessStatusCode();
            
            await JsRuntime.InvokeVoidAsync("alert", "Device created successfully");
            // Navigate or refresh
        }
        catch (Exception ex)
        {
            await JsRuntime.InvokeVoidAsync("alert", $"Error: {ex.Message}");
        }
        finally
        {
            _isSubmitting = false;
        }
    }
}
```

### 4. Modal Dialogs

```razor
<UiModal @bind-IsOpen="_isModalOpen" Title="Confirm Action" Size="md">
    <ChildContent>
        <p>Are you sure you want to perform this action?</p>
    </ChildContent>
    <FooterContent>
        <button class="btn btn-secondary" @onclick="@(() => _isModalOpen = false)">
            Cancel
        </button>
        <button class="btn btn-danger" @onclick="@ConfirmAction">
            Confirm
        </button>
    </FooterContent>
</UiModal>

@code {
    private bool _isModalOpen;
    
    private void OpenModal()
    {
        _isModalOpen = true;
    }
    
    private async Task ConfirmAction()
    {
        _isModalOpen = false;
        // Perform action
    }
}
```

### 5. Real-Time Updates

```razor
@implements IAsyncDisposable

@code {
    private Timer? _refreshTimer;
    private const int RefreshIntervalSeconds = 30;
    
    protected override void OnInitialized()
    {
        // Auto-refresh every 30 seconds
        _refreshTimer = new Timer(async _ =>
        {
            await InvokeAsync(async () =>
            {
                await LoadDataAsync();
                StateHasChanged();
            });
        }, null, TimeSpan.FromSeconds(RefreshIntervalSeconds), TimeSpan.FromSeconds(RefreshIntervalSeconds));
    }
    
    public async ValueTask DisposeAsync()
    {
        if (_refreshTimer != null)
        {
            await _refreshTimer.DisposeAsync();
        }
    }
}
```

### 6. Device Status Cards (Dashboard Pattern)

```razor
<div class="ui-grid cols-4">
    @foreach (var device in _devices)
    {
        <UiCard Title="@device.Name"
                Variant="@GetDeviceCardVariant(device)"
                Class="device-card"
                @onclick="@(() => OpenDeviceModal(device))">
            
            <div class="row between">
                <span class="muted">Status</span>
                <UiBadge Color="@GetDeviceStatusColor(device)">
                    @(device.Status ? "online" : "offline")
                </UiBadge>
            </div>
            
            @if (device.LastSeenUtc.HasValue)
            {
                <div class="row between mt-2">
                    <span class="muted">Last Seen</span>
                    <span class="small">@FormatRelativeTime(device.LastSeenUtc.Value)</span>
                </div>
            }
        </UiCard>
    }
</div>

@code {
    private string GetDeviceCardVariant(DeviceDto device)
    {
        return device.Status ? "card-glow-green" : "card-glow-orange";
    }
    
    private string GetDeviceStatusColor(DeviceDto device)
    {
        return device.Status ? "green" : "red";
    }
    
    private string FormatRelativeTime(DateTimeOffset time)
    {
        var elapsed = DateTimeOffset.UtcNow - time;
        if (elapsed.TotalMinutes < 1) return "Just now";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
        return $"{(int)elapsed.TotalDays}d ago";
    }
}
```

## API Communication

### HTTP Client Pattern

```csharp
// Services/ApiClient.cs
public class ApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<ApiClient> _logger;
    
    public ApiClient(HttpClient http, ILogger<ApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }
    
    public async Task<List<DeviceDto>?> GetDevicesAsync()
    {
        try
        {
            var response = await _http.GetAsync("api/devices/devices");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<DeviceDto>>();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch devices");
            return null;
        }
    }
    
    public async Task<bool> CreateDeviceAsync(CreateDeviceRequest request)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/devices", request);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to create device");
            return false;
        }
    }
}
```

### Using ApiClient in Components

```razor
@inject ApiClient Api

@code {
    protected override async Task OnInitializedAsync()
    {
        var devices = await Api.GetDevicesAsync();
        if (devices != null)
        {
            _devices = devices;
        }
    }
}
```

## Styling Guidelines

### CSS Architecture

```
wwwroot/css/
├── app.css              # Main stylesheet
├── components/          # Component-specific styles
│   ├── cards.css
│   ├── buttons.css
│   └── modals.css
└── utilities/           # Utility classes
    ├── spacing.css
    ├── colors.css
    └── typography.css
```

### Component Scoping

```razor
<div class="device-card">
    <h4>@Title</h4>
</div>

<style>
    .device-card {
        border: 1px solid #ddd;
        border-radius: 8px;
        padding: 1rem;
        cursor: pointer;
        transition: all 0.2s ease;
    }
    
    .device-card:hover {
        transform: translateY(-2px);
        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
    }
</style>
```

### Color Palette (Consistent with Existing UI)

```css
:root {
    --color-primary: #0d6efd;
    --color-success: #198754;
    --color-warning: #ffc107;
    --color-danger: #dc3545;
    --color-info: #0dcaf0;
    
    --color-green: #22c55e;
    --color-red: #ef4444;
    --color-orange: #f97316;
    --color-purple: #a855f7;
    
    --bg-dark: #1a1a1a;
    --bg-card: #2a2a2a;
    --text-muted: #9ca3af;
}
```

### Responsive Grid

```css
.ui-grid {
    display: grid;
    gap: 1rem;
    grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
}

.ui-grid.cols-2 {
    grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
}

.ui-grid.cols-4 {
    grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
}

@media (max-width: 768px) {
    .ui-grid {
        grid-template-columns: 1fr;
    }
}
```

## JavaScript Interop

### Calling JavaScript from Blazor

```razor
@inject IJSRuntime JsRuntime

@code {
    private async Task ShowNotification(string message)
    {
        await JsRuntime.InvokeVoidAsync("showToast", message);
    }
    
    private async Task<string> GetLocalStorageItem(string key)
    {
        return await JsRuntime.InvokeAsync<string>("localStorage.getItem", key);
    }
}
```

### JavaScript Functions

```javascript
// wwwroot/js/app.js
window.showToast = function(message) {
    // Use your preferred toast library
    alert(message);
};

window.copyToClipboard = function(text) {
    navigator.clipboard.writeText(text);
};

window.downloadFile = function(fileName, contentBase64) {
    const link = document.createElement('a');
    link.download = fileName;
    link.href = `data:application/octet-stream;base64,${contentBase64}`;
    link.click();
};
```

## Performance Optimization

### 1. Virtualization for Large Lists

```razor
@using Microsoft.AspNetCore.Components.Web.Virtualization

<Virtualize Items="_devices" Context="device">
    <DeviceCard Device="@device" />
</Virtualize>
```

### 2. Lazy Loading Components

```razor
@code {
    private bool _showExpensiveComponent;
}

@if (_showExpensiveComponent)
{
    <ExpensiveComponent />
}
```

### 3. Debouncing Input

```razor
<input type="text" @oninput="@OnSearchInput" />

@code {
    private Timer? _debounceTimer;
    private string _searchTerm = "";
    
    private void OnSearchInput(ChangeEventArgs e)
    {
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(async _ =>
        {
            await InvokeAsync(async () =>
            {
                _searchTerm = e.Value?.ToString() ?? "";
                await SearchAsync(_searchTerm);
                StateHasChanged();
            });
        }, null, 500, Timeout.Infinite);
    }
}
```

### 4. Memoization

```razor
@code {
    private List<DeviceDto> _devices = new();
    private List<DeviceDto> _filteredDevices = new();
    private string _lastFilter = "";
    
    private List<DeviceDto> GetFilteredDevices(string filter)
    {
        // Only recompute if filter changed
        if (_lastFilter != filter)
        {
            _lastFilter = filter;
            _filteredDevices = _devices
                .Where(d => d.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        return _filteredDevices;
    }
}
```

## Accessibility

### ARIA Labels

```razor
<button aria-label="Close modal" @onclick="@CloseModal">
    <span aria-hidden="true">&times;</span>
</button>

<div role="status" aria-live="polite" aria-atomic="true">
    @if (_isLoading)
    {
        <span class="visually-hidden">Loading...</span>
    }
</div>
```

### Keyboard Navigation

```razor
<div tabindex="0" @onkeydown="@HandleKeyDown">
    <!-- Interactive content -->
</div>

@code {
    private void HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" || e.Key == " ")
        {
            // Activate
        }
        else if (e.Key == "Escape")
        {
            // Close modal
        }
    }
}
```

### Focus Management

```razor
@inject IJSRuntime JsRuntime

@code {
    private async Task OpenModal()
    {
        _isModalOpen = true;
        await Task.Delay(100); // Wait for render
        await JsRuntime.InvokeVoidAsync("focusElement", "#modal-close-button");
    }
}
```

## Testing UI Components

### Component Test Pattern

```csharp
public class DeviceCardTests : TestContext
{
    [Fact]
    public void DeviceCard_RendersCorrectly()
    {
        // Arrange
        var device = new DeviceDto
        {
            Id = Guid.NewGuid(),
            Name = "Test Device",
            Status = true
        };
        
        // Act
        var component = RenderComponent<DeviceCard>(parameters => parameters
            .Add(p => p.Device, device));
        
        // Assert
        component.Find("h4").TextContent.Should().Be("Test Device");
        component.Find(".badge").TextContent.Should().Be("online");
    }
    
    [Fact]
    public async Task DeviceCard_HandlesClick()
    {
        // Arrange
        var clicked = false;
        var component = RenderComponent<DeviceCard>(parameters => parameters
            .Add(p => p.Device, new DeviceDto())
            .Add(p => p.OnClick, () => { clicked = true; }));
        
        // Act
        component.Find(".device-card").Click();
        
        // Assert
        clicked.Should().BeTrue();
    }
}
```

## Common Pitfalls

### ❌ Avoid These Anti-Patterns

1. **Querying SQL from UI**
   ```razor
   @* ❌ BAD - UI should never query SQL directly *@
   @inject AppDbContext _db
   ```
   
   **Fix**: Use API endpoints that read from Table Storage

2. **Not handling loading states**
   ```razor
   @* ❌ BAD - renders before data loads *@
   <div>@_data.Name</div>
   ```
   
   **Fix**: Check for null and show loading indicator
   ```razor
   @if (_isLoading)
   {
       <LoadingSpinner />
   }
   else if (_data != null)
   {
       <div>@_data.Name</div>
   }
   ```

3. **Memory leaks from timers**
   ```razor
   @* ❌ BAD - timer never disposed *@
   @code {
       protected override void OnInitialized()
       {
           _timer = new Timer(async _ => { await Refresh(); }, null, 0, 30000);
       }
   }
   ```
   
   **Fix**: Implement IAsyncDisposable
   ```razor
   @implements IAsyncDisposable
   
   @code {
       public async ValueTask DisposeAsync()
       {
           if (_timer != null)
               await _timer.DisposeAsync();
       }
   }
   ```

4. **Excessive API calls**
   ```razor
   @* ❌ BAD - calls API on every render *@
   @code {
       protected override async Task OnParametersSetAsync()
       {
           await LoadDataAsync(); // Called too frequently
       }
   }
   ```
   
   **Fix**: Use OnInitializedAsync and manual refresh
   ```razor
   @code {
       protected override async Task OnInitializedAsync()
       {
           await LoadDataAsync(); // Called once
       }
   }
   ```

## Deployment Considerations

### Build Optimization

```bash
# Production build with optimizations
dotnet publish src/ProdControlAV.API/ProdControlAV.API.csproj -c Release -o ./publish/api

# Blazor WebAssembly is included and optimized automatically
```

### Static Asset Caching

```csharp
// In Program.cs
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.Append("Cache-Control", "public,max-age=31536000");
    }
});
```

### Progressive Web App (PWA)

The app can be configured as a PWA by adding:
1. `manifest.json` in `wwwroot/`
2. Service worker in `wwwroot/service-worker.js`
3. Icons in various sizes

## Checklist for UI Changes

Before deploying UI changes:

- [ ] Component renders correctly on all screen sizes (mobile, tablet, desktop)
- [ ] Loading states handled properly
- [ ] Error states handled and displayed to user
- [ ] API calls use Table Storage endpoints (not SQL)
- [ ] Multi-tenant data isolation verified
- [ ] Accessibility tested (keyboard navigation, screen readers)
- [ ] Performance tested (no excessive re-renders)
- [ ] Browser compatibility tested (Chrome, Firefox, Safari, Edge)
- [ ] Console errors reviewed and fixed
- [ ] Timers and subscriptions properly disposed
- [ ] User feedback provided for all actions
- [ ] Documentation updated

## References

- [Blazor Documentation](https://learn.microsoft.com/en-us/aspnet/core/blazor/)
- [Dashboard Component](../../src/ProdControlAV.WebApp/Pages/Dashboard.razor)
- [UI Components](../../src/ProdControlAV.WebApp/Components/Ui/)
- [API Endpoints](../SQL_ELIMINATION_GUIDE.md)
