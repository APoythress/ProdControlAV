# ProdControlAV - Production Control Audio/Visual System

A .NET 8 distributed system for monitoring and controlling audio/visual production equipment. The system consists of a Blazor WebAssembly frontend, ASP.NET Core Web API backend with Azure SQL DB, and a Raspberry Pi agent for device monitoring via ping/TCP probes.

Always reference these instructions first and fallback to search or bash commands only when you encounter unexpected information that does not match the info here.

## Working Effectively

### Prerequisites and Setup
- .NET 8 SDK is required (verified: .NET 8.0.119 works)
- No additional SDKs or tools are required for basic development
- Entity Framework tools are NOT installed by default (see optional tools below)

### Bootstrap, Build, and Test the Repository
- **Restore packages**: `dotnet restore` -- takes ~1 minute. NEVER CANCEL. Set timeout to 3+ minutes.
- **Build solution**: `dotnet build` -- takes ~30 seconds. NEVER CANCEL. Set timeout to 2+ minutes.
- **Run tests**: `dotnet test` -- takes ~15-20 seconds. NEVER CANCEL. Set timeout to 2+ minutes.

All commands must be run from the repository root directory.

### Development Workflow

#### Run the Web Application (API + Blazor WebAssembly)
- **Start API server**: 
  ```bash
  cd src/ProdControlAV.API
  dotnet run
  ```
  - API serves on: http://localhost:5000 and https://localhost:5001
  - Includes integrated Blazor WebAssembly frontend
  - Uses SQL database hosted in Azure SQL DB
  - Supports authentication and multi-tenancy

#### Run the Monitoring Agent (for Raspberry Pi)
- **Start agent**: 
  ```bash
  cd src/ProdControlAV.Agent
  dotnet run
  ```
  - Requires API key configuration in `appsettings.json`
  - Default config points to `https://localhost:5001/api`
  - Agent monitors devices via ICMP ping and TCP probes

### Publishing for Deployment

#### Publish Web Application
```bash
dotnet publish src/ProdControlAV.API/ProdControlAV.API.csproj -c Release -o ./publish/api
```
- Takes ~1 minute 30 seconds. NEVER CANCEL. Set timeout to 3+ minutes.
- Includes optimized Blazor WebAssembly assets
- Self-contained deployment ready for production

#### Publish Raspberry Pi Agent
```bash
dotnet publish src/ProdControlAV.Agent/ProdControlAV.Agent.csproj -c Release -r linux-arm64 --self-contained true -o ./publish/agent-pi
```
- Cross-platform publish for ARM64 Raspberry Pi
- Self-contained with all dependencies included
- Note: May have dependency conflicts when building alongside API project

## Validation

### Manual Testing Requirements
- **ALWAYS test the complete application flow after making changes**
- **Start the API server** and verify it serves the Blazor WebAssembly app at https://localhost:5001
- **Test authentication flow** if authentication changes are made
- **Test device monitoring scenarios** if agent or device-related changes are made
- **Validate configuration** by reviewing appsettings.json files for valid API endpoints and keys

### Build Validation
- Always run `dotnet build` before committing changes
- Address nullable reference warnings if adding new models or APIs
- Route conflict warnings in API controllers are known issues and can be ignored

### Test Validation
- Always run `dotnet test` before committing changes
- The test suite includes comprehensive unit tests covering core agent functionality, device monitoring, configuration, and API services
- Tests use xUnit and Moq frameworks

## Project Structure

### Key Projects in the Solution
1. **ProdControlAV.Core** - Domain models and interfaces (shared library)
2. **ProdControlAV.Infrastructure** - Infrastructure layer implementations
3. **ProdControlAV.API** - ASP.NET Core Web API backend with integrated Blazor hosting
4. **ProdControlAV.WebApp** - Blazor WebAssembly frontend application
5. **ProdControlAV.Agent** - Worker service for Raspberry Pi device monitoring
6. **ProdControlAV.Tests** - Unit tests using xUnit and Moq

### Important Files and Locations
- **Solution file**: `ProdControlAV.sln` (contains all projects)
- **API configuration**: `src/ProdControlAV.API/appsettings.json`
- **Agent configuration**: `src/ProdControlAV.Agent/appsettings.json`
- **Database context**: `src/ProdControlAV.API/Data/AppDbContext.cs`
- **Agent README**: `src/ProdControlAV.Agent/README.md` (detailed deployment instructions)

### Architecture Overview
- **Frontend**: Blazor WebAssembly (runs in browser)
- **Backend**: ASP.NET Core Web API with Entity Framework Core
- **Database**: Azure SQL DB (development and production)
- **Authentication**: Cookie-based with multi-tenant support
- **Monitoring**: Raspberry Pi agent with ICMP/TCP probes
- **Communication**: REST API between agent and backend

## Common Development Tasks

### Working with the Database
- Azure SQL DB
- **Entity Framework tools are NOT installed by default**
- To install EF tools: `dotnet tool install --global dotnet-ef`
- Connection string in `src/ProdControlAV.API/appsettings.json`

### Adding New Device Types
- Update models in `ProdControlAV.Core/Models/`
- Add controller endpoints in `ProdControlAV.API/Controllers/`
- Update agent configuration for new device probing logic

### Modifying Authentication
- Authentication logic in `ProdControlAV.API/Auth/`
- Cookie authentication with tenant isolation
- User management in `ProdControlAV.API/Controllers/AuthController.cs`

### Raspberry Pi Deployment
- Follow detailed instructions in `src/ProdControlAV.Agent/README.md`
- Requires systemd service setup and network capabilities
- Agent runs as non-root user with CAP_NET_RAW for ICMP

## Build and Timing Expectations

### Command Timeouts (Critical for CI/CD)
- **dotnet restore**: 1-2 minutes typical, SET TIMEOUT: 3+ minutes. NEVER CANCEL.
- **dotnet build**: 30 seconds typical, SET TIMEOUT: 2+ minutes. NEVER CANCEL.
- **dotnet test**: 15 seconds typical, SET TIMEOUT: 2+ minutes. NEVER CANCEL.
- **dotnet publish (API)**: 1.5 minutes typical, SET TIMEOUT: 3+ minutes. NEVER CANCEL.
- **dotnet publish (Agent)**: Variable due to ARM64 cross-compile, SET TIMEOUT: 5+ minutes. NEVER CANCEL.

### Expected Warnings
- Nullable reference warnings in Core models (known issue, can be ignored)
- Blazor WebAssembly optimization warnings (informational)
- Route conflict warnings in CommandController (known issue, can be ignored)

## Troubleshooting

### Common Issues
- **"dotnet-ef not found"**: Install Entity Framework tools globally
- **Agent fails to start**: Check API key in `appsettings.json`
- **Build conflicts during Agent publish**: Remove project references temporarily
- **HTTPS certificate warnings**: Use development certificate or configure for production

### Development Environment
- Uses Azure SQL DB for both development and production
- No external dependencies required
- CORS configured for localhost development
- Authentication uses secure cookies

## Optional Tools

### Entity Framework Tools
```bash
dotnet tool install --global dotnet-ef
```
Enables database migrations and schema management.

### Blazor WebAssembly Optimization
```bash
dotnet workload install wasm-tools
```
Improves Blazor WebAssembly publishing performance and output size.

Remember: Always build and test your changes thoroughly. The system integrates multiple components that must work together correctly.