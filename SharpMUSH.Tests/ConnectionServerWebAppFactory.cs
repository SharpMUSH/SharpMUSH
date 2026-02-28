using TUnit.AspNetCore;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Integration test factory for SharpMUSH.ConnectionServer.
/// Manages test infrastructure lifecycle and provides access to services.
/// </summary>
public class ConnectionServerWebAppFactory : TestWebApplicationFactory<SharpMUSH.ConnectionServer.Program>, IAsyncInitializer, IAsyncDisposable
{
	[ClassDataSource<DockerNetwork>(Shared = SharedType.PerTestSession)]
	public required DockerNetwork DockerNetwork { get; init; }

	[ClassDataSource<NatsTestServer>(Shared = SharedType.PerTestSession)]
	public required NatsTestServer NatsTestServer { get; init; }

	public new IServiceProvider Services => _server!.Services;
	private ConnectionServerTestWebApplicationBuilderFactory<SharpMUSH.ConnectionServer.Program>? _server;

	public virtual async Task InitializeAsync()
	{
		var natsPort = NatsTestServer.Instance.GetMappedPublicPort(4222);
		var natsUrl = $"nats://localhost:{natsPort}";

		_server = new ConnectionServerTestWebApplicationBuilderFactory<SharpMUSH.ConnectionServer.Program>(natsUrl);
	}

	public new async ValueTask DisposeAsync()
	{
		GC.SuppressFinalize(this);
		await ValueTask.CompletedTask;
	}
}
