using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Testcontainer for Jaeger all-in-one instance for OpenTelemetry metrics collection.
/// Exposes OTLP gRPC (4317), OTLP HTTP (4318), and Jaeger UI (16686).
/// </summary>
public class JaegerTestServer : IAsyncInitializer, IAsyncDisposable
{
	public IContainer Instance { get; } = new ContainerBuilder()
		.WithImage("jaegertracing/all-in-one:latest")
		.WithPortBinding(16686, 16686) // Jaeger UI
		.WithPortBinding(4317, 4317)   // OTLP gRPC
		.WithPortBinding(4318, 4318)   // OTLP HTTP
		.WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(16686)))
		.WithReuse(false)
		.Build();

	public async Task InitializeAsync() => await Instance.StartAsync();
	public async ValueTask DisposeAsync() => await Instance.DisposeAsync();
}
