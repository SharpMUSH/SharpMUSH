using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models.RecurringJobs;
using SharpMUSH.Library.Services.RecurringJobs;
using SharpMUSH.Tests.Commands;

namespace SharpMUSH.Tests.Internal;

/// <summary>
/// Test hosts share one world, so work a host does in the background lands in other tests' assertions.
/// </summary>
public class TestWorldIsolationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	// Requested only so the secondary host is running while the store is watched.
	[ClassDataSource<RealityGameServerFactory>(Shared = SharedType.PerTestSession)]
	public required RealityGameServerFactory SecondaryHost { get; init; }

	[Test]
	[NotInParallel]
	public async Task NoTestHostFiresRecurringJobsInTheBackground()
	{
		// RecurringJobTests drives the job document by hand, on a clock of its own. A background runner on
		// the same document fires those jobs on the real clock and rewrites what the tests assert on.
		var store = Factory.Services.GetRequiredService<IExpandedDataStore>();
		var original = await store.GetExpandedServerData<RecurringJobDocument>(RecurringJobService.StorageKey);
		var overdue = new RecurringJob(Guid.NewGuid().ToString("N"), "isolation", "#1:1", "#0:1", "RUN",
			"* * * * *", "UTC", "must not fire", true, 1, 1, null, null, "scheduled", null);
		try
		{
			await store.SetExpandedServerData(RecurringJobService.StorageKey, new RecurringJobDocument([overdue]));
			// Both hosts poll once a second.
			await Task.Delay(TimeSpan.FromSeconds(2.5));
			var document = await store.GetExpandedServerData<RecurringJobDocument>(RecurringJobService.StorageKey);
			await Assert.That(document!.Jobs.Single()).IsEqualTo(overdue);
		}
		finally
		{
			await store.SetExpandedServerData(RecurringJobService.StorageKey, original ?? new RecurringJobDocument([]));
		}
	}
}
