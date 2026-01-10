# ProdControlAV - Production Control Audio/Visual System

A comprehensive .NET 8 distributed system designed for monitoring and controlling audio/visual production equipment. The system provides real-time device monitoring, centralized control, and multi-tenant management capabilities for professional A/V environments.

## System Architecture

```
┌─────────────────────┐    HTTPS/REST API    ┌─────────────────────┐
│                     │◄────────────────────►│                     │
│ ProdControlAV.Agent │                      │  ProdControlAV.API  │
│  (Raspberry Pi)     │                      │   (Web Server)      │
│                     │                      │                     │
└─────────────────────┘                      └─────────────────────┘
         │                                            │
         │ ICMP/TCP Probes                           │ Entity Framework
         ▼                                            ▼
┌─────────────────────┐                      ┌─────────────────────┐
│   A/V Equipment     │                      │   Azure SQL DB      │
│  (Cameras, Mixers,  │                      │  (Multi-tenant)     │
│   Switches, etc.)   │                      │                     │
└─────────────────────┘                      └─────────────────────┘
                                                       ▲
                                                       │ HTTPS
                                                       ▼
                                            ┌─────────────────────┐
                                            │ ProdControlAV.WebApp│
                                            │ (Blazor WebAssembly)│
                                            │                     │
                                            └─────────────────────┘
```

## Project Overview

### Core Projects

- **[ProdControlAV.Core](src/ProdControlAV.Core/README.md)** - Domain models, entities, and service interfaces that define the business logic and contracts used across all projects.

- **[ProdControlAV.Infrastructure](src/ProdControlAV.Infrastructure/README.md)** - Service implementations for external integrations including device communication, network monitoring, and command processing.

- **[ProdControlAV.API](src/ProdControlAV.API/README.md)** - ASP.NET Core Web API backend providing REST endpoints, authentication, multi-tenant data isolation, and Blazor WebAssembly hosting.

- **[ProdControlAV.WebApp](src/ProdControlAV.WebApp/README.md)** - Blazor WebAssembly frontend application providing a responsive web interface for device management and monitoring.

- **[ProdControlAV.Agent](src/ProdControlAV.Agent/README.md)** - .NET Worker Service for Raspberry Pi that performs device monitoring via ICMP ping and TCP probes, communicating with the API server.

### Key Features

#### 🎯 Per-Device Ping Frequency Configuration (NEW)
Configure individual monitoring intervals for each device to optimize database costs and system performance:
- **Cost Optimization**: Reduce Azure SQL DB operations by up to 65%
- **Flexible Scheduling**: Set frequencies from 5 seconds to 1 hour per device
- **Smart Polling**: Agent only pings devices when their interval has elapsed
- **Easy Configuration**: Web UI for managing frequencies via Settings page

See [Ping Frequency Optimization Guide](docs/PING_FREQUENCY_OPTIMIZATION.md) for details.

#### 🎬 ATEM Video Switcher Control (NEW)
Full integration with Blackmagic Design ATEM video switchers:
- **Program Control**: Cut or fade transitions to any input
- **Preview Control**: Independent preview input management
- **Macro Execution**: List and run ATEM macros remotely
- **State Monitoring**: Real-time tracking of program/preview sources
- **Auto-Reconnect**: Resilient connection with exponential backoff

See [ATEM Integration Guide](src/ProdControlAV.Agent/docs/ATEM-INTEGRATION.md) for setup and usage.

#### 🔍 Real-Time Device Monitoring
- ICMP ping and TCP probe support for comprehensive device health checks
- Automatic state change detection with configurable thresholds
- Historical status logging with timestamp tracking
- Multi-device concurrent monitoring with configurable concurrency limits

#### 🏢 Multi-Tenant Architecture
- Complete data isolation between organizations
- Tenant-specific device management and monitoring
- Secure authentication with per-tenant access control
- Scalable to support multiple organizations on single deployment

#### 🤖 Raspberry Pi Agent
- Lightweight monitoring agent for edge deployment
- Cross-platform ARM64 support for Raspberry Pi 5
- Automatic device discovery and registration
- Resilient communication with automatic retry logic

## How Projects Work Together

### Data Flow

1. **Device Monitoring**: The Agent continuously monitors A/V devices using ping/TCP probes
2. **Status Reporting**: Agent reports device status changes to the API via secure HTTPS endpoints
3. **Data Storage**: API persists device status and configuration in Azure SQL DB with tenant isolation
4. **Web Interface**: WebApp retrieves device information from API and displays real-time status
5. **Device Control**: Users issue commands through WebApp, which sends them to API, then to Agent for execution

### Communication Patterns

- **Agent ↔ API**: REST API with API key authentication over HTTPS
- **WebApp ↔ API**: REST API with cookie-based authentication and CSRF protection
- **API ↔ Database**: Entity Framework Core with SQL Server for multi-tenant data access
- **Agent ↔ Devices**: Direct ICMP ping and TCP socket connections for monitoring

### Authentication & Security

- **Multi-tenant Architecture**: Each organization has isolated data through tenant-based filtering
- **API Key Authentication**: Agents authenticate using secure 32+ character API keys
- **Cookie Authentication**: Web users authenticate via secure HTTP-only cookies
- **HTTPS Only**: All external communications use TLS encryption
- **Network Security**: Agents run with minimal privileges and restricted network capabilities

## Quick Start

### Prerequisites
- .NET 8 SDK
- SQL Server or Azure SQL Database
- Raspberry Pi 5 with Pi OS 64-bit (for agent deployment)

