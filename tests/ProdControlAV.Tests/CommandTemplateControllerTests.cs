using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ProdControlAV.API.Controllers;
using ProdControlAV.API.Data;
using ProdControlAV.Core.Models;
using ProdControlAV.Infrastructure.Services;
using Xunit;

namespace ProdControlAV.Tests;

public class CommandTemplateControllerTests
{
    private readonly Guid _testTenantId = Guid.NewGuid();

    private (AppDbContext context, CommandTemplateController controller) CreateTestContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var tenantProvider = new TestTenantProvider(_testTenantId);
        var context = new AppDbContext(options, tenantProvider);
        
        // Mock IDeviceActionStore
        var deviceActionStore = new Mock<IDeviceActionStore>();
        
        // Mock ILogger
        var logger = new Mock<ILogger<CommandTemplateController>>();
        
        var controller = new CommandTemplateController(context, tenantProvider, deviceActionStore.Object, logger.Object);

        return (context, controller);
    }

    [Fact]
    public async Task GetTemplates_ReturnsAllActiveTemplates()
    {
        // Arrange
        var (context, controller) = CreateTestContext();
        CommandTemplateSeeder.SeedCommandTemplates(context);

        // Act
        var result = await controller.GetTemplates();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var templates = Assert.IsAssignableFrom<System.Collections.Generic.IEnumerable<CommandTemplate>>(okResult.Value);
        Assert.NotEmpty(templates);
        Assert.All(templates, t => Assert.True(t.IsActive));
    }

    [Fact]
    public async Task GetTemplates_FiltersByDeviceType()
    {
        // Arrange
        var (context, controller) = CreateTestContext();
        CommandTemplateSeeder.SeedCommandTemplates(context);

        // Act
        var result = await controller.GetTemplates("HyperDeck");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var templates = Assert.IsAssignableFrom<System.Collections.Generic.IEnumerable<CommandTemplate>>(okResult.Value);
        Assert.All(templates, t => Assert.Equal("HyperDeck", t.DeviceType));
    }

    [Fact]
    public async Task GetTemplate_ReturnsTemplateById()
    {
        // Arrange
        var (context, controller) = CreateTestContext();
        var template = new CommandTemplate
        {
            Id = Guid.NewGuid(),
            Category = "Test",
            Name = "Test Command",
            Description = "Test Description",
            HttpMethod = "GET",
            Endpoint = "/test",
            DeviceType = "HyperDeck",
            DisplayOrder = 1,
            IsActive = true
        };
        context.CommandTemplates.Add(template);
        await context.SaveChangesAsync();

        // Act
        var result = await controller.GetTemplate(template.Id);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedTemplate = Assert.IsType<CommandTemplate>(okResult.Value);
        Assert.Equal(template.Id, returnedTemplate.Id);
        Assert.Equal(template.Name, returnedTemplate.Name);
    }

    [Fact]
    public async Task GetTemplate_ReturnsNotFound_WhenTemplateDoesNotExist()
    {
        // Arrange
        var (context, controller) = CreateTestContext();

        // Act
        var result = await controller.GetTemplate(Guid.NewGuid());

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task ApplyTemplate_CreatesDeviceAction()
    {
        // Arrange
        var (context, controller) = CreateTestContext();
        
        var device = new Device
        {
            Id = Guid.NewGuid(),
            Name = "Test Device",
            Model = "HyperDeck Studio",
            Brand = "Blackmagic",
            Type = "HyperDeck",
            Ip = "192.168.1.100",
            Port = 9993,
            TenantId = _testTenantId,
            Status = true
        };
        context.Devices.Add(device);

        var template = new CommandTemplate
        {
            Id = Guid.NewGuid(),
            Category = "Transport Control",
            Name = "Play",
            Description = "Start playback",
            HttpMethod = "PUT",
            Endpoint = "/transports/1",
            Payload = "{\"play\":true}",
            DeviceType = "HyperDeck",
            DisplayOrder = 1,
            IsActive = true
        };
        context.CommandTemplates.Add(template);
        await context.SaveChangesAsync();

        var request = new CommandTemplateController.ApplyTemplateRequest(device.Id);

        // Act
        var result = await controller.ApplyTemplate(template.Id, request);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var deviceAction = Assert.IsType<DeviceAction>(createdResult.Value);
        Assert.Equal(device.Id, deviceAction.DeviceId);
        Assert.Equal(_testTenantId, deviceAction.TenantId);
        Assert.Equal(template.Name, deviceAction.ActionName);
        Assert.Equal(template.Endpoint, deviceAction.Command);
        Assert.Equal(template.HttpMethod, deviceAction.HttpMethod);

        // Verify it was saved to database
        var savedAction = await context.DeviceActions.FindAsync(deviceAction.ActionId);
        Assert.NotNull(savedAction);
    }

    [Fact]
    public async Task ApplyTemplate_WithCustomName_UsesCustomName()
    {
        // Arrange
        var (context, controller) = CreateTestContext();
        
        var device = new Device
        {
            Id = Guid.NewGuid(),
            Name = "Test Device",
            Model = "HyperDeck Studio",
            Brand = "Blackmagic",
            Type = "HyperDeck",
            Ip = "192.168.1.100",
            Port = 9993,
            TenantId = _testTenantId,
            Status = true
        };
        context.Devices.Add(device);

        var template = new CommandTemplate
        {
            Id = Guid.NewGuid(),
            Category = "Transport Control",
            Name = "Play",
            Description = "Start playback",
            HttpMethod = "PUT",
            Endpoint = "/transports/1",
            DeviceType = "HyperDeck",
            DisplayOrder = 1,
            IsActive = true
        };
        context.CommandTemplates.Add(template);
        await context.SaveChangesAsync();

        var customName = "My Custom Play Button";
        var request = new CommandTemplateController.ApplyTemplateRequest(device.Id, customName);

        // Act
        var result = await controller.ApplyTemplate(template.Id, request);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var deviceAction = Assert.IsType<DeviceAction>(createdResult.Value);
        Assert.Equal(customName, deviceAction.ActionName);
    }

    [Fact]
    public async Task ApplyTemplate_ReturnsNotFound_WhenTemplateDoesNotExist()
    {
        // Arrange
        var (context, controller) = CreateTestContext();
        
        var device = new Device
        {
            Id = Guid.NewGuid(),
            Name = "Test Device",
            Model = "HyperDeck Studio",
            Brand = "Blackmagic",
            Type = "HyperDeck",
            Ip = "192.168.1.100",
            Port = 9993,
            TenantId = _testTenantId,
            Status = true
        };
        context.Devices.Add(device);
        await context.SaveChangesAsync();

        var request = new CommandTemplateController.ApplyTemplateRequest(device.Id);

        // Act
        var result = await controller.ApplyTemplate(Guid.NewGuid(), request);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task ApplyTemplate_ReturnsNotFound_WhenDeviceDoesNotExist()
    {
        // Arrange
        var (context, controller) = CreateTestContext();
        
        var template = new CommandTemplate
        {
            Id = Guid.NewGuid(),
            Category = "Transport Control",
            Name = "Play",
            Description = "Start playback",
            HttpMethod = "PUT",
            Endpoint = "/transports/1",
            DeviceType = "HyperDeck",
            DisplayOrder = 1,
            IsActive = true
        };
        context.CommandTemplates.Add(template);
        await context.SaveChangesAsync();

        var request = new CommandTemplateController.ApplyTemplateRequest(Guid.NewGuid());

        // Act
        var result = await controller.ApplyTemplate(template.Id, request);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task ApplyTemplate_ReturnsBadRequest_WhenTemplateIsInactive()
    {
        // Arrange
        var (context, controller) = CreateTestContext();
        
        var device = new Device
        {
            Id = Guid.NewGuid(),
            Name = "Test Device",
            Model = "HyperDeck Studio",
            Brand = "Blackmagic",
            Type = "HyperDeck",
            Ip = "192.168.1.100",
            Port = 9993,
            TenantId = _testTenantId,
            Status = true
        };
        context.Devices.Add(device);

        var template = new CommandTemplate
        {
            Id = Guid.NewGuid(),
            Category = "Transport Control",
            Name = "Play",
            Description = "Start playback",
            HttpMethod = "PUT",
            Endpoint = "/transports/1",
            DeviceType = "HyperDeck",
            DisplayOrder = 1,
            IsActive = false // Inactive template
        };
        context.CommandTemplates.Add(template);
        await context.SaveChangesAsync();

        var request = new CommandTemplateController.ApplyTemplateRequest(device.Id);

        // Act
        var result = await controller.ApplyTemplate(template.Id, request);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
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
