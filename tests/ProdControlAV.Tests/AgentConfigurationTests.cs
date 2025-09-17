using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ProdControlAV.Agent.Services;
using Xunit;

namespace ProdControlAV.Tests;

public class AgentConfigurationTests
{
    [Fact]
    public void ApiOptions_WithValidConfiguration_ShouldConfigureCorrectly()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Api:BaseUrl", "https://api.example.com/api" },
                { "Api:DevicesEndpoint", "/devices" },
                { "Api:StatusEndpoint", "/status" },
                { "Api:ApiKey", "12345678901234567890123456789012" } // 32 characters
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<ApiOptions>(configuration.GetSection("Api"));
        
        // Simulate the PostConfigure from Program.cs
        services.PostConfigure<ApiOptions>(options =>
        {
            var envApiUrl = Environment.GetEnvironmentVariable("PRODCONTROL_API_URL");
            if (!string.IsNullOrWhiteSpace(envApiUrl))
            {
                options.BaseUrl = envApiUrl;
            }
            
            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                options.ApiKey = Environment.GetEnvironmentVariable("PRODCONTROL_AGENT_APIKEY");
            }
            
            if (string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                throw new InvalidOperationException(
                    "Agent API Base URL must be provided either in configuration (Api:BaseUrl) " +
                    "or via environment variable (PRODCONTROL_API_URL)");
            }
            
            if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
            {
                throw new InvalidOperationException(
                    $"Agent API Base URL '{options.BaseUrl}' is not a valid URI");
            }
            
            if (baseUri.Scheme != "https" && baseUri.Scheme != "http")
            {
                throw new InvalidOperationException(
                    $"Agent API Base URL '{options.BaseUrl}' must use http or https scheme");
            }
            
            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                throw new InvalidOperationException(
                    "Agent API Key must be provided either in configuration (Api:ApiKey) " +
                    "or via environment variable (PRODCONTROL_AGENT_APIKEY)");
            }
            
