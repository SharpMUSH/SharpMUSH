using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;

namespace SharpMUSH.Tests.Internal;

/// <summary>
/// The session's throwaway world skips the per-commit fsync (<see cref="TestDatabaseStorage"/>). Only
/// periodic mode runs the flush timer, so a flush after a write is the proof it took effect.
/// </summary>
public class TestWorldSyncModeTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	[Test]
	public async Task TheTestWorldIsOpenedInPeriodicSyncMode()
	{
		var store = WebAppFactoryArg.Services.GetRequiredService<LightningDatabase>().Store;

		await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services,
			WebAppFactoryArg.Services.GetRequiredService<IMediator>(), "SyncModeProbe");

		await Assert.That(() => store.FlushCount).WaitsFor(count => count.IsGreaterThan(0),
			timeout: TimeSpan.FromSeconds(10), pollingInterval: TimeSpan.FromMilliseconds(100));
	}
}
