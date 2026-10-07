using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Tests.Database.Lightning;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@storage</c> through the command path: the wizard lock, and that each form answers with what it
/// promises — the capacity figures, the history counts with their policy, and a purge that under the
/// default policy keeps everything.
/// </summary>
public class StorageCommandTests : ServerTestBase
{
	private async Task<IReadOnlyList<string>> HeardAfterAsync(string command)
	{
		// The recorder accumulates for the whole session, so read only what this command added.
		var before = Notifications.DeliveryCountFor(WebAppFactoryArg.ExecutorDBRef);
		await Cmd(command);
		return Notifications.DeliveriesFor(WebAppFactoryArg.ExecutorDBRef)
			.Skip(before)
			.Select(delivery => delivery.Message)
			.ToList();
	}

	[Test]
	public async Task AMortalCannotRunIt()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services,
			WebAppFactoryArg.Services.GetRequiredService<IMediator>(),
			ConnectionService,
			"StorageMortal");

		var answer = await CmdAs(mortal.DbRef, mortal.Handle, "@storage/purge");

		await Assert.That(answer).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
	}

	[Test]
	public async Task TheReportTellsTheMapTheFileTheDiskAndTheLiveDataApart()
	{
		var heard = await HeardAfterAsync("@storage");

		await Assert.That(heard.Any(m => m.Contains("map limit") && m.Contains("file length") && m.Contains("allocated on disk")))
			.IsTrue();
		await Assert.That(heard.Any(m => m.Contains("Live data") && m.Contains("before the map is full"))).IsTrue();
		await Assert.That(heard.Any(m => m.Contains("never shrinks the file"))).IsTrue();
		await Assert.That(heard.Any(m => m.Contains("At its peak"))).IsTrue();
	}

	[Test]
	public async Task TheReportCountsReaderSlotsADeadProcessLeftBehind()
	{
		// The session's store also checks on a timer, which can free the slot before this test does; the
		// count the report reads goes up whichever check freed it.
		var store = ((LightningDatabase)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>()).Store;
		var clearedBefore = store.StaleReadersCleared;
		await DeadReader.LeaveAsync(store.Path);
		store.CheckStaleReaders();
		await Assert.That(store.StaleReadersCleared).IsEqualTo(clearedBefore + 1);

		var heard = await HeardAfterAsync("@storage");

		await Assert.That(heard.Any(m => m.Contains("reader slot(s) freed since startup"))).IsTrue();
	}

	[Test]
	public async Task HistoryListsEveryKindWithItsPolicy()
	{
		var heard = await HeardAfterAsync("@storage/history");

		await Assert.That(heard.Any(m => m.Contains("wiki (") && m.Contains("Policy: keep everything"))).IsTrue();
		await Assert.That(heard.Any(m => m.Contains("No archive is configured"))).IsTrue();
	}

	/// <summary>Nothing is configured in the test host, so a pass purges nothing — the default is archival.</summary>
	[Test]
	public async Task PurgeUnderTheDefaultPolicyKeepsEverything()
	{
		var heard = await HeardAfterAsync("@storage/purge");

		await Assert.That(heard.Any(m => m.Contains("wiki: keeps everything; nothing purged."))).IsTrue();
	}
}
