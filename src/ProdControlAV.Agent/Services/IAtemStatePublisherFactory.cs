// csharp
// File: AtemStatePublisherFactory.cs

using ProdControlAV.Agent.Services;

public interface IAtemStatePublisherFactory
{
    AtemStatePublisher Create(HttpClient http, ILogger<AtemStatePublisher> logger, Guid deviceId);
}

public sealed class AtemStatePublisherFactory : IAtemStatePublisherFactory
{
    private readonly IServiceProvider _sp;

    public AtemStatePublisherFactory(IServiceProvider sp) => _sp = sp;

    public AtemStatePublisher Create(HttpClient http, ILogger<AtemStatePublisher> logger, Guid deviceId)
    {
        var jwt = _sp.GetService<JwtAuthService>(); // may be null
        return new AtemStatePublisher(http, logger, deviceId, jwt);
    }
}