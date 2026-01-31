# Docker & Local Development Quick Reference

## 🐳 Docker Commands

### First Time Setup
```bash
# Copy environment template
cp .env.example .env

# Edit with your Azure credentials
nano .env  # or code .env
```

### Running the Application
```bash
# Build and start (foreground)
docker-compose up --build

# Build and start (background)
docker-compose up -d --build

# Start existing containers
docker-compose up

# Stop containers
docker-compose down

# Stop and remove volumes
docker-compose down -v
```

### Viewing Logs
```bash
# All services
docker-compose logs -f

# Specific service
docker-compose logs -f prodcontrolav-api

# Last 100 lines
docker-compose logs --tail=100
```

### Rebuilding After Code Changes
```bash
# Rebuild and restart
docker-compose up --build

# Force rebuild (clean slate)
docker-compose build --no-cache
docker-compose up
```

### Troubleshooting
```bash
# Check running containers
docker-compose ps

# Check service health
docker-compose exec prodcontrolav-api curl http://localhost:8080/api/health

# Shell into container
docker-compose exec prodcontrolav-api /bin/bash

# View container resource usage
docker stats
```

## 💻 IDE Development (Rider/Visual Studio)

### JetBrains Rider
```bash
# Run
Shift+F10 (Windows/Linux) or Control+R (macOS)

# Debug
Shift+F9 (Windows/Linux) or Control+D (macOS)

# Stop
Ctrl+F2

# Build
Ctrl+Shift+B
```

### Visual Studio
```bash
# Run
F5 (debug) or Ctrl+F5 (no debug)

# Stop
Shift+F5

# Build
Ctrl+Shift+B

# Rebuild
Ctrl+Alt+F7
```

### Common Tasks
```bash
# Restore packages
dotnet restore

# Build solution
dotnet build

# Run tests
dotnet test

# Run application
cd src/ProdControlAV.API
dotnet run

# Trust HTTPS certificate
dotnet dev-certs https --trust

# Clear HTTPS certificate
dotnet dev-certs https --clean
```

## 🔐 Environment Variables

### Required Variables
```bash
# Database
ConnectionStrings__DefaultConnection="Server=tcp:..."

# JWT
Jwt__Issuer="https://localhost:5001"
Jwt__Audience="https://localhost:5001"
Jwt__Key="your-32-character-key-here"

# Azure Storage
Storage__TablesEndpoint="https://yourstore.table.core.windows.net/"
Storage__QueueConnectionString="DefaultEndpointsProtocol=..."

# Agent API Key
Api__AgentApiKey="your-32-character-key-here"
```

### Generate Secure Keys
```bash
# Generate JWT key
openssl rand -base64 32

# Or using PowerShell
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Minimum 0 -Maximum 255 }))
```

## 🧪 Testing

### Unit Tests
```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test tests/ProdControlAV.Tests/

# Run with verbose output
dotnet test --verbosity detailed

# Run specific test
dotnet test --filter "FullyQualifiedName~TestName"
```

### Manual Testing
```bash
# Health check
curl http://localhost:5000/api/health
curl https://localhost:5001/api/health

# Swagger UI
https://localhost:5001/swagger

# Database check (requires auth)
curl https://localhost:5001/api/health/database
```

## 🗄️ Database Migrations

### Create Migration
```bash
cd src/ProdControlAV.API
dotnet ef migrations add MigrationName
```

### Apply Migration
```bash
# Update database
dotnet ef database update

# Update to specific migration
dotnet ef database update MigrationName

# Rollback to previous migration
dotnet ef database update PreviousMigrationName
```

### View Migrations
```bash
# List all migrations
dotnet ef migrations list

# Generate SQL script
dotnet ef migrations script
```

## 🔧 User Secrets (IDE Development)

### Setup User Secrets
```bash
cd src/ProdControlAV.API
dotnet user-secrets init
```

### Set Secrets
```bash
# Database
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=tcp:..."

# JWT
dotnet user-secrets set "Jwt:Key" "your-key-here"

# Storage
dotnet user-secrets set "Storage:TablesEndpoint" "https://..."
dotnet user-secrets set "Storage:QueueConnectionString" "DefaultEndpointsProtocol=..."

# Agent API Key
dotnet user-secrets set "Api:AgentApiKey" "your-key-here"
```

### List Secrets
```bash
dotnet user-secrets list
```

### Remove Secrets
```bash
# Remove specific secret
dotnet user-secrets remove "SecretKey"

# Clear all secrets
dotnet user-secrets clear
```

## 📁 Project Structure

```
ProdControlAV/
├── src/
│   ├── ProdControlAV.API/        # Web API + Blazor host
│   ├── ProdControlAV.WebApp/     # Blazor WebAssembly frontend
│   ├── ProdControlAV.Core/       # Domain models & interfaces
│   ├── ProdControlAV.Infrastructure/  # Service implementations
│   └── ProdControlAV.Agent/      # Raspberry Pi monitoring agent
├── tests/
│   └── ProdControlAV.Tests/      # Unit tests
├── .env.example                   # Environment template
├── docker-compose.yml             # Docker Compose config
├── Dockerfile                     # Docker build config
└── LOCAL-DEVELOPMENT.md           # Full setup guide
```

## 🌐 Default URLs

| Environment | URL | Description |
|-------------|-----|-------------|
| Docker | http://localhost:5000 | API + WebApp |
| IDE (HTTP) | http://localhost:5000 | API + WebApp |
| IDE (HTTPS) | https://localhost:5001 | API + WebApp |
| Swagger | https://localhost:5001/swagger | API Documentation |
| Health | http://localhost:5000/api/health | Basic health check |
| Health (Detailed) | http://localhost:5000/api/health/storage | Storage health |

## ⚠️ Common Issues

### "Connection string is null"
```bash
# Check .env file (Docker)
cat .env | grep DB_CONNECTION_STRING

# Check environment variables (IDE)
# Edit launchSettings.json or use User Secrets
```

### "Port already in use"
```bash
# Find process using port 5000
lsof -i :5000  # macOS/Linux
netstat -ano | findstr :5000  # Windows

# Kill process
kill -9 <PID>  # macOS/Linux
taskkill /F /PID <PID>  # Windows
```

### "HTTPS certificate not trusted"
```bash
# Clean and reinstall certificate
dotnet dev-certs https --clean
dotnet dev-certs https --trust
```

### "Cannot connect to Azure"
```bash
# Test connectivity
curl https://your-account.table.core.windows.net/

# Check firewall rules in Azure Portal
# Verify connection strings are correct
```

### Docker build fails
```bash
# Clean Docker cache
docker system prune -a

# Rebuild without cache
docker-compose build --no-cache
```

## 📚 More Information

- **Full Setup Guide**: [LOCAL-DEVELOPMENT.md](LOCAL-DEVELOPMENT.md)
- **API Documentation**: [src/ProdControlAV.API/README.md](src/ProdControlAV.API/README.md)
- **Agent Deployment**: [src/ProdControlAV.Agent/README.md](src/ProdControlAV.Agent/README.md)
- **General Deployment**: [DEPLOYMENT.md](DEPLOYMENT.md)

## 💡 Tips

1. **Use User Secrets** for IDE development to keep credentials out of source control
2. **Use .env file** for Docker development (already in .gitignore)
3. **Different credentials** for each environment (dev, staging, prod)
4. **Generate strong keys** using `openssl rand -base64 32`
5. **Trust dev certificates** with `dotnet dev-certs https --trust`
6. **Check logs** regularly during development
7. **Use health endpoints** to verify connectivity to Azure services
