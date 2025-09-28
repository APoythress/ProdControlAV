# ProdControlAV.WebApp - Blazor WebAssembly Frontend

The WebApp project is a Blazor WebAssembly application that provides a responsive web interface for monitoring and controlling A/V production equipment. It communicates with the ProdControlAV.API backend to deliver real-time device management capabilities through a modern single-page application.

## Architecture Role

ProdControlAV.WebApp serves as the **Presentation Layer** for web users:
- **Client-Side Rendering**: Blazor WebAssembly runs entirely in the browser
- **REST API Integration**: Communicates with ProdControlAV.API via HTTP requests
- **Real-Time Updates**: Live device status monitoring and updates
- **Responsive Design**: Optimized for desktop, tablet, and mobile devices
- **Single-Page Application**: Modern SPA experience with client-side routing

## Project Structure

### Pages (`/Pages`)

Blazor component pages for different application areas:

#### Core Application Pages
- **`Dashboard.razor`** - Main device status monitoring dashboard
  - Real-time device status cards with online/offline indicators
  - Quick device information and status overview
  - Modal dialogs for detailed device information
  - Automatic refresh for live status updates

#### Authentication Pages
- **`SignIn.razor`** - User authentication and login
- **`SignUp.razor`** - New user registration (if enabled)

#### Device Management
- **`/DeviceManagement/`** - Device administration and configuration
  - Device creation and editing forms
  - Bulk device operations
  - Device configuration management

#### Shared Components
- **`_Imports.razor`** - Global using statements and component imports

### Shared Components (`/Shared`)

Reusable UI components used across multiple pages:

- **`MainLayout.razor`** - Primary application layout with navigation
  - Header with user authentication status
  - Sidebar navigation menu
  - Main content area with responsive grid

- **`NavMenu.razor`** - Application navigation sidebar
  - Context-sensitive menu items based on user permissions
  - Current page highlighting
  - Collapsible mobile-friendly design

- **`NonHeaderLayout.razor`** - Layout for authentication pages without navigation

### Services (`/Services`)

Client-side services for API communication and business logic:

#### Device Management Services
- **`DeviceService.cs`** - Device data access and management
  - `GetAllDevicesAsync()` - Retrieve all devices for current user
  - `PingDeviceAsync()` - Test device connectivity
  - `CreateDeviceAsync()`, `UpdateDeviceAsync()`, `DeleteDeviceAsync()`

- **`DeviceManager.cs`** - Advanced device management operations
- **`DeviceStatusService.cs`** - Real-time device status monitoring

#### Authentication Services  
- **`PasskeyService.cs`** - Modern passwordless authentication support

### Models (`/Models`)

Client-side DTOs and view models for API communication:

- Device status DTOs for dashboard display
- User authentication models
- Configuration and settings models

### Static Assets (`/wwwroot`)

Static files served to the browser:
- CSS stylesheets and themes
- JavaScript interop files
- Images, icons, and branding assets
- Progressive Web App manifest

## Key Features

### Real-Time Device Dashboard

The main dashboard provides live monitoring of A/V equipment:

```razor
@page "/Dashboard"
@inject HttpClient Http
@inject DeviceApiClient deviceApiClient

<div class="container mt-5 text-white">
    <h3 class="text-center mb-4"><u>Device Status</u></h3>
    
    <div class="row g-3 justify-content-center">
        @foreach (var device in Devices)
        {
            <div class="col-12 col-sm-6 col-md-4 col-lg-3">
                <div class="card text-center bg-dark border-light text-white" 
                     @onclick="@(() => OpenDeviceModal(device))">
                    <div class="card-body">
                        <h5 class="card-title">@device.Name</h5>
                        <p class="card-text">
                            Status: <span class="badge @GetStatusClass(device)">
                                @(device.Status ? "online" : "offline")
                            </span>
                        </p>
                    </div>
                </div>
            </div>
        }
    </div>
</div>
```

#### Dashboard Features:
- **Live Status Cards**: Visual representation of each device's current status
- **Color-Coded Indicators**: Green for online, red for offline, yellow for warning states
- **Quick Actions**: Click device cards to view details or execute commands
- **Auto-Refresh**: Automatic status updates without page reload
- **Responsive Grid**: Adapts to screen size for optimal viewing on all devices

### Device Management Interface

Comprehensive device administration capabilities:

#### Device Configuration
- **Add New Devices**: Form-based device registration with validation
- **Edit Device Properties**: Modify device settings and network configuration
- **Device Types**: Support for cameras, mixers, displays, and network equipment
- **Location Management**: Organize devices by physical location or studio

#### Bulk Operations
- **Multi-Device Selection**: Checkbox-based device selection
- **Bulk Status Updates**: Update multiple devices simultaneously
- **Batch Commands**: Execute commands across multiple devices
- **Export/Import**: Device configuration backup and restore

### Authentication & User Management

Secure user authentication with modern standards:

