# Local Development Setup Guide

This guide explains how to set up your local development environment for ProdControlAV, including both Docker-based and IDE-based (JetBrains Rider / Visual Studio) development.

## Overview

ProdControlAV supports two local development approaches:

1. **Docker Development** - Run the complete API + WebApp in a Docker container that simulates production
2. **IDE Development** - Run directly from JetBrains Rider or Visual Studio on Windows/macOS/Linux

Both approaches connect to your existing Azure infrastructure (SQL Database, Table Storage, Queue Storage) to provide a realistic development environment while keeping all credentials secure via environment variables.

---

## Prerequisites

### Common Prerequisites
- **.NET 8 SDK** - [Download](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Git** - For cloning the repository
- **Azure Account** - Access to Azure SQL Database and Azure Storage accounts

### Docker Development Prerequisites
- **Docker Desktop** - [Download](https://www.docker.com/products/docker-desktop)
- **Docker Compose** - Included with Docker Desktop

### IDE Development Prerequisites
- **JetBrains Rider** OR **Visual Studio 2022** (17.8+)
- **Azure Storage Explorer** (optional) - For debugging storage issues

---

## Security Best Practices

⚠️ **IMPORTANT**: Never commit sensitive credentials to source control!

- Use `.env` files for Docker (already in `.gitignore`)
- Use `launchSettings.json` or User Secrets for IDE development
- Use different credentials for each environment
- Rotate keys regularly
- Use Azure Managed Identity in production instead of connection strings

---

## Option 1: Docker Development Setup

Docker development provides a production-like environment that runs in a container, perfect for testing deployment configurations.

### Step 1: Configure Environment Variables

1. **Copy the example environment file:**
   ```bash
   cp .env.example .env
   ```

2. **Edit `.env` and fill in your Azure credentials:**
   ```bash
   # Use your favorite text editor
   nano .env
   # OR
   code .env
   ```

3. **Required values to set:**
   - `DB_CONNECTION_STRING` - Your Azure SQL Database connection string
   - `JWT_ISSUER` - Your API domain (e.g., `https://localhost:5001` for local)
   - `JWT_AUDIENCE` - Same as issuer for local dev
   - `JWT_KEY` - Generate a secure key: `openssl rand -base64 32`
   - `TABLES_ENDPOINT` - Your Azure Table Storage endpoint
   - `QUEUE_CONNECTION_STRING` - Your Azure Queue Storage connection string
   - `AGENT_API_KEY` - Generate a secure key: `openssl rand -base64 32`

### Step 2: Build and Run with Docker Compose

```bash
# Build and start the application
docker-compose up --build

# OR run in detached mode (background)
docker-compose up -d --build
```

### Step 3: Access the Application

- **Web UI**: http://localhost:5000
- **API**: http://localhost:5000/api
- **Swagger Documentation**: http://localhost:5000/swagger

### Step 4: View Logs

```bash
# Follow logs in real-time
docker-compose logs -f

# View logs for specific service
docker-compose logs -f prodcontrolav-api
```

### Step 5: Stop the Application

```bash
# Stop and remove containers
docker-compose down

# Stop and remove containers + volumes
docker-compose down -v
```

### Docker Development Tips

- **Code Changes**: After modifying code, rebuild with `docker-compose up --build`
- **Database Migrations**: Run migrations from inside the container:
  ```bash
  docker-compose exec prodcontrolav-api dotnet ef database update
  ```
- **Debugging**: Add `- ASPNETCORE_ENVIRONMENT=Development` in `docker-compose.yml`
- **HTTPS Support**: For production-like HTTPS, mount certificates into the container

---

## Option 2: IDE Development Setup (Rider / Visual Studio)

IDE development provides the best debugging experience with full breakpoint support and hot reload.

### Option A: Using launchSettings.json (Direct Configuration)

1. **Edit `src/ProdControlAV.API/Properties/launchSettings.json`**

2. **Find the `ProdControlAV.Server` profile and fill in the environment variables:**
   ```json
   "environmentVariables": {
     "ASPNETCORE_ENVIRONMENT": "Development",
     "ConnectionStrings__DefaultConnection": "Server=tcp:your-server.database.windows.net,1433;...",
     "Jwt__Issuer": "https://localhost:5001",
     "Jwt__Audience": "https://localhost:5001",
     "Jwt__Key": "your-secure-jwt-key-here",
     "Storage__TablesEndpoint": "https://your-storage.table.core.windows.net/",
     "Storage__QueueConnectionString": "DefaultEndpointsProtocol=https;AccountName=...",
     "Api__AgentApiKey": "your-secure-agent-api-key-here"
   }
   ```

3. **⚠️ Warning**: `launchSettings.json` is committed to source control. For team environments, use User Secrets instead (see Option B).

### Option B: Using User Secrets (Recommended for Teams)

User Secrets keep credentials out of your codebase entirely.

1. **Initialize User Secrets:**
   ```bash
   cd src/ProdControlAV.API
   dotnet user-secrets init
   ```

2. **Set your secrets:**
   ```bash
   # Database
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=tcp:your-server.database.windows.net,1433;..."
   
   # JWT
   dotnet user-secrets set "Jwt:Issuer" "https://localhost:5001"
   dotnet user-secrets set "Jwt:Audience" "https://localhost:5001"
   dotnet user-secrets set "Jwt:Key" "your-secure-jwt-key-here"
   
   # Azure Storage
   dotnet user-secrets set "Storage:TablesEndpoint" "https://your-storage.table.core.windows.net/"
   dotnet user-secrets set "Storage:QueueConnectionString" "DefaultEndpointsProtocol=https;AccountName=..."
   
   # Agent API Key
   dotnet user-secrets set "Api:AgentApiKey" "your-secure-agent-api-key-here"
   ```

3. **Use the `ProdControlAV.Server (User Secrets)` launch profile in your IDE**

### Running from JetBrains Rider

1. **Open the solution:**
   - File → Open → Select `ProdControlAV.sln`

2. **Select launch configuration:**
   - Top toolbar → Select `ProdControlAV.Server` or `ProdControlAV.Server (User Secrets)`

3. **Run the application:**
   - Click the green play button (▶️)
   - Or press `Shift+F10` (Windows/Linux) / `Control+R` (macOS)

4. **Debug the application:**
   - Set breakpoints in your code
   - Click the debug button (🐞)
   - Or press `Shift+F9` (Windows/Linux) / `Control+D` (macOS)

5. **Access the application:**
   - Rider will open your browser automatically to https://localhost:5001
   - API: https://localhost:5001/api
   - Swagger: https://localhost:5001/swagger

### Running from Visual Studio

1. **Open the solution:**
   - File → Open → Project/Solution → Select `ProdControlAV.sln`

2. **Set startup project:**
   - Right-click `ProdControlAV.API` in Solution Explorer
   - Select "Set as Startup Project"

3. **Select launch profile:**
   - Debug toolbar → Select `ProdControlAV.Server` or `ProdControlAV.Server (User Secrets)`

4. **Run the application:**
   - Press F5 (with debugging) or Ctrl+F5 (without debugging)

5. **Access the application:**
   - Visual Studio will open your browser automatically
   - Access points same as Rider above

### IDE Development Tips

- **Hot Reload**: Both Rider and Visual Studio support hot reload for code changes
- **Database Migrations**: Run from Package Manager Console or Terminal:
  ```bash
  cd src/ProdControlAV.API
  dotnet ef migrations add YourMigrationName
  dotnet ef database update
  ```
- **Multiple Projects**: To run Agent and API simultaneously, set multiple startup projects
- **HTTPS Certificate**: Trust the development certificate:
  ```bash
  dotnet dev-certs https --trust
  ```

---

## Verifying Your Setup

After setup, verify everything works:

### 1. Check Health Endpoints

```bash
# API health check
curl http://localhost:5000/api/health

# Or with HTTPS
curl https://localhost:5001/api/health
```

### 2. Check Swagger UI

Navigate to: https://localhost:5001/swagger

You should see the API documentation.

### 3. Test Azure Connectivity

- **Database**: Try signing in (creates a database connection)
- **Table Storage**: Check that DeviceStatus table is created in your Azure Storage account
- **Queue Storage**: Send a command and verify it appears in the queue

### 4. Check Logs

Look for these messages in the console output:

```
info: TableSetup[0]
      Ensured Azure Table exists: DeviceStatus
info: TableSetup[0]
      Ensured Azure Table exists: CommandQueue
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:5001
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
```

---

## Troubleshooting

### "Connection string is null or empty"
- **Docker**: Check your `.env` file has the correct `DB_CONNECTION_STRING`
- **IDE**: Check your environment variables in `launchSettings.json` or User Secrets

### "JWT key is null or empty"
- Generate a secure key: `openssl rand -base64 32`
- Set it in your environment configuration

### "Cannot connect to Azure Storage"
- Verify your `TABLES_ENDPOINT` or `TABLES_CONNECTION_STRING`
- Check network connectivity to Azure
- Verify storage account allows access from your IP

### "Failed to ensure Azure Table exists"
- Check storage account credentials
- Verify you have permissions to create tables
- If using Managed Identity locally, authenticate with: `az login`

### Docker build fails
- Ensure you're in the repository root directory
- Check Docker daemon is running
- Try cleaning: `docker system prune -a`

### HTTPS certificate errors (IDE)
- Trust the development certificate:
  ```bash
  dotnet dev-certs https --clean
  dotnet dev-certs https --trust
  ```

### Port already in use
- Change ports in `launchSettings.json` or `docker-compose.yml`
- Or stop the process using the port:
  ```bash
  # Find process on port 5000
  lsof -i :5000
  # Kill it
  kill -9 <PID>
  ```

---

## Environment Variable Reference

### Database Configuration
| Variable | Description | Example |
|----------|-------------|---------|
| `ConnectionStrings__DefaultConnection` | Azure SQL connection string | `Server=tcp:...` |

### JWT Configuration
| Variable | Description | Example |
|----------|-------------|---------|
| `Jwt__Issuer` | Token issuer | `https://localhost:5001` |
| `Jwt__Audience` | Token audience | `https://localhost:5001` |
| `Jwt__Key` | Signing key (32+ chars) | `base64-encoded-key` |

### Azure Storage Configuration
| Variable | Description | Example |
|----------|-------------|---------|
| `Storage__TablesEndpoint` | Table Storage endpoint | `https://...table.core.windows.net/` |
| `Storage__QueueConnectionString` | Queue connection string | `DefaultEndpointsProtocol=https;...` |

### Agent Configuration
| Variable | Description | Example |
|----------|-------------|---------|
| `Api__AgentApiKey` | Agent authentication key | `base64-encoded-key` |

### Runtime Configuration
| Variable | Description | Example |
|----------|-------------|---------|
| `ASPNETCORE_ENVIRONMENT` | Environment name | `Development`, `Production` |
| `ASPNETCORE_URLS` | Listen URLs | `https://localhost:5001` |

---

## Next Steps

- **Run Tests**: `dotnet test`
- **Create a Migration**: `dotnet ef migrations add YourMigration`
- **Deploy Agent**: See [Agent Deployment Guide](src/ProdControlAV.Agent/README.md)
- **Configure Production**: See [Deployment Documentation](DEPLOYMENT.md)

---

## Getting Help

- **Documentation**: Check the `/docs` folder for detailed guides
- **Issues**: Submit issues on the GitHub repository
- **Logs**: Enable verbose logging by setting `Logging__LogLevel__Default=Debug`

---

**Security Reminder**: Always use environment variables or User Secrets for sensitive data. Never commit credentials to source control!
