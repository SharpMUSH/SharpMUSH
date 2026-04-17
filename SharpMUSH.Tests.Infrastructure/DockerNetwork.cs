using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Shared Docker network for all test containers.
/// Allows containers to communicate with each other using container names as hostnames.
/// </summary>
public class DockerNetwork : IAsyncInitializer, IAsyncDisposable
{
	public INetwork Instance { get; } = new NetworkBuilder()
		.WithName($"tunit-sharpmush-{Guid.NewGuid():N}")
		.Build();

	public async Task InitializeAsync() => await Instance.CreateAsync();
	public async ValueTask DisposeAsync() => await Instance.DisposeAsync();
}