### Development Setup

1. **Clone and Build**:
   ```bash
   git clone <repository-url>
   cd ProdControlAV
   dotnet restore
   dotnet build
   dotnet test
   ```

2. **Run the Web Application** (API + WebApp):
   ```bash
   cd src/ProdControlAV.API
   dotnet run
   ```
   Access the application at: https://localhost:5001

3. **Run the Agent** (requires API configuration):
   ```bash
   cd src/ProdControlAV.Agent
   # Configure API endpoint and key in appsettings.json
   dotnet run
   ```

## Deployment

### Web Application Deployment
- **Target Environment**: Any server supporting .NET 8 (Windows/Linux)
- **Database**: Azure SQL Database with automated deployments
- **Hosting**: Self-contained deployment with integrated Blazor WebAssembly
- **SSL/TLS**: Production requires valid SSL certificates for HTTPS

### Agent Deployment
- **Target Platform**: Raspberry Pi 5 with Pi OS 64-bit Lite
- **Deployment Method**: Automated scripts for cross-compilation and SSH deployment
- **Service Management**: Systemd service with automatic startup and monitoring
- **Security**: Runs as unprivileged user with minimal system capabilities

For detailed deployment instructions, see:
- [Web Application Deployment](DEPLOYMENT.md)
- [Agent Deployment](src/ProdControlAV.Agent/README.md)
- [Deployment Scripts](scripts/README.md)

## Infrastructure Overview

### Hosting Environment
- **Web Tier**: ASP.NET Core hosted on cloud infrastructure with load balancing
- **Database Tier**: Azure SQL Database with automated backups and high availability
- **Edge Monitoring**: Raspberry Pi agents deployed on-premises for direct device access
- **CDN**: Static assets served via content delivery network for optimal performance

### Scalability
- **Horizontal Scaling**: Multiple agent instances can monitor different device clusters
- **Database Scaling**: Azure SQL supports automatic scaling based on workload
- **Multi-tenancy**: Single deployment supports multiple isolated organizations
- **Load Distribution**: API endpoints designed for stateless operation

### Monitoring & Observability
- **Application Logging**: Structured logging with configurable log levels
- **Performance Metrics**: Built-in ASP.NET Core metrics and health checks
- **Device Status**: Real-time monitoring with configurable alert thresholds
- **System Health**: Comprehensive monitoring of agent connectivity and API performance

## Security Considerations

### Data Protection
- **Encryption in Transit**: All communications use TLS 1.2+ encryption
- **Encryption at Rest**: Azure SQL Database provides transparent data encryption
- **Tenant Isolation**: Row-level security ensures data separation between organizations
- **API Security**: Rate limiting, request validation, and secure headers

### Access Control
- **Authentication**: Multi-factor authentication support for web users
- **Authorization**: Role-based access control with granular permissions
- **API Keys**: Secure key generation and rotation for agent authentication
- **Network Security**: Firewall rules and network segmentation recommendations

### Compliance
- **Data Residency**: Configurable data storage regions for compliance requirements
- **Audit Logging**: Complete audit trail of all system actions and changes
- **Privacy**: GDPR-compliant data handling and user consent management

## Contributing

### Development Workflow
1. Follow the existing code patterns and conventions
2. Ensure all tests pass: `dotnet test`
3. Document any new features or breaking changes
4. Submit pull requests with comprehensive descriptions

### Testing Strategy
- **Unit Tests**: Core business logic and service implementations
- **Integration Tests**: Database operations and external service interactions
- **End-to-End Tests**: Complete user workflows and system integration

### Code Quality
- **Static Analysis**: Built-in .NET analyzers with nullable reference types
- **Security Scanning**: Regular dependency vulnerability assessments
- **Performance Monitoring**: Continuous performance regression testing

## License

This project is proprietary software. See the license agreement for usage terms and restrictions.

## Support

For technical support, feature requests, or security issues, please contact the development team through the appropriate channels outlined in your organization's support documentation.

# Azure Table Storage Migration (Device Status)

## Test Coverage
- Unit tests for TableDeviceStatusStore and StatusController (partitioning, upsert, claim validation, multi-tenant isolation)
- Integration tests for TableDeviceStatusStore using Azurite

## Integration Requirements
- WebApp and Agent must use new API contract:
  - POST /api/status: StatusPostDto
  - GET /api/status?tenantId=...: StatusListDto
- API configuration must specify Table Storage endpoint (Azure or Azurite)

## Local Development (Azurite)
- Start Azurite Table Storage:
  ```cmd
  docker run -p 10002:10002 mcr.microsoft.com/azure-storage/azurite azurite-table --tableHost 0.0.0.0
  ```
- Set `Storage:ConnectionString` in `appsettings.Development.json`:
  ```json
  "Storage": {
    "ConnectionString": "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFe...==;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;"
  }
  ```

## Troubleshooting
- If Table Storage errors occur, check endpoint/connection string in appsettings.json
- For local dev, ensure Azurite is running and port 10002 is open
- Monitor API logs for Table transaction counts and latency

## Rollout Guidance
- Validate in dev/staging with Azurite before production cutover
- Monitor dashboard read latency and Table transaction counts
- Optional: implement StatusHistory table in phase 2

## Operational Notes
- All device status logic is now routed through Azure Table Storage with partitioned queries for multi-tenant isolation
- Table transaction logging is enabled for observability and cost tracking
