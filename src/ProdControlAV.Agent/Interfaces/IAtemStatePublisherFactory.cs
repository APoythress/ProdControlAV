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
        return new AtemStatePublisher(http, logger, deviceId);
    }
}