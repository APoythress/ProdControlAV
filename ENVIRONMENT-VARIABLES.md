# Environment Configuration Guide

## How Environment Variables Work in ProdControlAV

This guide explains how ProdControlAV uses environment variables to manage configuration across different environments (local development, Docker, Azure).

## Configuration System Overview

ProdControlAV uses the standard ASP.NET Core configuration system with the following hierarchy (later sources override earlier ones):

1. **appsettings.json** - Base configuration with placeholders
2. **appsettings.{Environment}.json** - Environment-specific overrides
3. **Environment Variables** - Runtime configuration (highest priority)
4. **User Secrets** - Development-only secrets (for IDE use)

### Why Placeholders in appsettings.json?

You'll notice that `appsettings.json` contains placeholders like `${DB_CONNECTION_STRING}`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "${DB_CONNECTION_STRING}"
  }
}
```

**These are NOT automatically expanded by .NET!** They serve as documentation to show what environment variables need to be set. The actual values come from environment variables.

## Setting Environment Variables by Scenario

### 1. Docker Development (docker-compose)

When using Docker Compose, environment variables are set in two ways:

**A. Via .env file** (Recommended)
```bash
# Create .env from template
cp .env.example .env

# Edit .env with your values
nano .env
```

The `.env` file is automatically loaded by Docker Compose and used to populate environment variables.

**B. Via docker-compose.yml environment section**

Environment variables can also be set directly in `docker-compose.yml`:

```yaml
environment:
  - ConnectionStrings__DefaultConnection=Server=...
  - Jwt__Key=your-key
```

**Note**: Use `__` (double underscore) to represent nested configuration keys!

### 2. JetBrains Rider / Visual Studio

**Option A: launchSettings.json** (Good for quick setup)

Edit `src/ProdControlAV.API/Properties/launchSettings.json`:

```json
{
  "profiles": {
    "ProdControlAV.Server": {
      "environmentVariables": {
        "ConnectionStrings__DefaultConnection": "Server=tcp:...",
        "Jwt__Key": "your-key-here"
      }
    }
  }
}
```

**⚠️ Warning**: `launchSettings.json` is committed to source control. Use User Secrets for team environments.

**Option B: User Secrets** (Recommended for teams)

```bash
cd src/ProdControlAV.API
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=tcp:..."
dotnet user-secrets set "Jwt:Key" "your-key"
```

Then use the "ProdControlAV.Server (User Secrets)" profile in your IDE.

**Note**: User Secrets use `:` (colon) notation, not `__`!

### 3. Azure App Service / Production

Set environment variables in Azure Portal:

1. Go to your App Service
2. **Settings → Configuration → Application Settings**
3. Add new setting:
   - Name: `ConnectionStrings__DefaultConnection` (or use the Connection Strings section)
   - Value: Your Azure SQL connection string

**Note**: Azure uses `__` for nested keys in Application Settings, but Connection Strings have a dedicated section.

### 4. Command Line / Terminal

For testing or one-off runs:

**Linux/macOS:**
```bash
export ConnectionStrings__DefaultConnection="Server=tcp:..."
export Jwt__Key="your-key"
cd src/ProdControlAV.API
dotnet run
```

**Windows (PowerShell):**
```powershell
$env:ConnectionStrings__DefaultConnection="Server=tcp:..."
$env:Jwt__Key="your-key"
cd src/ProdControlAV.API
dotnet run
```

**Windows (CMD):**
```cmd
set ConnectionStrings__DefaultConnection=Server=tcp:...
set Jwt__Key=your-key
cd src/ProdControlAV.API
dotnet run
```

## Environment Variable Naming Conventions

### Nested Configuration

ASP.NET Core configuration uses hierarchical keys. The format differs by context:

| Context | Format | Example |
|---------|--------|---------|
| Environment Variables | `Parent__Child` | `ConnectionStrings__DefaultConnection` |
| User Secrets | `Parent:Child` | `ConnectionStrings:DefaultConnection` |
| JSON Files | `{ "Parent": { "Child": "value" } }` | Standard JSON structure |

### Why Double Underscore?

Environment variable names cannot contain colons (`:`) on some systems, so ASP.NET Core uses double underscore (`__`) as the separator when reading from environment variables.

**Example Mapping:**

```json
// appsettings.json
{
  "ConnectionStrings": {
    "DefaultConnection": "..."
  },
  "Jwt": {
    "Issuer": "...",
    "Key": "..."
  }
}
```

**Environment Variables:**
- `ConnectionStrings__DefaultConnection`
- `Jwt__Issuer`
- `Jwt__Key`

**User Secrets:**
- `ConnectionStrings:DefaultConnection`
- `Jwt:Issuer`
- `Jwt:Key`

## Required Environment Variables

### Core Configuration

| Variable | Description | Example |
|----------|-------------|---------|
| `ConnectionStrings__DefaultConnection` | Azure SQL connection string | `Server=tcp:your-server.database.windows.net,1433;...` |
| `Jwt__Issuer` | JWT token issuer | `https://your-domain.com` |
| `Jwt__Audience` | JWT token audience | `https://your-domain.com` |
| `Jwt__Key` | JWT signing key (32+ chars) | Generate: `openssl rand -base64 32` |
| `Storage__TablesEndpoint` | Azure Table Storage endpoint | `https://your-storage.table.core.windows.net/` |
| `Storage__QueueConnectionString` | Azure Queue Storage connection | `DefaultEndpointsProtocol=https;AccountName=...` |
| `Api__AgentApiKey` | Agent authentication key | Generate: `openssl rand -base64 32` |

