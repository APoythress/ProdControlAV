// C#
using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ProdControlAV.API.Data
{
    /// <summary>
    /// Design-time tenant provider that returns Guid.Empty for EF Core tooling scenarios
    /// </summary>
    internal class DesignTimeTenantProvider : ITenantProvider
    {
        public Guid TenantId => Guid.Empty;
    }

    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
            var basePath = Directory.GetCurrentDirectory();

            var configuration = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{env}.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            // Resolve connection string from args, config, or env
            var connectionString = ResolveConnectionString(args, configuration);

            // Mask preview for logging (avoid printing passwords)
            var preview = connectionString == null ? "<null>" :
                (connectionString.Length > 64 ? connectionString.Substring(0, 64) + "..." : connectionString);

            Console.WriteLine($"DesignTimeDbContextFactory: env='{env}', basePath='{basePath}', DefaultConnection length={(connectionString?.Length ?? 0)}, preview='{preview}'");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Connection string 'DefaultConnection' not found or empty. " +
                    "Ensure `appsettings.json` (or env) contains ConnectionStrings:DefaultConnection, " +
                    "or pass --connection \"<conn>\" to dotnet ef, or set environment variable ConnectionStrings__DefaultConnection.");
            }

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            try
            {
                optionsBuilder.UseSqlServer(connectionString);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException($"Invalid connection string format. Preview: '{preview}'. SqlClient error: {ex.Message}", ex);
            }

            var designTimeTenantProvider = new DesignTimeTenantProvider();
            return new AppDbContext(optionsBuilder.Options, designTimeTenantProvider);
        }

        private static string ResolveConnectionString(string[] args, IConfiguration configuration)
        {
            if (args != null)
            {
                foreach (var a in args)
                {
                    if (a.StartsWith("--connection=", StringComparison.OrdinalIgnoreCase))
                    {
                        return a.Substring("--connection=".Length).Trim('"');
                    }
                }
            }

            // Standard config key
            var cs = configuration.GetConnectionString("DefaultConnection");
            if (!string.IsNullOrWhiteSpace(cs)) return cs;

            // Environment variable fallback (Docker / CI friendly)
            cs = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
            if (!string.IsNullOrWhiteSpace(cs)) return cs;

            cs = Environment.GetEnvironmentVariable("DEFAULT_CONNECTION");
            return cs;
        }
    }
}