            if (options.ApiKey.Length < 32)
            {
                throw new InvalidOperationException(
                    "Agent API Key must be at least 32 characters long for security");
            }
        });

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var options = serviceProvider.GetRequiredService<IOptions<ApiOptions>>().Value;

        // Assert
        Assert.Equal("https://api.example.com/api", options.BaseUrl);
        Assert.Equal("/devices", options.DevicesEndpoint);
        Assert.Equal("/status", options.StatusEndpoint);
        Assert.Equal("12345678901234567890123456789012", options.ApiKey);
    }

    [Fact]
    public void ApiOptions_WithEnvironmentVariableBaseUrl_ShouldOverrideConfiguration()
    {
        // Arrange
        var envBaseUrl = "https://env.example.com/api";
        Environment.SetEnvironmentVariable("PRODCONTROL_API_URL", envBaseUrl);
        
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "Api:BaseUrl", "https://config.example.com/api" },
                    { "Api:DevicesEndpoint", "/devices" },
                    { "Api:StatusEndpoint", "/status" },
                    { "Api:ApiKey", "12345678901234567890123456789012" }
                })
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.Configure<ApiOptions>(configuration.GetSection("Api"));
            
            // Simulate the PostConfigure from Program.cs
            services.PostConfigure<ApiOptions>(options =>
            {
                var envApiUrl = Environment.GetEnvironmentVariable("PRODCONTROL_API_URL");
                if (!string.IsNullOrWhiteSpace(envApiUrl))
                {
                    options.BaseUrl = envApiUrl;
                }
                
                if (string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    options.ApiKey = Environment.GetEnvironmentVariable("PRODCONTROL_AGENT_APIKEY");
                }
                
                if (string.IsNullOrWhiteSpace(options.BaseUrl))
                {
                    throw new InvalidOperationException(
                        "Agent API Base URL must be provided either in configuration (Api:BaseUrl) " +
                        "or via environment variable (PRODCONTROL_API_URL)");
                }
                
                if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
                {
                    throw new InvalidOperationException(
                        $"Agent API Base URL '{options.BaseUrl}' is not a valid URI");
                }
                
                if (baseUri.Scheme != "https" && baseUri.Scheme != "http")
                {
                    throw new InvalidOperationException(
                        $"Agent API Base URL '{options.BaseUrl}' must use http or https scheme");
                }
                
                if (string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    throw new InvalidOperationException(
                        "Agent API Key must be provided either in configuration (Api:ApiKey) " +
                        "or via environment variable (PRODCONTROL_AGENT_APIKEY)");
                }
                
                if (options.ApiKey.Length < 32)
                {
                    throw new InvalidOperationException(
                        "Agent API Key must be at least 32 characters long for security");
                }
            });

            var serviceProvider = services.BuildServiceProvider();

            // Act
            var options = serviceProvider.GetRequiredService<IOptions<ApiOptions>>().Value;

            // Assert
            Assert.Equal(envBaseUrl, options.BaseUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PRODCONTROL_API_URL", null);
        }
    }

    [Fact]
    public void ApiOptions_WithEnvironmentVariableApiKey_ShouldOverrideConfiguration()
    {
        // Arrange
        var envApiKey = "environment_api_key_32_chars_long";
        Environment.SetEnvironmentVariable("PRODCONTROL_AGENT_APIKEY", envApiKey);
        
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "Api:BaseUrl", "https://api.example.com/api" },
                    { "Api:DevicesEndpoint", "/devices" },
                    { "Api:StatusEndpoint", "/status" },
                    { "Api:ApiKey", "" } // Empty in config
                })
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.Configure<ApiOptions>(configuration.GetSection("Api"));
            
            // Simulate the PostConfigure from Program.cs
            services.PostConfigure<ApiOptions>(options =>
            {
                var envApiUrl = Environment.GetEnvironmentVariable("PRODCONTROL_API_URL");
                if (!string.IsNullOrWhiteSpace(envApiUrl))
                {
                    options.BaseUrl = envApiUrl;
                }
                
                if (string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    options.ApiKey = Environment.GetEnvironmentVariable("PRODCONTROL_AGENT_APIKEY");
                }
                
                if (string.IsNullOrWhiteSpace(options.BaseUrl))
                {
                    throw new InvalidOperationException(
                        "Agent API Base URL must be provided either in configuration (Api:BaseUrl) " +
                        "or via environment variable (PRODCONTROL_API_URL)");
                }
                
                if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
                {
                    throw new InvalidOperationException(
                        $"Agent API Base URL '{options.BaseUrl}' is not a valid URI");
                }
                
                if (baseUri.Scheme != "https" && baseUri.Scheme != "http")
                {
                    throw new InvalidOperationException(
                        $"Agent API Base URL '{options.BaseUrl}' must use http or https scheme");
                }
                
                if (string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    throw new InvalidOperationException(
                        "Agent API Key must be provided either in configuration (Api:ApiKey) " +
                        "or via environment variable (PRODCONTROL_AGENT_APIKEY)");
                }
                
                if (options.ApiKey.Length < 32)
                {
                    throw new InvalidOperationException(
                        "Agent API Key must be at least 32 characters long for security");
                }
            });

            var serviceProvider = services.BuildServiceProvider();

            // Act
            var options = serviceProvider.GetRequiredService<IOptions<ApiOptions>>().Value;

            // Assert
            Assert.Equal(envApiKey, options.ApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PRODCONTROL_AGENT_APIKEY", null);
        }
    }

    [Fact]
    public void ApiOptions_WithMissingBaseUrl_ShouldThrowException()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Api:BaseUrl", "" }, // Empty base URL
                { "Api:DevicesEndpoint", "/devices" },
                { "Api:StatusEndpoint", "/status" },
                { "Api:ApiKey", "12345678901234567890123456789012" }
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<ApiOptions>(configuration.GetSection("Api"));
        
        // Simulate the PostConfigure from Program.cs
        services.PostConfigure<ApiOptions>(options =>
        {
            var envApiUrl = Environment.GetEnvironmentVariable("PRODCONTROL_API_URL");
            if (!string.IsNullOrWhiteSpace(envApiUrl))
            {
                options.BaseUrl = envApiUrl;
            }
            
            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                options.ApiKey = Environment.GetEnvironmentVariable("PRODCONTROL_AGENT_APIKEY");
            }
            
            if (string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                throw new InvalidOperationException(
                    "Agent API Base URL must be provided either in configuration (Api:BaseUrl) " +
                    "or via environment variable (PRODCONTROL_API_URL)");
            }
            
            if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
            {
                throw new InvalidOperationException(
                    $"Agent API Base URL '{options.BaseUrl}' is not a valid URI");
            }
            
            if (baseUri.Scheme != "https" && baseUri.Scheme != "http")
            {
                throw new InvalidOperationException(
                    $"Agent API Base URL '{options.BaseUrl}' must use http or https scheme");
            }
            
            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                throw new InvalidOperationException(
                    "Agent API Key must be provided either in configuration (Api:ApiKey) " +
                    "or via environment variable (PRODCONTROL_AGENT_APIKEY)");
            }
            
            if (options.ApiKey.Length < 32)
            {
                throw new InvalidOperationException(
                    "Agent API Key must be at least 32 characters long for security");
            }
        });

        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            serviceProvider.GetRequiredService<IOptions<ApiOptions>>().Value);
        
        Assert.Contains("Agent API Base URL must be provided", exception.Message);
    }

    [Fact]
    public void ApiOptions_WithInvalidUri_ShouldThrowException()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Api:BaseUrl", "not-a-valid-uri" },
                { "Api:DevicesEndpoint", "/devices" },
                { "Api:StatusEndpoint", "/status" },
                { "Api:ApiKey", "12345678901234567890123456789012" }
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<ApiOptions>(configuration.GetSection("Api"));
        
        // Simulate the PostConfigure from Program.cs
        services.PostConfigure<ApiOptions>(options =>
        {
            var envApiUrl = Environment.GetEnvironmentVariable("PRODCONTROL_API_URL");
            if (!string.IsNullOrWhiteSpace(envApiUrl))
            {
                options.BaseUrl = envApiUrl;
            }
            
            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                options.ApiKey = Environment.GetEnvironmentVariable("PRODCONTROL_AGENT_APIKEY");
            }
            
            if (string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                throw new InvalidOperationException(
                    "Agent API Base URL must be provided either in configuration (Api:BaseUrl) " +
                    "or via environment variable (PRODCONTROL_API_URL)");
            }
            
            if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
            {
                throw new InvalidOperationException(
                    $"Agent API Base URL '{options.BaseUrl}' is not a valid URI");
            }
            
            if (baseUri.Scheme != "https" && baseUri.Scheme != "http")
            {
                throw new InvalidOperationException(
                    $"Agent API Base URL '{options.BaseUrl}' must use http or https scheme");
            }
            
            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                throw new InvalidOperationException(
                    "Agent API Key must be provided either in configuration (Api:ApiKey) " +
                    "or via environment variable (PRODCONTROL_AGENT_APIKEY)");
            }
            
            if (options.ApiKey.Length < 32)
            {
                throw new InvalidOperationException(
                    "Agent API Key must be at least 32 characters long for security");
            }
        });

        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            serviceProvider.GetRequiredService<IOptions<ApiOptions>>().Value);
        
        Assert.Contains("is not a valid URI", exception.Message);
    }

    [Fact]
    public void ApiOptions_WithInvalidScheme_ShouldThrowException()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Api:BaseUrl", "ftp://api.example.com/api" },
                { "Api:DevicesEndpoint", "/devices" },
                { "Api:StatusEndpoint", "/status" },
                { "Api:ApiKey", "12345678901234567890123456789012" }
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<ApiOptions>(configuration.GetSection("Api"));
        
        // Simulate the PostConfigure from Program.cs
        services.PostConfigure<ApiOptions>(options =>
        {
            var envApiUrl = Environment.GetEnvironmentVariable("PRODCONTROL_API_URL");
            if (!string.IsNullOrWhiteSpace(envApiUrl))
            {
                options.BaseUrl = envApiUrl;
            }
            
            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                options.ApiKey = Environment.GetEnvironmentVariable("PRODCONTROL_AGENT_APIKEY");
            }
            
            if (string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                throw new InvalidOperationException(
                    "Agent API Base URL must be provided either in configuration (Api:BaseUrl) " +
                    "or via environment variable (PRODCONTROL_API_URL)");
            }
            
            if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
            {
                throw new InvalidOperationException(
                    $"Agent API Base URL '{options.BaseUrl}' is not a valid URI");
            }
            
            if (baseUri.Scheme != "https" && baseUri.Scheme != "http")
            {
                throw new InvalidOperationException(
                    $"Agent API Base URL '{options.BaseUrl}' must use http or https scheme");
            }
            
            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                throw new InvalidOperationException(
                    "Agent API Key must be provided either in configuration (Api:ApiKey) " +
                    "or via environment variable (PRODCONTROL_AGENT_APIKEY)");
            }
            
            if (options.ApiKey.Length < 32)
            {
                throw new InvalidOperationException(
                    "Agent API Key must be at least 32 characters long for security");
            }
        });

        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            serviceProvider.GetRequiredService<IOptions<ApiOptions>>().Value);
        
        Assert.Contains("must use http or https scheme", exception.Message);
    }

    [Fact]
    public void ApiOptions_WithMissingApiKey_ShouldThrowException()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Api:BaseUrl", "https://api.example.com/api" },
                { "Api:DevicesEndpoint", "/devices" },
                { "Api:StatusEndpoint", "/status" },
                { "Api:ApiKey", "" } // Empty API key
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<ApiOptions>(configuration.GetSection("Api"));
        
        // Simulate the PostConfigure from Program.cs
        services.PostConfigure<ApiOptions>(options =>
        {
            var envApiUrl = Environment.GetEnvironmentVariable("PRODCONTROL_API_URL");
            if (!string.IsNullOrWhiteSpace(envApiUrl))
            {
                options.BaseUrl = envApiUrl;
            }
            
            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                options.ApiKey = Environment.GetEnvironmentVariable("PRODCONTROL_AGENT_APIKEY");
            }
            
            if (string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                throw new InvalidOperationException(
                    "Agent API Base URL must be provided either in configuration (Api:BaseUrl) " +
                    "or via environment variable (PRODCONTROL_API_URL)");
            }
            
            if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
            {
                throw new InvalidOperationException(
                    $"Agent API Base URL '{options.BaseUrl}' is not a valid URI");
            }
            
            if (baseUri.Scheme != "https" && baseUri.Scheme != "http")
            {
                throw new InvalidOperationException(
                    $"Agent API Base URL '{options.BaseUrl}' must use http or https scheme");
            }
            
            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                throw new InvalidOperationException(
                    "Agent API Key must be provided either in configuration (Api:ApiKey) " +
                    "or via environment variable (PRODCONTROL_AGENT_APIKEY)");
            }
            
            if (options.ApiKey.Length < 32)
            {
                throw new InvalidOperationException(
                    "Agent API Key must be at least 32 characters long for security");
            }
        });

        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            serviceProvider.GetRequiredService<IOptions<ApiOptions>>().Value);
        
        Assert.Contains("Agent API Key must be provided", exception.Message);
    }

    [Fact]
    public void ApiOptions_WithShortApiKey_ShouldThrowException()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Api:BaseUrl", "https://api.example.com/api" },
                { "Api:DevicesEndpoint", "/devices" },
                { "Api:StatusEndpoint", "/status" },
                { "Api:ApiKey", "short" } // Too short
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<ApiOptions>(configuration.GetSection("Api"));
        
        // Simulate the PostConfigure from Program.cs
        services.PostConfigure<ApiOptions>(options =>
        {
            var envApiUrl = Environment.GetEnvironmentVariable("PRODCONTROL_API_URL");
            if (!string.IsNullOrWhiteSpace(envApiUrl))
            {
                options.BaseUrl = envApiUrl;
            }
            
            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                options.ApiKey = Environment.GetEnvironmentVariable("PRODCONTROL_AGENT_APIKEY");
            }
            
            if (string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                throw new InvalidOperationException(
                    "Agent API Base URL must be provided either in configuration (Api:BaseUrl) " +
                    "or via environment variable (PRODCONTROL_API_URL)");
            }
            
            if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
            {
                throw new InvalidOperationException(
                    $"Agent API Base URL '{options.BaseUrl}' is not a valid URI");
            }
            
            if (baseUri.Scheme != "https" && baseUri.Scheme != "http")
            {
                throw new InvalidOperationException(
                    $"Agent API Base URL '{options.BaseUrl}' must use http or https scheme");
            }
            
            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                throw new InvalidOperationException(
                    "Agent API Key must be provided either in configuration (Api:ApiKey) " +
                    "or via environment variable (PRODCONTROL_AGENT_APIKEY)");
            }
            
            if (options.ApiKey.Length < 32)
            {
                throw new InvalidOperationException(
                    "Agent API Key must be at least 32 characters long for security");
            }
        });

        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            serviceProvider.GetRequiredService<IOptions<ApiOptions>>().Value);
        
        Assert.Contains("must be at least 32 characters long", exception.Message);
    }
}