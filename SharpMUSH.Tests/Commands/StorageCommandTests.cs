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

		var report = string.Join("\n", heard);
		await Assert.That(report).Contains("limit").And.Contains("live,").And.Contains("reusable");
		await Assert.That(report).Contains("On disk:").And.Contains("Disk free:");
		await Assert.That(report).Contains("Only a compacted copy shrinks it.");
		await Assert.That(report).Contains("at the peak").And.Contains("Next run:");
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

		await Assert.That(heard.Any(m => m.Contains("dead reader(s) cleared since startup"))).IsTrue();
	}

	[Test]
	public async Task HistoryListsEveryKindWithItsPolicy()
	{
		var heard = await HeardAfterAsync("@storage/history");

		var report = string.Join("\n", heard);
		await Assert.That(heard.Any(m => m.Split('\n').Any(line => line.Contains("wiki ") && line.Contains("keep everything")))).IsTrue();
		await Assert.That(report).Contains("No archive:");
	}

	/// <summary>Nothing is configured in the test host, so a pass purges nothing — the default is archival.</summary>
	[Test]
	public async Task PurgeUnderTheDefaultPolicyKeepsEverything()
	{
		var heard = await HeardAfterAsync("@storage/purge");

		await Assert.That(heard.Any(m => m.Contains("wiki: keeps everything; nothing purged."))).IsTrue();
	}
}
