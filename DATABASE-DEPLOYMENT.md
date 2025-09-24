# Database Schema Deployment Guide

This document explains how to deploy database schema changes for ProdControlAV after removing automatic migrations from the GitHub Actions CI/CD pipeline.

## 📋 Overview

Starting from this update, database migrations are **no longer applied automatically** during CI/CD deployments. Instead, you must manually apply schema changes to your database using the generated SQL scripts.

## 🛠️ Generating SQL Migration Scripts

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

### For New Databases (Fresh Installation)

1. **Generate the scripts**:
   ```bash
   ./scripts/generate-db-scripts.sh
   ```

2. **Configure your connection string** in `appsettings.Production.json`:
   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Server=your-server.database.windows.net,1433;Database=your-db;User Id=your-user;Password=your-password;Encrypt=True;"
     }
   }
   ```

3. **Run the complete schema script** against your SQL Server database:
   ```sql
   -- Execute: database-scripts/01-complete-schema.sql
   ```

### For Existing Databases (Updates)

1. **Generate new scripts** after merging changes to main
2. **Backup your production database** (CRITICAL!)
3. **Test the scripts** on a staging/development database first
4. **Apply the incremental scripts** in the correct order
5. **Deploy your application** using the existing CI/CD pipeline

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

### What Changed in GitHub Actions

The following steps were **removed** from the deployment workflow:
```yaml
# REMOVED: These steps no longer run automatically
- name: Install EF Core tools
  run: dotnet tool install --global dotnet-ef

- name: Apply database migrations
  run: |
    cd src/ProdControlAV.API
    dotnet ef database update --verbose
  env:
    ConnectionStrings__DefaultConnection: ${{ secrets.SQL_CONNECTION_STRING }}
```

### New Deployment Flow

1. **Code changes** are pushed to `main`
2. **GitHub Actions** builds and deploys the application (without migrations)
3. **Developer manually applies** database schema changes using generated scripts
4. **Application connects** to the updated database

## 📝 Best Practices

### Before Deployment
- [ ] Generate and review SQL scripts
- [ ] Backup production database
- [ ] Test scripts on staging environment
- [ ] Verify application compatibility

### During Deployment
- [ ] Apply scripts during maintenance window
- [ ] Monitor for errors during execution
- [ ] Verify data integrity after completion
- [ ] Test critical application functionality

### After Deployment
- [ ] Document what was deployed
- [ ] Monitor application logs
- [ ] Verify all features work correctly
- [ ] Keep backup until confident in stability

## 🐛 Troubleshooting

### Script Generation Issues

**Problem**: "dotnet-ef not found"
```bash
# Solution: Install EF tools manually
dotnet tool install --global dotnet-ef
```

**Problem**: "DefaultConnection connection string is required"
```bash
# Solution: The script should handle this automatically
# If it doesn't, check that DesignTimeDbContextFactory.cs includes:
# .AddJsonFile("appsettings.ScriptGeneration.json", optional: true)
```

### Database Deployment Issues

**Problem**: Script execution errors
- Check SQL Server version compatibility
- Verify connection string and permissions
- Review script for potential conflicts

**Problem**: Application won't start after migration
- Verify all migrations were applied successfully
- Check application configuration
- Review connection string settings

## 📞 Support

If you encounter issues:

1. **Check the generated SQL scripts** for obvious problems
2. **Test on a development database** first
3. **Review Entity Framework migrations** in the `Migrations/` folder
4. **Consult the application logs** for specific error messages

## 🔄 Example Workflow

Here's a complete example workflow for deploying schema changes:

```bash
# 1. After merging schema changes to main
git pull origin main

# 2. Generate SQL scripts
./scripts/generate-db-scripts.sh

# 3. Review the generated scripts
cat database-scripts/01-complete-schema.sql

# 4. Backup production database (using your preferred method)
# Example: Azure CLI
az sql db export --resource-group MyResourceGroup --server MyServer --name MyDatabase --admin-user MyUser --admin-password MyPassword --storage-key MyStorageKey --storage-key-type StorageAccessKey --storage-uri https://myaccount.blob.core.windows.net/mycontainer/backup.bacpac

# 5. Apply scripts to staging first
sqlcmd -S staging-server -d staging-db -i database-scripts/01-complete-schema.sql

# 6. Test staging environment
# ... run tests ...

# 7. Apply to production during maintenance window
sqlcmd -S production-server -d production-db -i database-scripts/01-complete-schema.sql

# 8. Deploy application (triggers automatically via GitHub Actions)
# Application deployment happens automatically when changes are pushed to main
```

---

This approach gives you full control over when and how database schema changes are applied, reducing the risk of deployment failures due to database issues.