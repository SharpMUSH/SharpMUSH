using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using Kept = SharpMUSH.Client.Services.SceneDraftSync.Kept;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// Which draft the live scene page opens with, given this browser's copy and the server's: the server's when another
/// device wrote after this one last agreed with it, else this browser's, pushed up when it was written since.
/// </summary>
public class SceneDraftSyncChooseTests
{
	[TUnit.Core.Test]
	public async Task NoCopyAnywhere_ShowsNothing()
	{
		var (show, push) = SceneDraftSync.Choose(null, null);

		await Assert.That(show).IsNull();
		await Assert.That(push).IsFalse();
	}

	[TUnit.Core.Test]
	public async Task OnlyTheServersCopy_IsShown()
	{
		var (show, push) = SceneDraftSync.Choose(null, new SceneDraftCopy("ooc", "From the phone.", null, 200));

		await Assert.That(show).IsEqualTo(new Kept("ooc", "From the phone.", null, 200, false));
		await Assert.That(push).IsFalse();
	}

	[TUnit.Core.Test]
	public async Task AnotherDeviceWroteSince_TheServersCopyWins()
	{
		var local = new Kept("emit", "Typed here.", null, 100, true);

		var (show, push) = SceneDraftSync.Choose(local, new SceneDraftCopy("emit", "Typed there.", null, 200));

		await Assert.That(show!.Text).IsEqualTo("Typed there.");
		await Assert.That(push).IsFalse();
	}

	/// <summary>A pose sent from another device leaves an empty draft that is newer, so this one empties too.</summary>
	[TUnit.Core.Test]
	public async Task APoseSentElsewhere_EmptiesThisCopy()
	{
		var local = new Kept("emit", "Already sent.", null, 100, false);

		var (show, _) = SceneDraftSync.Choose(local, new SceneDraftCopy("emit", "", null, 300));

		await Assert.That(show!.Text).IsEmpty();
	}

	[TUnit.Core.Test]
	public async Task WrittenHereSinceTheLastSync_IsKeptAndPushed()
	{
		var local = new Kept("emit", "Typed offline.", null, 200, true);

		var (show, push) = SceneDraftSync.Choose(local, new SceneDraftCopy("emit", "Older.", null, 200));

		await Assert.That(show).IsEqualTo(local);
		await Assert.That(push).IsTrue();
	}

	[TUnit.Core.Test]
	public async Task ServerUnreachable_ADirtyCopyIsStillPushedLater()
	{
		var local = new Kept("emit", "Typed offline.", null, 0, true);

		var (show, push) = SceneDraftSync.Choose(local, null);

		await Assert.That(show).IsEqualTo(local);
		await Assert.That(push).IsTrue();
	}
}