#### Login Options
- **Username/Password**: Traditional authentication with secure password hashing
- **Passkey Support**: Modern passwordless authentication using WebAuthn
- **Remember Me**: Persistent login sessions with configurable expiration
- **Multi-Factor Authentication**: Optional 2FA for enhanced security

#### User Experience
- **Responsive Forms**: Mobile-optimized login and registration forms
- **Error Handling**: Clear error messages and validation feedback
- **Session Management**: Automatic logout on session expiration
- **Password Recovery**: Self-service password reset functionality

### Real-Time Updates

Live data synchronization without manual refresh:

#### Auto-Refresh Mechanisms
```csharp
public class DeviceStatusService
{
    private Timer? _refreshTimer;
    
    public async Task StartAutoRefresh()
    {
        _refreshTimer = new Timer(async _ =>
        {
            await RefreshDeviceStatus();
            StateHasChanged();
        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(10));
    }
}
```

#### Update Features:
- **Configurable Intervals**: Adjustable refresh rates for different data types
- **Efficient Updates**: Only changed data is fetched and updated
- **Background Processing**: Updates occur without interrupting user interaction
- **Connection State Monitoring**: Handles network disconnections gracefully

## API Integration

### HTTP Client Configuration

The WebApp communicates with the API backend through a configured HttpClient:

```csharp
// Program.cs
builder.Services.AddScoped(sp => new HttpClient 
{ 
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) 
});
```

#### Service Registration
- **Device Services**: Device management and status monitoring
- **Authentication Services**: User login and session management
- **Configuration Services**: Application settings and preferences

### REST API Communication

All API communication follows RESTful patterns:

#### Device Operations
```csharp
// GET /api/devices/devices
public async Task<List<DeviceStatusDto>> GetAllDevicesAsync()
{
    return await _http.GetFromJsonAsync<List<DeviceStatusDto>>("/api/devices/devices")
           ?? new List<DeviceStatusDto>();
}

// POST /api/devices
public async Task<bool> CreateDeviceAsync(Device device)
{
    var response = await _http.PostAsJsonAsync("/api/devices", device);
    return response.IsSuccessStatusCode;
}
```

#### Error Handling
- **HTTP Status Codes**: Proper handling of 200, 400, 401, 403, 500 responses
- **Timeout Management**: Configurable request timeouts with retry logic
- **Offline Support**: Graceful degradation when API is unavailable
- **User Notifications**: Clear error messages and recovery suggestions

### Authentication Integration

Cookie-based authentication shared with the API:

#### Authentication Flow
1. User submits credentials through SignIn page
2. WebApp posts to `/api/auth/login` endpoint
3. API sets secure authentication cookie
4. Subsequent API requests include authentication cookie automatically
5. WebApp redirects to Dashboard on successful authentication

#### Authorization
- **Route Protection**: Authenticated routes redirect to login if not authenticated
- **Role-Based Access**: Different UI elements based on user permissions
- **Tenant Isolation**: Users only see data for their authorized tenants

## Responsive Design

### CSS Framework Integration

The WebApp uses Bootstrap 5 for responsive design:

```html
<div class="col-12 col-sm-6 col-md-4 col-lg-3">
    <div class="card text-center bg-dark border-light text-white">
        <!-- Device status card -->
    </div>
</div>
```

#### Responsive Features:
- **Mobile-First Design**: Optimized for mobile devices with progressive enhancement
- **Flexible Grid System**: Bootstrap grid adapts to all screen sizes
- **Touch-Friendly Interface**: Appropriate touch targets and gesture support
- **Print Optimization**: CSS print styles for reporting and documentation

### Device-Specific Optimizations

#### Desktop Experience
- **Multi-Column Layouts**: Efficient use of large screen real estate
- **Keyboard Navigation**: Full keyboard accessibility for power users
- **Context Menus**: Right-click context menus for advanced operations
- **Drag-and-Drop**: Advanced interactions for device organization

#### Mobile Experience
- **Collapsible Navigation**: Hamburger menu for space efficiency
- **Swipe Gestures**: Touch-friendly navigation and interactions
- **Optimized Forms**: Mobile keyboards and input optimizations
- **Reduced Data Usage**: Efficient data loading for mobile networks

## Performance Optimization

### Client-Side Performance

Blazor WebAssembly performance optimizations:

#### Bundle Optimization
- **Tree Shaking**: Unused code eliminated from final bundle
- **Asset Compression**: Brotli compression for all static assets
- **Lazy Loading**: Components loaded on-demand to reduce initial bundle size
- **Prerendering**: Static generation for improved initial load times

#### Runtime Performance
- **Component Reuse**: Efficient component lifecycle management
- **State Management**: Minimized state updates and re-renders
- **Memory Management**: Proper disposal of resources and event handlers
- **Caching**: Browser caching for static assets and API responses

### Data Loading Strategies

