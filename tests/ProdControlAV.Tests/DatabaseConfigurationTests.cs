using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ProdControlAV.Core.Interfaces;

namespace ProdControlAV.Tests;

public class DatabaseConfigurationTests
{
    [Fact]
    public void DesignTimeDbContextFactory_WithValidSqlServerConnectionString_ShouldCreateContext()
    {
        // Arrange
        var factory = new DesignTimeDbContextFactory();
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", 
            "Server=localhost;Database=TestDB;Trusted_Connection=true;");

        try
        {
            // Act
            var context = factory.CreateDbContext(Array.Empty<string>());

            // Assert
            Assert.NotNull(context);
            Assert.IsType<AppDbContext>(context);
            
            // Verify it's using SQL Server
            var options = context.Database.GetDbConnection();
            Assert.Contains("Microsoft.Data.SqlClient", options.GetType().FullName ?? "");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
        }
    }

    [Fact]
    public void DesignTimeDbContextFactory_WithAzureSqlConnectionString_ShouldCreateContext()
    {
        // Arrange
        var factory = new DesignTimeDbContextFactory();
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", 
            "Server=tcp:myserver.database.windows.net,1433;Database=TestDB;User ID=user;Password=pass;");

        try
        {
            // Act
            var context = factory.CreateDbContext(Array.Empty<string>());

            // Assert
            Assert.NotNull(context);
            Assert.IsType<AppDbContext>(context);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__Default", null);
        }
    }

    [Fact]
    public void DesignTimeDbContextFactory_WithNoConnectionString_ShouldThrowException()
    {
        // Arrange
        var factory = new DesignTimeDbContextFactory();
        
        // Clear all possible environment variables and config sources
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", null);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        
        // Create a temporary directory with empty appsettings files (no connection string)
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var originalDir = Directory.GetCurrentDirectory();
        
        try
        {
            Directory.SetCurrentDirectory(tempDir);
            
            // Create empty appsettings files
            File.WriteAllText("appsettings.json", "{}");
            File.WriteAllText("appsettings.Development.json", "{}");
            File.WriteAllText("appsettings.Production.json", "{}");
            
            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateDbContext(Array.Empty<string>()));
            Assert.Contains("Default connection string is required", exception.Message);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
            Directory.Delete(tempDir, true);
        }
    }

    [Theory]
    [InlineData("Server=localhost;Database=TestDB;Trusted_Connection=true;")]
    [InlineData("Server=tcp:myserver.database.windows.net,1433;Database=TestDB;User ID=user;Password=pass;")]
    [InlineData("Data Source=localhost;Initial Catalog=TestDB;Integrated Security=true;")]
    public void DatabaseConfiguration_WithValidSqlServerConnectionStrings_ShouldWork(string connectionString)
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>("ConnectionStrings:Default", connectionString)
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        
        // Add mock tenant provider
        services.AddScoped<ITenantProvider>(_ => new TestTenantProvider());
        
        // Act & Assert - Should not throw
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));
        var serviceProvider = services.BuildServiceProvider();
        var context = serviceProvider.GetRequiredService<AppDbContext>();
        
        Assert.NotNull(context);
    }

    // Test implementation of ITenantProvider
    private class TestTenantProvider : ITenantProvider
    {
        public Guid TenantId => Guid.Empty;
    }
}