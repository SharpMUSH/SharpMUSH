using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// A mortal may give an object a new parent only if they control that parent, or the parent is
/// LINK_OK and they pass its Parent lock (PennMUSH <c>do_parent</c>, <c>src/set.c:1462-1467</c>).
/// <c>parent(obj,new)</c> runs the same check, notifies the same text and returns the object's
/// parent as it stands afterwards (<c>fun_parent</c>, <c>src/fundb.c:1617-1640</c>).
/// </summary>
/// <remarks>
/// Expected output observed on PennMUSH 80a1d5b (<c>pennmush/test/runtest.pl</c>, God-owned
/// parents, mortal-owned child):
/// <code>
/// @parent Kid=PUnlocked         -> Permission denied.
/// @parent Kid=PLinkLocked       -> Permission denied.   (LINK_OK, @lock/parent =#FALSE)
/// @parent Kid=PLinkOk           -> Parent changed.
/// think parent(Kid,PUnlocked)   -> Permission denied. / #-1
/// think parent(Kid,PLinkLocked) -> Permission denied. / #-1
/// think parent(Kid,PLinkOk)     -> Parent changed. / #5
/// think parent(Kid,PUnlocked)   -> Permission denied. / #5
/// </code>
/// </remarks>
public class ParentPermissionParityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private Mediator.IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<Mediator.IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	private async Task God(string command)
		=> await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	/// <summary>
	/// A connected mortal alone in a fresh God-owned room, so room-wide messages from other tests
	/// (connects, disconnects) never land among the ones a test reads. It is created there: a mortal
	/// that passed through the default home first could still be handed a disconnect announced there.
	/// </summary>
	private async Task<TestIsolationHelpers.TestPlayer> Mortal(string prefix)
	{
		var dig = await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName(prefix)}"));
		var room = DBRef.Parse(dig.Message.ToPlainText().Trim());
		return await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, room);
	}

	/// <summary>A God-owned thing, optionally LINK_OK and with a Parent lock.</summary>
	private async Task<DBRef> GodParent(string prefix, bool linkOk, string? parentLock = null)
	{
		var parent = await TestIsolationHelpers.CreateTestThingAsync(GodParser, ConnectionService, prefix);
		if (linkOk) await God($"@set {parent}=LINK_OK");
		if (parentLock is not null) await God($"@lock/parent {parent}={parentLock}");
		return parent;
	}

	private async Task<DBRef> MortalChild(TestIsolationHelpers.TestPlayer mortal)
	{
		var created = await GodParser.CommandParse(mortal.Handle, ConnectionService,
			MarkupText.Plain($"@create {TestIsolationHelpers.GenerateUniqueName("ParentKid")}"));
		return DBRef.Parse(created.Message.ToPlainText().Trim());
	}

	private async Task<string> AsMortal(TestIsolationHelpers.TestPlayer mortal, string command)
	{
		var recorder = WebAppFactoryArg.Notifications;
		var before = recorder.CountFor(mortal.DbRef);
		await GodParser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain(command));
		return string.Join("\n", recorder.For(mortal.DbRef).Skip(before));
	}

	private async Task<string> ParentOf(DBRef obj)
	{
		var node = (await Mediator.Send(new GetObjectNodeQuery(obj))).Expect<AnySharpObject>();
		var parent = (await node.Object().Parent.WithCancellation(CancellationToken.None)).Object();
		return parent is null ? "#-1" : $"#{parent.DBRef.Number}";
	}

	[Test]
	public async ValueTask ParentCommandRefusesAnUnlockedParentThatIsNotLinkOk()
	{
		var mortal = await Mortal("ParentCmdDeny");
		var parent = await GodParent("PUnlocked", linkOk: false);
		var kid = await MortalChild(mortal);

		var seen = await AsMortal(mortal, $"@parent {kid}={parent}");

		await Assert.That(seen).IsEqualTo("Permission denied.");
		await Assert.That(await ParentOf(kid)).IsEqualTo("#-1");
	}

	[Test]
	public async ValueTask ParentCommandRefusesALinkOkParentWhoseLockFails()
	{
		var mortal = await Mortal("ParentCmdLocked");
		var parent = await GodParent("PLinkLocked", linkOk: true, parentLock: "#FALSE");
		var kid = await MortalChild(mortal);

		var seen = await AsMortal(mortal, $"@parent {kid}={parent}");

		await Assert.That(seen).IsEqualTo("Permission denied.");
		await Assert.That(await ParentOf(kid)).IsEqualTo("#-1");
	}

	[Test]
	public async ValueTask ParentCommandAcceptsALinkOkParentWhoseLockPasses()
	{
		var mortal = await Mortal("ParentCmdAllow");
		var parent = await GodParent("PLinkOk", linkOk: true);
		var kid = await MortalChild(mortal);

		var seen = await AsMortal(mortal, $"@parent {kid}={parent}");

		await Assert.That(seen).IsEqualTo("Parent changed.");
		await Assert.That(await ParentOf(kid)).IsEqualTo($"#{parent.Number}");
	}

	[Test]
	public async ValueTask ParentFunctionRefusesAnUnlockedParentThatIsNotLinkOk()
	{
		var mortal = await Mortal("ParentFnDeny");
		var parent = await GodParent("PUnlocked", linkOk: false);
		var kid = await MortalChild(mortal);

		var seen = await AsMortal(mortal, $"think parent({kid},{parent})");

		await Assert.That(seen).IsEqualTo("Permission denied.\n#-1");
		await Assert.That(await ParentOf(kid)).IsEqualTo("#-1");
	}

	[Test]
	public async ValueTask ParentFunctionRefusesALinkOkParentWhoseLockFails()
	{
		var mortal = await Mortal("ParentFnLocked");
		var parent = await GodParent("PLinkLocked", linkOk: true, parentLock: "#FALSE");
		var kid = await MortalChild(mortal);

		var seen = await AsMortal(mortal, $"think parent({kid},{parent})");

		await Assert.That(seen).IsEqualTo("Permission denied.\n#-1");
		await Assert.That(await ParentOf(kid)).IsEqualTo("#-1");
	}

	[Test]
	public async ValueTask ParentFunctionAcceptsALinkOkParentAndReturnsTheParentAfterEachAttempt()
	{
		var mortal = await Mortal("ParentFnAllow");
		var linkOk = await GodParent("PLinkOk", linkOk: true);
		var unlocked = await GodParent("PUnlocked", linkOk: false);
		var kid = await MortalChild(mortal);

		var accepted = await AsMortal(mortal, $"think parent({kid},{linkOk})");
		var refused = await AsMortal(mortal, $"think parent({kid},{unlocked})");

		await Assert.That(accepted).IsEqualTo($"Parent changed.\n#{linkOk.Number}");
		await Assert.That(refused).IsEqualTo($"Permission denied.\n#{linkOk.Number}")
			.Because("a refused change returns the parent the object still has");
		await Assert.That(await ParentOf(kid)).IsEqualTo($"#{linkOk.Number}");
	}
}