#### Progressive Loading
```csharp
protected override async Task OnInitializedAsync()
{
    // Load critical data first
    await LoadDashboardSummary();
    
    // Load detailed data in background
    _ = Task.Run(async () =>
    {
        await LoadDetailedDeviceData();
        InvokeAsync(StateHasChanged);
    });
}
```

#### Caching Strategies:
- **Browser Cache**: Static assets cached with appropriate headers
- **Application Cache**: Device data cached client-side with TTL
- **API Response Caching**: Conditional requests using ETags
- **Offline Storage**: IndexedDB for offline capability

## Development Workflow

### Local Development

Running the WebApp requires the API backend:

```bash
# Start the API backend first
cd src/ProdControlAV.API
dotnet run

# The WebApp is served by the API at https://localhost:5001
# No separate startup required for WebApp
```

#### Development Features:
- **Hot Reload**: Automatic browser refresh on code changes
- **Debug Support**: Full debugging capability in Visual Studio/VS Code
- **Browser DevTools**: Standard web debugging tools available
- **Live Refresh**: CSS and Razor changes update immediately

### Build Process

The WebApp is built and integrated into the API project:

```bash
# Build WebApp (automatically included in API build)
dotnet build src/ProdControlAV.API

# Publish optimized bundle
dotnet publish src/ProdControlAV.API -c Release
```

#### Build Optimizations:
- **AOT Compilation**: Ahead-of-time compilation for improved performance
- **Bundle Minimization**: Minified JavaScript and CSS
- **Asset Optimization**: Compressed images and static assets
- **PWA Generation**: Service worker and app manifest generation

### Testing Strategy

#### Unit Testing
- **Component Testing**: Blazor component unit tests using bUnit
- **Service Testing**: HTTP client mocking for service layer tests
- **Logic Testing**: Pure C# business logic unit tests

#### Integration Testing
- **End-to-End Testing**: Full user workflow testing with Selenium
- **API Integration Testing**: Real API communication testing
- **Browser Testing**: Cross-browser compatibility testing

## Security Considerations

### Client-Side Security

Since Blazor WebAssembly runs in the browser, security follows web best practices:

#### Data Protection
- **No Sensitive Data**: Sensitive information never stored client-side
- **Token Security**: Authentication tokens handled securely
- **HTTPS Only**: All communication encrypted in transit
- **Content Security Policy**: CSP headers prevent XSS attacks

#### Authentication Security
- **Secure Cookies**: HTTP-only, secure cookies for authentication
- **CSRF Protection**: Anti-forgery tokens for state-changing operations
- **Session Management**: Configurable session timeouts
- **Logout Handling**: Secure session cleanup on logout

### Input Validation

Client-side validation with server-side verification:

```razor
<EditForm Model="@newDevice" OnValidSubmit="@CreateDevice">
    <DataAnnotationsValidator />
    <ValidationSummary />
    
    <div class="form-group">
        <label>Device Name</label>
        <InputText @bind-Value="newDevice.Name" class="form-control" />
        <ValidationMessage For="@(() => newDevice.Name)" />
    </div>
</EditForm>
```

#### Validation Features:
- **Data Annotations**: Model-based validation rules
- **Real-Time Validation**: Immediate feedback on input errors
- **Server Validation**: All client validation is verified server-side
- **Custom Validators**: Business rule validation for complex scenarios

## Integration with Other Projects

### ProdControlAV.API Integration
- **Hosted Deployment**: WebApp is served directly by the API project
- **Shared Authentication**: Same authentication cookies and sessions
- **REST Communication**: All data exchange via REST APIs
- **Real-Time Updates**: Polling-based updates for device status

### ProdControlAV.Core Integration
- **Shared Models**: Core domain models used for type safety
- **Business Logic**: Core interfaces define expected behaviors
- **Validation Rules**: Shared validation logic between client and server

## Deployment Considerations

### Production Deployment

The WebApp is deployed as part of the API project:

#### Static File Serving
- **Integrated Hosting**: API serves WebApp static files
- **CDN Support**: Static assets can be served from CDN for performance
- **Caching Headers**: Appropriate cache headers for different asset types
- **Compression**: Brotli and Gzip compression for all text assets

#### Progressive Web App Features
- **Service Worker**: Background data sync and offline capability
- **App Manifest**: Installation prompts for mobile devices
- **Push Notifications**: Real-time alerts for critical device status changes
- **Offline Support**: Basic functionality available without network connection

### Monitoring & Analytics

#### Application Monitoring
- **Performance Metrics**: Core Web Vitals and custom performance indicators
- **Error Tracking**: Client-side error reporting and analysis
- **User Analytics**: Usage patterns and feature adoption tracking
- **Network Monitoring**: API communication success rates and latencies

For detailed information about related projects, see:
- [Main README](../../README.md) - System overview and architecture
- [ProdControlAV.API](../ProdControlAV.API/README.md) - Backend API and hosting
- [ProdControlAV.Core](../ProdControlAV.Core/README.md) - Shared domain models
- [ProdControlAV.Agent](../ProdControlAV.Agent/README.md) - Device monitoring agent