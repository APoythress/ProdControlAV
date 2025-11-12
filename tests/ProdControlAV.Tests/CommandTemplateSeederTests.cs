using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProdControlAV.API.Data;
using ProdControlAV.Core.Models;
using Xunit;

namespace ProdControlAV.Tests;

public class CommandTemplateSeederTests
{
    private AppDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var tenantProvider = new TestTenantProvider(Guid.NewGuid());
        return new AppDbContext(options, tenantProvider);
    }

    [Fact]
    public void SeedCommandTemplates_CreatesTemplates()
    {
        // Arrange
        using var context = CreateInMemoryContext();

        // Act
        CommandTemplateSeeder.SeedCommandTemplates(context);

        // Assert
        var templates = context.CommandTemplates.ToList();
        Assert.NotEmpty(templates);
        Assert.All(templates, t => Assert.Equal("HyperDeck", t.DeviceType));
    }

    [Fact]
    public void SeedCommandTemplates_IsIdempotent()
    {
        // Arrange
        using var context = CreateInMemoryContext();

        // Act
        CommandTemplateSeeder.SeedCommandTemplates(context);
        var firstCount = context.CommandTemplates.Count();
        
        CommandTemplateSeeder.SeedCommandTemplates(context);
        var secondCount = context.CommandTemplates.Count();

        // Assert
        Assert.Equal(firstCount, secondCount);
    }

    [Fact]
    public void SeedCommandTemplates_CreatesMultipleCategories()
    {
        // Arrange
        using var context = CreateInMemoryContext();

        // Act
        CommandTemplateSeeder.SeedCommandTemplates(context);

        // Assert
        var categories = context.CommandTemplates
            .Select(t => t.Category)
            .Distinct()
            .ToList();
        
        Assert.Contains("Transport Control", categories);
        Assert.Contains("Status & Info", categories);
        Assert.Contains("Configuration", categories);
        Assert.Contains("Clip Management", categories);
    }

    [Fact]
    public void SeedCommandTemplates_AllTemplatesAreActive()
    {
        // Arrange
        using var context = CreateInMemoryContext();

        // Act
        CommandTemplateSeeder.SeedCommandTemplates(context);

        // Assert
        var templates = context.CommandTemplates.ToList();
        Assert.All(templates, t => Assert.True(t.IsActive));
    }

    [Fact]
    public void SeedCommandTemplates_AllTemplatesHaveValidData()
    {
        // Arrange
        using var context = CreateInMemoryContext();

        // Act
        CommandTemplateSeeder.SeedCommandTemplates(context);

        // Assert
        var templates = context.CommandTemplates.ToList();
        Assert.All(templates, t =>
        {
            Assert.NotEqual(Guid.Empty, t.Id);
            Assert.NotEmpty(t.Category);
            Assert.NotEmpty(t.Name);
            Assert.NotEmpty(t.Description);
            Assert.NotEmpty(t.HttpMethod);
            Assert.NotEmpty(t.Endpoint);
            Assert.NotEmpty(t.DeviceType);
            Assert.True(t.DisplayOrder > 0);
        });
    }

    [Fact]
    public void GetHyperDeckCommandTemplates_ReturnsExpectedCategories()
    {
        // Act
        var templates = CommandTemplateSeeder.GetHyperDeckCommandTemplates();

        // Assert
        var groupedByCategory = templates.GroupBy(t => t.Category).ToList();
        Assert.Contains(groupedByCategory, g => g.Key == "Transport Control");
        Assert.Contains(groupedByCategory, g => g.Key == "Status & Info");
        Assert.Contains(groupedByCategory, g => g.Key == "Configuration");
        Assert.Contains(groupedByCategory, g => g.Key == "Clip Management");
    }

    [Fact]
    public void GetHyperDeckCommandTemplates_TransportControlHasExpectedCommands()
    {
        // Act
        var templates = CommandTemplateSeeder.GetHyperDeckCommandTemplates();

        // Assert
        var transportCommands = templates
            .Where(t => t.Category == "Transport Control")
            .Select(t => t.Name)
            .ToList();

        Assert.Contains("Play", transportCommands);
        Assert.Contains("Stop", transportCommands);
        Assert.Contains("Record", transportCommands);
        Assert.Contains("Next Clip", transportCommands);
        Assert.Contains("Previous Clip", transportCommands);
    }

    private class TestTenantProvider : ITenantProvider
    {
        public Guid TenantId { get; }
        
        public TestTenantProvider(Guid tenantId)
        {
            TenantId = tenantId;
        }
    }
}
