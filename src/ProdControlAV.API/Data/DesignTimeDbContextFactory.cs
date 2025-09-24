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
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        
        // Default to SQLite if no connection string is provided
        if (string.IsNullOrEmpty(connectionString))
        {
            // Ensure the data directory exists
            var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "data");
            Directory.CreateDirectory(dataDir);
            connectionString = $"Data Source={Path.Combine(dataDir, "prodcontrol.db")}";
        }

        // Check if this is a SQL Server connection string (for production scenarios)
        var useSqlServer = !string.IsNullOrEmpty(connectionString) && 
                           (IsAzureSqlConnectionString(connectionString) || IsSqlServerConnectionString(connectionString));

        if (useSqlServer)
        {
            optionsBuilder.UseSqlServer(connectionString);
        }
        else
        {
            optionsBuilder.UseSqlite(connectionString);
        }
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