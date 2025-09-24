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
            .AddJsonFile("appsettings.ScriptGeneration.json", optional: true)
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
        
        // Require connection string for SQL Server
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("DefaultConnection connection string is required for SQL Server database.");
        }

        // Always use SQL Server
        optionsBuilder.UseSqlServer(connectionString);
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