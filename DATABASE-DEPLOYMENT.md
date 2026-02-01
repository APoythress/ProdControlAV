# Database Schema Deployment Guide

This document explains database schema deployment for ProdControlAV.

## 📋 Overview

Database migrations are **automatically applied** when the application starts. When the API container is deployed to Azure Container Apps, it will automatically detect and apply any pending database migrations before serving requests.

### How It Works

1. The application checks for pending migrations on startup
2. If migrations are pending, they are applied automatically
3. If migration fails, the application will not start (fail-fast behavior)
4. Once migrations succeed, the application continues normal startup

This ensures that the database schema is always up-to-date with the deployed code, eliminating the need for manual database updates after deployments.

### Multi-Instance Deployments

Entity Framework Core's `Database.Migrate()` uses SQL Server's built-in locking mechanisms via the `__EFMigrationsHistory` table to prevent race conditions when multiple container instances start simultaneously. Only one instance will apply the migration; others will wait and verify the migration is complete.

### Startup Time Considerations

For large or complex migrations:
- Migrations run synchronously during container startup
- Azure Container Apps startup probes may need adjustment for long-running migrations
- Consider scaling to 1 instance temporarily for major schema changes
- For very large migrations (>2 minutes), apply manually during a maintenance window using the optional SQL scripts

## 🛠️ Manual Migration Scripts (Optional)

While migrations are applied automatically at startup, you may still want to generate SQL scripts for the following scenarios:

- **Pre-deployment review**: Review SQL changes before deploying
- **Complex migrations**: Apply migrations manually during scheduled maintenance windows
- **Backup purposes**: Keep SQL scripts as documentation

### Prerequisites

- .NET 8 SDK installed
- Access to the ProdControlAV source code
- Entity Framework Core tools (will be installed automatically if missing)

### Using the Script Generator

We provide scripts to generate SQL migration scripts from your Entity Framework migrations:

#### On Linux/macOS:
```bash
./scripts/generate-db-scripts.sh
```

#### On Windows:
```batch
scripts\generate-db-scripts.bat
```

### What the Script Does

1. **Installs EF Core tools** (if not already installed)
2. **Creates a temporary configuration** with a dummy connection string
3. **Generates SQL migration scripts** from your Entity Framework migrations
4. **Outputs scripts** to the `database-scripts/` directory
5. **Provides usage instructions**

### Generated Files

- `database-scripts/01-complete-schema.sql` - Complete database schema (for new databases)
- Additional incremental scripts (if you have multiple migrations)

## 📦 Deployment Process

### Automatic Migration (Default)

The application automatically applies pending migrations on startup:

1. **Code changes** are pushed to `main`
2. **GitHub Actions** builds and deploys the Docker container
3. **Container starts** and automatically detects pending migrations
4. **Migrations are applied** to the database
5. **Application starts** serving requests with the updated schema

No manual intervention is required for standard deployments.

### Manual Migration (Optional)

For complex migrations or scheduled maintenance windows, you can disable automatic migrations and apply them manually:

1. **Generate the scripts**:
   ```bash
   ./scripts/generate-db-scripts.sh
   ```

2. **Backup your production database** (CRITICAL!)

3. **Test the scripts** on a staging/development database first

4. **Apply the scripts** in the correct order to your production database

5. **Deploy your application** using the existing CI/CD pipeline (migrations will be skipped if already applied)

## 🔐 Security Considerations

### Connection Strings
- **Never commit** production connection strings to source control
- Use **Azure Key Vault** or similar secret management for production
- Store connection strings as **environment variables** or **configuration secrets**

### Database Access
- Use **dedicated SQL users** with minimal required permissions
- Enable **SQL Server authentication** and **encrypted connections**
- Regularly **rotate database passwords**

## 🎯 CI/CD Integration

### How Migrations Are Applied

Database migrations are applied automatically when the container starts:

1. **Code changes** are pushed to `main`
2. **GitHub Actions** builds and deploys the Docker container
3. **Container starts** and executes `Program.cs` startup code
4. **Migrations are detected** using Entity Framework Core's `Database.Migrate()` method
5. **Pending migrations are applied** to the database
6. **Application starts** serving requests

If migration fails, the container will not start and will log the error for investigation.

## 📝 Best Practices

### Before Deployment
- [ ] Review Entity Framework migrations in code
- [ ] Test migrations on staging environment first
- [ ] Verify application compatibility with schema changes
- [ ] For complex migrations, review estimated execution time
- [ ] Consider scaling to 1 instance temporarily for major schema changes

### During Deployment
- [ ] Monitor container startup logs for migration progress
- [ ] Watch for any errors during migration execution
- [ ] Verify container health after startup
- [ ] If deployment has multiple instances, expect only one to apply migrations

### For Large Migrations (>2 minutes)
- [ ] Consider applying migrations manually during a maintenance window
- [ ] Use the optional SQL script generation for pre-deployment review
- [ ] Temporarily scale to 1 instance to avoid multiple containers waiting
- [ ] Increase Azure Container Apps startup probe timeout if needed

### After Deployment
- [ ] Verify all features work correctly
- [ ] Monitor application logs for any database-related errors
- [ ] Test critical functionality that uses updated schema

## 🐛 Troubleshooting

### Migration Failures

**Problem**: Container fails to start with migration errors
```
Solution:
1. Check Azure Container App logs for specific migration error
2. Verify connection string is correctly configured in secrets
3. Ensure database server is accessible from Container App
4. Check if database user has sufficient permissions (dbowner or equivalent)
```

**Problem**: "The connection string 'DefaultConnection' is missing"
```
Solution:
1. Verify the secret 'db-connstr' is set in Container App
2. Ensure environment variable ConnectionStrings__DefaultConnection references the secret
3. Check the deployment workflow set-secret and set-env-vars steps
```

**Problem**: Migration times out during startup
```
Solution:
1. Increase container startup timeout in Azure Container App settings
2. For large migrations, consider applying manually first using SQL scripts
3. Check database server resources and connection limits
```

### Script Generation Issues (for manual migrations)

**Problem**: "dotnet-ef not found"
```bash
# Solution: Install EF tools manually
dotnet tool install --global dotnet-ef
```

## 📞 Support

If you encounter issues:

1. **Check Azure Container App logs** for migration errors during startup
2. **Review Entity Framework migrations** in the `Migrations/` folder
3. **Verify database connectivity** and permissions
4. **Test migrations locally** against a test database before deploying

## 🔄 Example Deployment Workflow

### Automatic Migration (Standard)

```bash
# 1. Develop and test your changes locally
git checkout -b feature/my-database-change

# 2. Add Entity Framework migration
cd src/ProdControlAV.API
dotnet ef migrations add MyMigrationName

# 3. Test locally with automatic migration
dotnet run  # Migrations apply automatically on startup

# 4. Commit and push changes
git add .
git commit -m "Add database migration for feature X"
git push origin feature/my-database-change

# 5. Create PR and merge to main
# GitHub Actions will automatically:
# - Build the Docker container
# - Deploy to Azure Container Apps
# - Container applies migrations on startup
```

### Manual Migration (Complex Changes)

For complex migrations that need review or scheduled maintenance:

```bash
# 1. Generate SQL scripts for review
./scripts/generate-db-scripts.sh

# 2. Review the generated scripts
cat database-scripts/01-complete-schema.sql

# 3. Test on staging environment
# Deploy to staging first and monitor migration logs

# 4. Once verified, deploy to production
# The automatic migration will apply changes on container startup
```

---

With automatic migrations on container startup, database schema changes are deployed seamlessly alongside code changes, eliminating manual intervention and reducing the risk of schema mismatches between code and database.