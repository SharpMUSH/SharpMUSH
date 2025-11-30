using Testcontainers.RabbitMq;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

public class RabbitMqTestServer : IAsyncInitializer, IAsyncDisposable
{
	public RabbitMqContainer Instance { get; } = new RabbitMqBuilder()
		.WithUsername("sharpmush")
		.WithPassword("sharpmush_dev_password")
		.WithPortBinding(5672,5672)
		.Build();

	public async Task InitializeAsync() => await Instance.StartAsync();
	public async ValueTask DisposeAsync() => await Instance.DisposeAsync();
}