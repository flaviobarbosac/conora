using Conora.Domain.Ports;
using MassTransit;

namespace Conora.Infrastructure.Messaging;

public sealed class MassTransitMessageBus : IMessageBus
{
    private readonly IPublishEndpoint _publishEndpoint;

    public MassTransitMessageBus(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
        => _publishEndpoint.Publish(message, cancellationToken);
}
