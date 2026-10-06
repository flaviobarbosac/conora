using Conora.Domain.Ports;
using Conora.Infrastructure.Persistence;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Conora.Infrastructure.Messaging;

public static class MessagingExtensions
{
    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var useInMemory = string.Equals(
            configuration["MassTransit:Transport"],
            "InMemory",
            StringComparison.OrdinalIgnoreCase);

        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();

            x.AddEntityFrameworkOutbox<AppDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
            });

            if (useInMemory)
            {
                x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
                return;
            }

            x.UsingRabbitMq((context, cfg) =>
            {
                var host = configuration.GetConnectionString("RabbitMq");
                if (string.IsNullOrWhiteSpace(host))
                    host = "amqp://guest:guest@localhost:5673";
                cfg.Host(new Uri(host));
                cfg.ConfigureEndpoints(context);
            });
        });

        services.AddScoped<IMessageBus, MassTransitMessageBus>();
        return services;
    }
}
