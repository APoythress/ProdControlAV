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
        string? connectionString = null;
        string? databaseProvider = null;
        
        // Check environment variables first (for CI/CD scenarios)
        var envConnectionString = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrEmpty(envConnectionString))
        {
            connectionString = envConnectionString;
            // Auto-detect provider based on connection string
            if (IsAzureSqlConnectionString(connectionString) || IsSqlServerConnectionString(connectionString))
            {
                databaseProvider = "SqlServer";
            }
        }
        
        // Fallback to configuration file
        if (string.IsNullOrEmpty(connectionString))
        {
            var dbSection = configuration.GetSection("Database");
            databaseProvider = dbSection["Provider"] ?? "Sqlite";

            if (databaseProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                connectionString = dbSection["SqlServer:ConnectionString"]
                                 ?? dbSection.GetSection("SqlServer")["ConnectionString"];
            }
            else
            {
                // Handle SQLite configuration
                var configured = dbSection["Sqlite:ConnectionString"] 
                               ?? dbSection.GetSection("Sqlite")["ConnectionString"];
                
                if (string.IsNullOrWhiteSpace(configured))
                {
                    // Default SQLite path for design time
                    var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "data", "prodcontrol.db");
                    Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
                    connectionString = $"Data Source={dbPath}";
                }
                else if (configured.StartsWith("Data Source=./", StringComparison.OrdinalIgnoreCase))
                {
                    var relative = configured.Substring("Data Source=".Length).Trim();
                    var dbPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), relative));
                    Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
                    connectionString = $"Data Source={dbPath}";
                }
                else
                {
                    connectionString = configured;
                }
            }
        }

        // Absolute fallback for design time
        if (string.IsNullOrEmpty(connectionString))
        {
            var fallbackDbPath = Path.Combine(Directory.GetCurrentDirectory(), "data", "prodcontrol.db");
            Directory.CreateDirectory(Path.GetDirectoryName(fallbackDbPath)!);
            connectionString = $"Data Source={fallbackDbPath}";
            databaseProvider = "Sqlite";
        }

        // Configure the appropriate provider
        if (databaseProvider == "SqlServer" || IsAzureSqlConnectionString(connectionString) || IsSqlServerConnectionString(connectionString))
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