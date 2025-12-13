using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class CommandDiscoveryServiceTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private ICommandDiscoveryService CommandDiscoveryService => 
		WebAppFactoryArg.Services.GetRequiredService<ICommandDiscoveryService>();

	[Test]
	public async ValueTask CommandDiscoveryServiceIsRegistered()
	{
		// Verify the service is properly registered in DI container
		var service = WebAppFactoryArg.Services.GetRequiredService<ICommandDiscoveryService>();
		await Assert.That(service).IsNotNull();
	}

	[Test]
	[Skip("Integration test - requires database setup with command attributes")]
	public async ValueTask MatchUserDefinedCommand_WithCaching_ReturnsSameResults()
	{
		// This test would verify that cached results match uncached results
		// Would need proper database setup with objects that have $command: attributes
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Integration test - requires database setup with command attributes")]
	public async ValueTask MatchUserDefinedCommand_AfterCacheInvalidation_RebuildsCache()
	{
		// This test would verify that after cache invalidation (via attribute commands), the cache is rebuilt
		// Cache is invalidated automatically when attribute commands execute via Mediator pipeline
		// Would need proper database setup with objects that have $command: attributes
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Performance test - requires benchmark infrastructure")]
	public async ValueTask MatchUserDefinedCommand_CachedIsFasterThanUncached()
	{
		// This test would verify that cached lookups are faster than uncached
		// Would need proper benchmark infrastructure and timing
		await ValueTask.CompletedTask;
	}
}
