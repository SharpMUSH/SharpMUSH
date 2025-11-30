using MassTransit;

namespace SharpMUSH.Server.Strategy.RabbitMQ;

public class RabbitMQContainerServiceStrategy : RabbitMQServiceStrategy
{
	public override void ConfigureRabbitMq(IBusRegistrationContext context, IRabbitMqBusFactoryConfigurator cfg)
	{
		var rabbitHost = Environment.GetEnvironmentVariable("RABBITMQ_HOST") 
			?? throw new NullReferenceException("RABBITMQ_HOST");
		var rabbitUser = Environment.GetEnvironmentVariable("RABBITMQ_USER") 
			?? throw new NullReferenceException("RABBITMQ_USER");
		var rabbitPass = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") 
			?? throw new NullReferenceException("RABBITMQ_PASSWORD");

		cfg.Lazy = false;
		cfg.Durable = false;
		cfg.Host(rabbitHost, "/", h =>
		{
			h.Username(rabbitUser);
			h.Password(rabbitPass);
		});

		// Configure message retry
		cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));

		// Configure endpoints for consumers
		cfg.ConfigureEndpoints(context);
	}
}
