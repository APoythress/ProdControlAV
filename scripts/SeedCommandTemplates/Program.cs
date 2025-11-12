using Microsoft.EntityFrameworkCore;
using ProdControlAV.API.Data;

namespace ProdControlAV.Scripts;

/// <summary>
/// Console application to seed HyperDeck command templates into the database
/// Usage: dotnet run --project scripts/SeedCommandTemplates.csproj -- "connection-string"
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: dotnet run -- \"<connection-string>\"");
            Console.WriteLine("Example: dotnet run -- \"Server=localhost;Database=ProdControlAV;Integrated Security=true;TrustServerCertificate=true\"");
            return;
        }

        var connectionString = args[0];
        
        Console.WriteLine("Seeding HyperDeck Command Templates...");
        Console.WriteLine($"Connection string: {MaskConnectionString(connectionString)}");
        Console.WriteLine();

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(connectionString)
                .Options;

            // Create a simple tenant provider for seeding (not tenant-specific)
            var tenantProvider = new SimpleTenantProvider(Guid.Empty);
            
            using var context = new AppDbContext(options, tenantProvider);
            
            // Ensure database is created
            Console.WriteLine("Ensuring database exists...");
            await context.Database.EnsureCreatedAsync();
            
            // Seed the templates
            Console.WriteLine("Seeding command templates...");
            CommandTemplateSeeder.SeedCommandTemplates(context);
            
            Console.WriteLine();
            Console.WriteLine("✓ Successfully seeded command templates!");
            Console.WriteLine($"Total templates: {context.CommandTemplates.Count()}");
            
            // Display summary
            var groupedTemplates = context.CommandTemplates
                .GroupBy(t => t.Category)
                .Select(g => new { Category = g.Key, Count = g.Count() })
                .OrderBy(g => g.Category);
            
            Console.WriteLine();
            Console.WriteLine("Templates by category:");
            foreach (var group in groupedTemplates)
            {
                Console.WriteLine($"  - {group.Category}: {group.Count} templates");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"✗ Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }
    }

    static string MaskConnectionString(string connStr)
    {
        // Mask sensitive information in connection string
        var parts = connStr.Split(';');
        var masked = new List<string>();
        
        foreach (var part in parts)
        {
            if (part.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
                part.Contains("Pwd", StringComparison.OrdinalIgnoreCase))
            {
                var key = part.Split('=')[0];
                masked.Add($"{key}=***");
            }
            else
            {
                masked.Add(part);
            }
        }
        
        return string.Join(";", masked);
    }
}

// Simple tenant provider for seeding operations
public class SimpleTenantProvider : ITenantProvider
{
    public Guid TenantId { get; }
    
    public SimpleTenantProvider(Guid tenantId)
    {
        TenantId = tenantId;
    }
}
