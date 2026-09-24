using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// <c>fun_findable</c> (<c>src/fundb.c:1438-1452</c>) answers <c>Can_Locate(obj, victim)</c>, behind
/// its own gate: <c>See_All(executor) || controls(executor, obj) || controls(executor, victim)</c>,
/// and <c>#-1 PERMISSION DENIED</c> otherwise. That gate used to be supplied incidentally by
/// <c>LocateService</c>, which applied <c>fun_locate</c>'s looker gate to every caller; #1222 takes
/// it out of the service, so the function has to ask for itself.
/// </summary>
/// <remarks>
/// Every case drives a *mortal*: the shared fixtures run as God, who is See_All, so a God-typed
/// findable() clears the gate whatever it is and proves nothing.
/// </remarks>
public class FindableFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private Task<TestIsolationHelpers.TestPlayer> Mortal(string prefix)
		=> TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

	private async Task<DBRef> Thing(string prefix)
		=> await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, prefix);

	private async Task God(string command)
		=> await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	/// <summary>Evaluates <paramref name="expression"/> as <paramref name="who"/> and returns what they were told.</summary>
	private async Task<string> As(TestIsolationHelpers.TestPlayer who, string expression)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(who.DbRef);
		await Parser.CommandParse(who.Handle, ConnectionService, MarkupText.Plain($"think {expression}"));
		return string.Join("\n", WebAppFactoryArg.Notifications.For(who.DbRef).Skip(before));
	}

	/// <summary>A room of its own, so nothing another test leaves lying around is nearby.</summary>
	private async Task<string> Room(string prefix, params object[] occupants)
	{
		var dig = await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName(prefix)}"));
		var room = dig.Message!.ToPlainText().Trim();
		foreach (var occupant in occupants)
		{
			await God($"@teleport/silent {occupant}={room}");
		}

		return room;
	}

	/// <summary>Controlling the object asked about clears the gate, and the answer is the locate.</summary>
	[Test]
	public async ValueTask AboutItselfAMortalIsAnswered()
	{
		var asker = await Mortal("FindableSelf");
		var item = await Thing("FindableSelfItem");
		await Room("FindableSelfRoom", asker.DbRef, item);

		await Assert.That(await As(asker, $"[findable({asker.DbRef},{item})]")).Contains("1");
	}

	/// <summary>
	/// A mortal controlling neither side is refused with <c>#-1 PERMISSION DENIED</c>
	/// (<c>fundb.c:1447-1449</c>) — not with a quiet <c>0</c>, which cannot be told from "cannot find
	/// it" and is what the service gate's failure used to produce.
	/// </summary>
	[Test]
	public async ValueTask AboutObjectsItDoesNotControlAMortalIsRefused()
	{
		var asker = await Mortal("FindableNosy");
		var other = await Mortal("FindableOther");
		var item = await Thing("FindableOtherItem");
		await Room("FindableOtherRoom", other.DbRef, item);

		await Assert.That(await As(asker, $"[findable({other.DbRef},{item})]")).Contains("#-1 PERMISSION DENIED");
	}

	/// <summary>Controlling the victim alone is enough, as it is the second arm of the gate.</summary>
	[Test]
	public async ValueTask ControllingTheVictimAloneClearsTheGate()
	{
		var asker = await Mortal("FindableOwner");
		var other = await Mortal("FindableOwnerOther");
		var item = await Thing("FindableOwnerItem");
		await Room("FindableOwnerRoom", other.DbRef, item);
		await God($"@chown/preserve {item}={asker.DbRef}");

		var answer = await As(asker, $"[findable({other.DbRef},{item})]");

		await Assert.That(answer).DoesNotContain("#-1 PERMISSION DENIED");
		await Assert.That(answer).Contains("1").Because("the item is in the other player's room, so they can locate it");
	}

	/// <summary>See_All is the first arm: a wizard is answered about anyone.</summary>
	[Test]
	public async ValueTask AWizardIsAnsweredAboutAnyone()
	{
		var wizard = await Mortal("FindableWiz");
		var other = await Mortal("FindableWizOther");
		var item = await Thing("FindableWizItem");
		await Room("FindableWizRoom", other.DbRef, item);
		await God($"@set {wizard.DbRef}=WIZARD");

		var answer = await As(wizard, $"[findable({other.DbRef},{item})]");

		await Assert.That(answer).DoesNotContain("#-1 PERMISSION DENIED");
		await Assert.That(answer).Contains("1");
	}
}
