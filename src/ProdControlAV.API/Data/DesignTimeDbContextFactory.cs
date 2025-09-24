using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Design-time factory for EF migrations and tooling support.
/// This allows EF tools to create the DbContext without running the full application.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Build configuration from standard ASP.NET Core sources
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddJsonFile("appsettings.Production.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        
        // Determine provider and connection string
        ConfigureDatabase(optionsBuilder, configuration, args);

        // Create a design-time tenant provider 
        var designTimeTenantProvider = new DesignTimeTenantProvider();
        
        return new AppDbContext(optionsBuilder.Options, designTimeTenantProvider);
    }

    private static void ConfigureDatabase(DbContextOptionsBuilder<AppDbContext> optionsBuilder, IConfiguration configuration, string[] args)
    {
        // Check environment variables first (for CI/CD scenarios)
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                "SQL Server connection string must be provided via ConnectionStrings:DefaultConnection environment variable or configuration. " +
                "SQLite is no longer supported.");
        }

        // Verify it's a SQL Server connection string
        if (!IsAzureSqlConnectionString(connectionString) && !IsSqlServerConnectionString(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string must be for SQL Server or Azure SQL Database. SQLite is no longer supported.");
        }

        optionsBuilder.UseSqlServer(connectionString);
    }

    private static bool IsAzureSqlConnectionString(string connectionString)
    {
        return connectionString.Contains("database.windows.net", StringComparison.OrdinalIgnoreCase) ||
               connectionString.Contains("Server=tcp:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSqlServerConnectionString(string connectionString)
    {
        return connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase) ||
               connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase) && 
               !connectionString.EndsWith(".db", StringComparison.OrdinalIgnoreCase) &&
               !connectionString.Contains("./", StringComparison.OrdinalIgnoreCase) &&
               !connectionString.Contains("/", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Design-time implementation of ITenantProvider for EF migrations.
/// Uses a default tenant ID since migrations apply to all tenants.
/// </summary>
public class DesignTimeTenantProvider : ITenantProvider
{
    public Guid TenantId => Guid.Empty; // Use empty GUID for design-time migrations
}