### Optional Configuration

| Variable | Description | Default |
|----------|-------------|---------|
| `ASPNETCORE_ENVIRONMENT` | Environment name | `Production` |
| `ASPNETCORE_URLS` | Listen URLs | `http://+:80` |
| `Logging__LogLevel__Default` | Log level | `Information` |

## Verifying Configuration

### Check What Values Are Active

You can log configuration values at startup (for non-sensitive data):

```csharp
// In Program.cs or Startup.cs
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
Console.WriteLine($"JWT Issuer: {jwtIssuer}");
```

### Common Issues

**Issue**: Application fails with "Connection string is null"
- **Cause**: Environment variable not set or named incorrectly
- **Fix**: Double-check variable name uses `__` not `:` for environment variables

**Issue**: Configuration shows placeholder like `${DB_CONNECTION_STRING}`
- **Cause**: Environment variable not set; appsettings.json placeholder is being used
- **Fix**: Set the actual environment variable

**Issue**: User Secrets not working
- **Cause**: Using wrong launch profile or secrets not initialized
- **Fix**: Use the "User Secrets" profile, run `dotnet user-secrets init`

## Security Best Practices

### DO:
✅ Use `.env` files for Docker (already in `.gitignore`)
✅ Use User Secrets for IDE development
✅ Use Azure Key Vault references in production
✅ Rotate keys regularly
✅ Use different credentials per environment
✅ Generate strong keys: `openssl rand -base64 32`

### DON'T:
❌ Commit `.env` files to source control
❌ Commit `appsettings.json` with real credentials
❌ Share credentials in chat, email, or documentation
❌ Use production credentials in development
❌ Hardcode credentials in source code

## Examples

### Example .env File

```bash
# Database
DB_CONNECTION_STRING=Server=tcp:myserver.database.windows.net,1433;Initial Catalog=ProdControlAV;User ID=myuser;Password=MySecurePass123!;Encrypt=True;TrustServerCertificate=False;

# JWT
JWT_ISSUER=https://api.mycompany.com
JWT_AUDIENCE=https://api.mycompany.com
JWT_KEY=abcdef1234567890abcdef1234567890

# Azure Storage
TABLES_ENDPOINT=https://mystorageaccount.table.core.windows.net/
QUEUE_CONNECTION_STRING=DefaultEndpointsProtocol=https;AccountName=mystorageaccount;AccountKey=mykey;EndpointSuffix=core.windows.net

# Agent
AGENT_API_KEY=agent-key-1234567890abcdef1234567890
```

### Example User Secrets Setup

```bash
cd src/ProdControlAV.API

# Initialize (creates UserSecretsId in .csproj)
dotnet user-secrets init

# Set all required secrets
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=tcp:myserver.database.windows.net,1433;Initial Catalog=ProdControlAV;User ID=myuser;Password=MySecurePass123!;Encrypt=True;"

dotnet user-secrets set "Jwt:Issuer" "https://api.mycompany.com"
dotnet user-secrets set "Jwt:Audience" "https://api.mycompany.com"
dotnet user-secrets set "Jwt:Key" "abcdef1234567890abcdef1234567890"

dotnet user-secrets set "Storage:TablesEndpoint" "https://mystorageaccount.table.core.windows.net/"
dotnet user-secrets set "Storage:QueueConnectionString" "DefaultEndpointsProtocol=https;AccountName=mystorageaccount;AccountKey=mykey;EndpointSuffix=core.windows.net"

dotnet user-secrets set "Api:AgentApiKey" "agent-key-1234567890abcdef1234567890"

# List all secrets (verify)
dotnet user-secrets list

# Remove all secrets (if needed)
dotnet user-secrets clear
```

## Further Reading

- [ASP.NET Core Configuration Documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/)
- [User Secrets in Development](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets)
- [Azure App Service Configuration](https://learn.microsoft.com/en-us/azure/app-service/configure-common)
- [Docker Environment Variables](https://docs.docker.com/compose/environment-variables/)

## Quick Links

- **[LOCAL-DEVELOPMENT.md](LOCAL-DEVELOPMENT.md)** - Complete setup guide for Docker and IDE
- **[QUICK-REFERENCE.md](QUICK-REFERENCE.md)** - Command cheat sheet
- **[.env.example](.env.example)** - Template for environment variables
