using MassTransit;

namespace SharpMUSH.Server.Strategy.RabbitMQ;

public class RabbitMQTestContainerServiceStrategy : RabbitMQServiceStrategy
{
	public override void ConfigureRabbitMq(IBusRegistrationContext context, IRabbitMqBusFactoryConfigurator cfg)
	{
		cfg.Lazy = false;
		cfg.Durable = false;
		cfg.Host("localhost", "/", h =>
		{
			h.Username("sharpmush");
			h.Password("sharpmush_dev_password");
		});

		// Configure message retry
		cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));

		// Configure endpoints for consumers
		cfg.ConfigureEndpoints(context);
	}
}
