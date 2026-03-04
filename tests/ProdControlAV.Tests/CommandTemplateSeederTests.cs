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
        Assert.Contains(templates, t => t.DeviceType == "HyperDeck");
        Assert.Contains(templates, t => t.DeviceType == "ATEM");
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

    // ── ATEM template tests ───────────────────────────────────────────────────

    [Fact]
    public void GetAtemCommandTemplates_ReturnsTemplates()
    {
        var templates = CommandTemplateSeeder.GetAtemCommandTemplates();
        Assert.NotEmpty(templates);
        Assert.All(templates, t => Assert.Equal("ATEM", t.DeviceType));
    }

    [Fact]
    public void GetAtemCommandTemplates_AllTemplatesHaveRequiredFields()
    {
        var templates = CommandTemplateSeeder.GetAtemCommandTemplates();
        Assert.All(templates, t =>
        {
            Assert.NotEqual(Guid.Empty, t.Id);
            Assert.NotEmpty(t.Category);
            Assert.NotEmpty(t.Name);
            Assert.NotEmpty(t.Description);
            Assert.Equal("ATEM", t.HttpMethod);
            Assert.NotEmpty(t.Endpoint);
            Assert.False(string.IsNullOrEmpty(t.AtemFunction));
            Assert.True(t.DisplayOrder > 0);
            Assert.True(t.IsActive);
        });
    }

    [Fact]
    public void GetAtemCommandTemplates_HasAllExpectedCategories()
    {
        var templates = CommandTemplateSeeder.GetAtemCommandTemplates();
        var categories = templates.Select(t => t.Category).Distinct().ToList();

        Assert.Contains("Program Switching", categories);
        Assert.Contains("Preview Routing",   categories);
        Assert.Contains("Aux Routing",       categories);
        Assert.Contains("Macros",            categories);
        Assert.Contains("Status",            categories);
    }

    [Fact]
    public void GetAtemCommandTemplates_CutToProgramCoversInputs1To8()
    {
        var templates = CommandTemplateSeeder.GetAtemCommandTemplates();
        var cutTemplates = templates
            .Where(t => t.AtemFunction == "CutToProgram")
            .ToList();

        Assert.Equal(8, cutTemplates.Count);
        for (int i = 1; i <= 8; i++)
            Assert.Contains(cutTemplates, t => t.AtemInputId == i);
    }

    [Fact]
    public void GetAtemCommandTemplates_FadeToProgramHasDefaultTransitionRate()
    {
        var templates = CommandTemplateSeeder.GetAtemCommandTemplates();
        var fadeTemplates = templates.Where(t => t.AtemFunction == "FadeToProgram").ToList();

        Assert.Equal(8, fadeTemplates.Count);
        Assert.All(fadeTemplates, t => Assert.Equal(30, t.AtemTransitionRate));
    }

    [Fact]
    public void GetAtemCommandTemplates_SetAuxTemplatesHaveChannelAndInput()
    {
        var templates = CommandTemplateSeeder.GetAtemCommandTemplates();
        var auxTemplates = templates.Where(t => t.AtemFunction == "SetAux").ToList();

        Assert.NotEmpty(auxTemplates);
        Assert.All(auxTemplates, t =>
        {
            Assert.NotNull(t.AtemChannel);
            Assert.NotNull(t.AtemInputId);
            Assert.True(t.AtemInputId >= 1);
            Assert.True(t.AtemChannel >= 0);
        });
    }

    [Fact]
    public void GetAtemCommandTemplates_RunMacroTemplatesHaveMacroId()
    {
        var templates = CommandTemplateSeeder.GetAtemCommandTemplates();
        var macroTemplates = templates.Where(t => t.AtemFunction == "RunMacro").ToList();

        Assert.NotEmpty(macroTemplates);
        Assert.All(macroTemplates, t => Assert.NotNull(t.AtemMacroId));
    }

    [Fact]
    public void GetAtemCommandTemplates_StatusTemplatesHaveNoInputId()
    {
        var templates = CommandTemplateSeeder.GetAtemCommandTemplates();
        var statusFunctions = new[] { "GetProgramInput", "GetPreviewInput", "ListMacros" };
        var statusTemplates = templates.Where(t => statusFunctions.Contains(t.AtemFunction)).ToList();

        Assert.NotEmpty(statusTemplates);
        Assert.All(statusTemplates, t => Assert.Null(t.AtemInputId));
    }

    [Fact]
    public void GetAtemCommandTemplates_GetAuxSourceTemplatesHaveChannel()
    {
        var templates = CommandTemplateSeeder.GetAtemCommandTemplates();
        var auxSourceTemplates = templates.Where(t => t.AtemFunction == "GetAuxSource").ToList();

        Assert.NotEmpty(auxSourceTemplates);
        Assert.All(auxSourceTemplates, t => Assert.NotNull(t.AtemChannel));
    }

    [Fact]
    public void GetAtemCommandTemplates_CommandDataIsAlwaysNull()
    {
        // CommandData is not used for ATEM commands; the ATEM-specific fields are used instead.
        var templates = CommandTemplateSeeder.GetAtemCommandTemplates();
        Assert.All(templates, t => Assert.Null(t.Payload));
    }

    [Fact]
    public void GetAtemCommandTemplates_AllIdsAreUnique()
    {
        var templates = CommandTemplateSeeder.GetAtemCommandTemplates();
        var ids = templates.Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void SeedCommandTemplates_IsIdempotentAcrossBothDeviceTypes()
    {
        using var context = CreateInMemoryContext();

        CommandTemplateSeeder.SeedCommandTemplates(context);
        int firstCount = context.CommandTemplates.Count();

        CommandTemplateSeeder.SeedCommandTemplates(context);
        int secondCount = context.CommandTemplates.Count();

        Assert.Equal(firstCount, secondCount);
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
