using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// The bundled multidescer package, installed from the catalogue and driven with typed commands: whole
/// descriptions saved and worn, pieces put together by slot, covered and limited, outfits, /noisy and
/// +shortdesc. Each test installs the package and removes it again. Not in parallel: the commands are global.
/// </summary>
[NotInParallel]
public partial class MultidescerIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();
	private IPackageRegistryService Registry => WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>();

	private async Task<string> God(string command)
	{
		var said = (await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command))).Message.ToPlainText()?.Trim() ?? string.Empty;
		await WebAppFactoryArg.QueueBarrierAsync();
		return said;
	}

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

	/// <summary>What <paramref name="player"/> was told while <paramref name="command"/> ran.</summary>
	private async Task<string> As(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
		return string.Join("\n", Notifications.For(player.DbRef).Skip(before));
	}

	private string Heard(TestIsolationHelpers.TestPlayer player) => string.Join("\n", Notifications.For(player.DbRef));

	/// <summary>The player's description as look shows it.</summary>
	private Task<string> Described(TestIsolationHelpers.TestPlayer player) => Eval($"u(#{player.DbRef.Number}/DESCRIBE)");

	private async Task<DBRef> RoomAsync(string prefix)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		return await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName($"{prefix}Room"), god));
	}

	private Task<TestIsolationHelpers.TestPlayer> Player(string prefix, DBRef room) =>
		TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, room);

	private async Task InstallAsync()
	{
		var controller = new PackagesController(
			Registry,
			WebAppFactoryArg.Services.GetRequiredService<IPackageSourceService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageManifestService>(),
			Installer,
			WebAppFactoryArg.Services.GetRequiredService<IPackageAuthoringService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageOperationRunner>(),
			WebAppFactoryArg.Services.GetRequiredService<PluginUploadStore>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Server.Services.PackageManifestResolver>(),
			WebAppFactoryArg.Services.GetRequiredService<IAuditLog>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Models.Packages.EngineVersion>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IBundledPackageBootstrap>());
		var applied = await controller.Apply(
			new ApplyRequest(BundledPackages.RemoteName, "multidescer", null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because("multidescer must install from the catalogue");
		// AINSTALL is queued after the apply: it adds the commands.
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	private async Task<string> PackageObjectAsync(string reference) =>
		$"#{DBRef.Parse((await Registry.GetPackageObjectsAsync("multidescer")).First(o => o.Ref == reference).Objid).Number}";

	private async Task UninstallAsync() => await WebAppFactoryArg.Services.UninstallWithBridgesAsync("multidescer");

	[Test]
	public async Task WholeDescriptionsAreSavedWornAndTheOriginalKept()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("MdAnn", await RoomAsync("MdWhole"));
			var old = TestIsolationHelpers.GenerateUniqueName("MdOld");
			await God($"&DESCRIBE #{ann.DbRef.Number}={old}");

			// Saving leaves the description alone.
			var winter = TestIsolationHelpers.GenerateUniqueName("MdWinter");
			var saved = await As(ann, $"+desc winter={winter} wool, [add(1,2)] layers");
			await Assert.That(saved).Contains("[DESC] Saved the description 'winter'. +desc winter wears it.");
			await Assert.That(await Described(ann)).IsEqualTo(old);

			// The first wear keeps the description the player had, as original.
			// What is typed is evaluated once, as the player, and shown as it came out.
			var worn = await As(ann, "+desc winter");
			await Assert.That(worn).Contains("Your description is saved as 'original'. +desc original wears it again.")
				.And.Contains("[DESC] You now wear winter.");
			await Assert.That(await Described(ann)).IsEqualTo($"{winter} wool, 3 layers");

			// An escaped bracket stays text: DESCRIBE never evaluates the piece a second time.
			var plain = TestIsolationHelpers.GenerateUniqueName("MdPlain");
			await As(ann, $"+desc/save plain={plain} \\[add(1,2)\\]");
			await As(ann, "+desc plain");
			await Assert.That(await Described(ann)).IsEqualTo($"{plain} [add(1,2)]");

			// /view shows a piece as look would.
			await Assert.That(await As(ann, "+desc/view winter")).Contains($"{winter} wool, 3 layers");

			// A name prefix finds the one piece it starts.
			await As(ann, "+desc orig");
			await Assert.That(await Described(ann)).IsEqualTo(old);

			// Two whole descriptions join with a space, in the order of their slot and then as typed.
			await As(ann, "+desc winter + plain");
			await Assert.That(await Described(ann)).IsEqualTo($"{winter} wool, 3 layers {plain} [add(1,2)]");

			// A name is taken once.
			await Assert.That(await As(ann, "+desc/save winter=again")).Contains("You already have a piece called 'winter'.");
			await Assert.That(await As(ann, "+desc nowhere")).Contains("You have nothing saved called 'nowhere'.");

			// /save with no text keeps the description as it is now, code and all.
			await God($"&DESCRIBE #{ann.DbRef.Number}=Coded [add(2,2)]");
			await As(ann, "+desc/save coded");
			await God($"&DESCRIBE #{ann.DbRef.Number}=gone");
			await As(ann, "+desc coded");
			await Assert.That(await Described(ann)).IsEqualTo("Coded 4");

			// Replacing a worn piece changes the description at once; renaming and deleting.
			await As(ann, "+desc winter");
			await Assert.That(await As(ann, "+desc/replace winter=Thin cotton")).Contains("'winter' has new text, and you are wearing it.");
			await Assert.That(await Described(ann)).IsEqualTo("Thin cotton");
			await Assert.That(await As(ann, "+desc/rename winter=summer")).Contains("'winter' is now 'summer'.");
			await Assert.That(await As(ann, "+desc/delete summer")).Contains("Deleted 'summer', and took it off.");
			await Assert.That(await Described(ann)).IsEqualTo(string.Empty);

			var listed = await As(ann, "+desc");
			await Assert.That(listed).Contains("original").And.Contains("plain").And.DoesNotContain("summer");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task PiecesArePutTogetherBySlotCoveredAndLimited()
	{
		try
		{
			await InstallAsync();
			var bo = await Player("MdBo", await RoomAsync("MdPieces"));
			var build = TestIsolationHelpers.GenerateUniqueName("MdBuild");
			var shirt = TestIsolationHelpers.GenerateUniqueName("MdShirt");
			var coat = TestIsolationHelpers.GenerateUniqueName("MdCoat");
			var boots = TestIsolationHelpers.GenerateUniqueName("MdBoots");
			var sandals = TestIsolationHelpers.GenerateUniqueName("MdSandals");

			await Assert.That(await As(bo, $"+desc/save body/build={build}")).Contains("Saved the body piece 'build'. +wear build wears it.");
			await As(bo, $"+desc/save top/shirt={shirt}");
			await As(bo, $"+desc/save outer/coat={coat}");
			await As(bo, $"+desc/save feet/boots={boots}");
			await As(bo, $"+desc/save feet/sandals={sandals}");
			await Assert.That(await As(bo, "+desc/save cloak/cape=x")).Contains("There is no slot called 'cloak'.");

			// Slot order, whatever order they were named in; another paragraph after a blank line.
			await As(bo, "+desc shirt + build");
			await Assert.That(await Described(bo)).IsEqualTo($"{build}\n\n{shirt}");

			// Outer covers top while it is worn.
			await Assert.That(await As(bo, "+wear coat")).Contains("[DESC] You put on coat.");
			await Assert.That(await Described(bo)).IsEqualTo($"{build}\n\n{coat}");

			// Feet holds one: new sandals take the boots off. Same paragraph: a space.
			await As(bo, "+wear boots");
			await As(bo, "+wear sandals");
			await Assert.That(await Described(bo)).IsEqualTo($"{build}\n\n{coat} {sandals}");

			// An outfit is what is worn now, and comes back whole.
			await Assert.That(await As(bo, "+desc/outfit day")).Contains("Saved the outfit 'day': shirt, build, coat, and sandals.");
			await Assert.That(await As(bo, "+remove coat")).Contains("You took off coat.");
			await Assert.That(await Described(bo)).IsEqualTo($"{build}\n\n{shirt} {sandals}");
			await As(bo, "+remove/all");
			await Assert.That(await Described(bo)).IsEqualTo(string.Empty);
			await As(bo, "+desc day");
			await Assert.That(await Described(bo)).IsEqualTo($"{build}\n\n{coat} {sandals}");

			// A deleted piece comes out of the outfit too.
			await As(bo, "+desc/delete sandals");
			await Assert.That(await Described(bo)).IsEqualTo($"{build}\n\n{coat}");
			await As(bo, "+remove/all");
			await As(bo, "+wear day");
			await Assert.That(await Described(bo)).IsEqualTo($"{build}\n\n{coat}");

			// Only this object writes the wardrobe.
			await Assert.That(await Eval($"hasflag(#{bo.DbRef.Number}/MULTIDESC,wizard)")).IsEqualTo("1");
			await Assert.That(await As(bo, "+desc/slots")).Contains("outer").And.Contains("top").And.Contains("Max").And.Contains("none");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task CodeHandWrittenDescriptionsAndLimitsHold()
	{
		try
		{
			await InstallAsync();
			var ev = await Player("MdEv", await RoomAsync("MdCode"));
			var n = ev.DbRef.Number;

			// A piece saved from the description keeps its code, and each piece is evaluated on its own.
			await God($"&MOOD #{n}=calm");
			await God($"&DESCRIBE #{n}=Mood [v(MOOD)]");
			await As(ev, "+desc/save moody");
			await God($"&DESCRIBE #{n}=add(1,2)");
			await As(ev, "+desc/save calc");
			await Assert.That(await As(ev, "+desc moody + calc")).Contains("Your description is saved as 'original'.");
			await Assert.That(await Described(ev)).IsEqualTo("Mood calm 3");
			await God($"&MOOD #{n}=cross");
			await Assert.That(await Described(ev)).IsEqualTo("Mood cross 3");

			// A description written by hand after that is kept too, and stays on.
			var hand = TestIsolationHelpers.GenerateUniqueName("MdHand");
			await God($"&DESCRIBE #{n}={hand}");
			await Assert.That(await As(ev, "+wear calc")).Contains("Your description is saved as 'kept-");
			await Assert.That(await Described(ev)).IsEqualTo($"{hand} 3");

			// Changes that cannot be undone need the whole name.
			await Assert.That(await As(ev, "+desc/delete kep")).Contains("Type the whole name.");

			// Moved into a slot that holds one, a worn piece takes the other off.
			await As(ev, "+desc/save hair/blond=Blond");
			await As(ev, "+desc/save top/cap=Cap");
			await As(ev, "+desc blond + cap");
			await Assert.That(await Described(ev)).IsEqualTo("Blond\n\nCap");
			await Assert.That(await As(ev, "+desc/replace hair/cap=Red cap")).Contains("'cap' has new text, and you are wearing it.");
			await Assert.That(await Described(ev)).IsEqualTo("Red cap");

			// Moved in behind a newer piece, the moved piece is the one that comes off.
			await As(ev, "+desc/replace top/cap=Cap");
			await As(ev, "+desc cap + blond");
			await Assert.That(await As(ev, "+desc/replace hair/cap=Red cap")).Contains("'cap' has new text, and it came off: hair holds no more.");
			await Assert.That(await Described(ev)).IsEqualTo("Blond");

			// A refusal stops the command: nothing is written after it.
			var refused = await As(ev, "+desc/replace cloak/blond=Gone");
			await Assert.That(refused).Contains("There is no slot called 'cloak'.").And.DoesNotContain("has new text");
			await Assert.That(await Described(ev)).IsEqualTo("Blond");

			// An outfit whose pieces were all deleted is refused.
			await As(ev, "+desc cap");
			await As(ev, "+desc/outfit hats");
			await As(ev, "+desc/delete cap");
			await Assert.That(await As(ev, "+wear hats")).Contains("has no pieces left.");

			// A SAFE character can still delete.
			await God($"@set #{n}=SAFE");
			await Assert.That(await As(ev, "+desc/delete blond")).Contains("Deleted 'blond'.");
			await Assert.That(await As(ev, "+desc")).DoesNotContain("blond");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task NoisyTellsTheRoomAndShortdescIsKept()
	{
		try
		{
			await InstallAsync();
			var room = await RoomAsync("MdNoisy");
			var cy = await Player("MdCy", room);
			var di = await Player("MdDi", room);
			var piece = TestIsolationHelpers.GenerateUniqueName("MdPiece");

			await As(cy, $"+desc/save {piece}=Text");
			await As(cy, $"+desc {piece}");
			await Assert.That(Heard(di)).DoesNotContain(piece);
			await As(cy, $"+desc/noisy {piece}");
			await Assert.That(Heard(di)).Contains($"[DESC] {cy.Name} changes into {piece}.");
			await Assert.That(await As(cy, $"+desc/view/noisy {piece}")).Contains("/noisy goes with a change to what you have on.");

			var shortText = TestIsolationHelpers.GenerateUniqueName("MdShort");
			await Assert.That(await As(cy, $"+shortdesc {shortText} in grey")).Contains($"Your short description is now: {shortText} in grey");
			await Assert.That(await Eval($"get(#{cy.DbRef.Number}/SHORTDESC)")).IsEqualTo($"{shortText} in grey");
			await Assert.That(await As(cy, $"+shortdesc {new string('x', 101)}")).Contains("Keep a short description to 100 characters. That one is 101.");
			await Assert.That(await As(cy, "+shortdesc/clear")).Contains("Your short description is cleared.");
			await Assert.That(await Eval($"hasattr(#{cy.DbRef.Number},SHORTDESC)")).IsEqualTo("0");

			// +help carries the topics.
			await Assert.That(await As(cy, "+help desc")).Contains("keeps more than one description");
			await Assert.That(await As(cy, "+help wear")).Contains("change one piece of your description");
		}
		finally
		{
			await UninstallAsync();
		}
	}
	[Test]
	public async Task PlayersKeepAndChangeTheirOwnSlots()
	{
		try
		{
			await InstallAsync();
			var fi = await Player("MdFi", await RoomAsync("MdSlots"));
			var gil = await Player("MdGil", await RoomAsync("MdSlotsOther"));
			var n = fi.DbRef.Number;

			// The game's list until the first change.
			await Assert.That(await As(fi, "+desc/slots")).Contains("The game's list.");
			await Assert.That(await Eval($"hasattr(#{n},MULTIDESC`SLOT)")).IsEqualTo("0");

			// A game whose list is longer than a character may keep leaves everyone on it.
			var commands = await PackageObjectAsync("multidescer");
			await God($"&DATA`CONFIG`MAX-SLOTS {commands}=5");
			await Assert.That(await As(fi, "+desc/slot face/max=1"))
				.Contains("The game's list has 9 slots, more than the 5 a character may keep, so yours cannot be changed.");
			await Assert.That(await Eval($"hasattr(#{n},MULTIDESC`SLOT)")).IsEqualTo("0");
			await God($"&DATA`CONFIG`MAX-SLOTS {commands}=30");

			// A new slot, after face, in face's paragraph: a copy of the game's list is made first.
			await Assert.That(await As(fi, "+desc/addslot scars=face"))
				.Contains("Added the slot 'scars' after 'face', in the body paragraph.");
			await Assert.That(await Eval($"hasattr(#{n},MULTIDESC`SLOT`OUTER`COVERS)")).IsEqualTo("1");
			await Assert.That(await As(fi, "+desc/slots")).Contains("Your own list.").And.Contains("scars");

			var build = TestIsolationHelpers.GenerateUniqueName("MdBuild");
			var scar = TestIsolationHelpers.GenerateUniqueName("MdScar");
			var eyes = TestIsolationHelpers.GenerateUniqueName("MdEyes");
			await As(fi, $"+desc/save body/build={build}");
			await As(fi, $"+desc/save scars/scar={scar}");
			await As(fi, $"+desc/save face/eyes={eyes}");
			await As(fi, "+desc build + scar + eyes");
			await Assert.That(await Described(fi)).IsEqualTo($"{build} {eyes} {scar}");

			// Moving a slot changes the description at once.
			await Assert.That(await As(fi, "+desc/slot scars/after=body")).Contains("'scars' now comes after 'body'.");
			await Assert.That(await Described(fi)).IsEqualTo($"{build} {scar} {eyes}");

			// A paragraph of its own.
			await Assert.That(await As(fi, "+desc/slot scars/paragraph=none")).Contains("'scars' is a paragraph of its own.");
			await Assert.That(await Described(fi)).IsEqualTo($"{build}\n\n{scar}\n\n{eyes}");
			await As(fi, "+desc/slot scars/paragraph=body");

			// Hides, and a max that takes the oldest off.
			await Assert.That(await As(fi, "+desc/slot scars/hides=face")).Contains("your pieces in 'face' do not show.");
			await Assert.That(await Described(fi)).IsEqualTo($"{build} {scar}");
			await As(fi, "+desc/slot scars/hides=");
			await As(fi, "+desc/save scars/burn=Burn");
			await As(fi, "+wear burn");
			await Assert.That(await As(fi, "+desc/slot scars/max=1")).Contains("'scars' holds one piece at once. Took off scar to fit.");
			await Assert.That(await Described(fi)).IsEqualTo($"{build} Burn {eyes}");

			// Renaming moves its pieces and what hides it.
			await As(fi, "+desc/slot outer/hides=top scars");
			await Assert.That(await As(fi, "+desc/slot scars/name=marks")).Contains("'scars' is now called 'marks', and 2 pieces moved with it.");
			await Assert.That(await Eval($"get(#{n}/MULTIDESC`SLOT`OUTER`COVERS)")).IsEqualTo("top marks");
			await Assert.That(await Described(fi)).IsEqualTo($"{build} Burn {eyes}");

			// Refusals change nothing.
			await Assert.That(await As(fi, "+desc/slot marks/max=200")).Contains("Max is a number from 0 to 100, or none for no limit.");
			await Assert.That(await As(fi, "+desc/slot marks/hides=marks")).Contains("A slot cannot hide itself.");
			await Assert.That(await As(fi, "+desc/slot top/hides=outer")).Contains("Those slots already hide 'top', so it cannot hide them back.");
			await Assert.That(await As(fi, "+desc/slot marks/hides=outer")).Contains("Those slots already hide 'marks', so it cannot hide them back.");
			await Assert.That(await As(fi, "+desc/slot marks/after=nowhere")).Contains("You have no slot called 'nowhere' to put it after.");
			await Assert.That(await As(fi, "+desc/slot whole/name=all")).Contains("The whole description slot keeps its name");
			await Assert.That(await As(fi, "+desc/addslot face")).Contains("You already have a slot called 'face'.");
			await Assert.That(await As(fi, "+desc/addslot bad-name")).Contains("A slot's name is one word of letters, digits and _, starting with a letter.");
			await Assert.That(await As(fi, "+desc/addslot 0=face")).Contains("starting with a letter.");
			await God("+config/add multidescer/slots/0=25");
			await Assert.That(await Eval($"hasattr({await PackageObjectAsync("store")},SLOT`0)")).IsEqualTo("0");
			await Assert.That(await As(fi, "+desc/save 0/scar=Text")).Contains("There is no slot called '0'.");
			var store = await PackageObjectAsync("store");
			await God("+config multidescer/slots/top/covers=outer");
			await Assert.That(await Eval($"get({store}/SLOT`TOP`COVERS)")).IsEqualTo("");
			await Assert.That(await As(fi, "+desc/slot marks/name=none")).Contains("none clears a setting, so it cannot name a slot.");
			await Assert.That(await As(fi, "+desc/slot marks/paragraph=2nd")).Contains("A paragraph's name is one word of letters, digits and _, starting with a letter");
			await Assert.That(await As(fi, "+desc/delslot whole")).Contains("The whole description slot stays");

			// Another player still has the game's list.
			await Assert.That(await As(gil, "+desc/save scars/x=y")).Contains("There is no slot called 'scars'.");

			// A slot taken off the game's list hands what it hid to the slots that hid it.
			await God("+config multidescer/slots/feet/covers=hair");
			await God("+config multidescer/slots/accessory/covers=feet");
			await As(gil, "+desc/save hair/plait=Plait");
			await As(gil, "+desc/save accessory/pin=Pin");
			await As(gil, "+desc plait + pin");
			await Assert.That(await Described(gil)).IsEqualTo("Plait\n\nPin");
			await God("+config/remove multidescer/slots/feet");
			await Assert.That(await Eval($"get({store}/SLOT`ACCESSORY`COVERS)")).IsEqualTo("hair");
			await As(gil, "+desc plait + pin");
			await Assert.That(await Described(gil)).IsEqualTo("Pin");

			// Taken off with nothing in its place, it hides nothing and nothing hides its pieces.
			await God("+config/remove multidescer/slots/hair");
			await Assert.That(await Eval($"hasattr({store}/SLOT`HAIR)")).IsEqualTo("0");
			await Assert.That(await Eval($"get({store}/SLOT`ACCESSORY`COVERS)")).IsEqualTo("");
			await As(gil, "+desc plait + pin");
			await Assert.That(await Described(gil)).IsEqualTo("Pin\n\nPlait");

			// A slot taken away: its pieces stay, and come last.
			await As(fi, "+desc/slot marks/hides=hair");
			await Assert.That(await As(fi, "+desc/delslot marks")).Contains("Took away the slot 'marks'. 'outer' now hides 'hair' in its place. scar and burn stay saved and come last when worn.");
			await Assert.That(await Eval($"get(#{n}/MULTIDESC`SLOT`OUTER`COVERS)")).IsEqualTo("hair top");
			await Assert.That(await Described(fi)).IsEqualTo($"{build} {eyes}\n\nBurn");

			// Back to the game's list.
			await Assert.That(await As(fi, "+desc/resetslots")).Contains("You use the game's slots again. Pieces in 'marks' stay saved and come last when worn.");
			await Assert.That(await Eval($"hasattr(#{n},MULTIDESC`SLOT)")).IsEqualTo("0");
			await Assert.That(await As(fi, "+desc/resetslots")).Contains("You already use the game's slots.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task AWholeDescriptionHoldsASlotsPlace()
	{
		try
		{
			await InstallAsync();
			var ma = await Player("MdMa", await RoomAsync("MdMarks"));
			var eyes = TestIsolationHelpers.GenerateUniqueName("MdEyes");
			var coat = TestIsolationHelpers.GenerateUniqueName("MdCoat");
			await As(ma, "+desc/save tall=A tall woman with <face>, wearing <outer>.");
			await As(ma, $"+desc/save face/eyes={eyes}");
			await As(ma, $"+desc/save outer/coat={coat}");
			await As(ma, "+desc/save hair/braid=Braid");

			// The pieces worn in a marked slot are read in its place, and not again in their own.
			await As(ma, "+desc tall + eyes + coat + braid");
			await Assert.That(await Described(ma)).IsEqualTo($"A tall woman with {eyes}, wearing {coat}.\n\nBraid");

			// A marker with nothing worn in its slot reads as nothing.
			await As(ma, "+remove coat");
			await Assert.That(await Described(ma)).IsEqualTo($"A tall woman with {eyes}, wearing .\n\nBraid");

			// Viewed on its own, a piece keeps its markers.
			await Assert.That(await As(ma, "+desc/view tall")).Contains("A tall woman with <face>, wearing <outer>.");

			// Renaming a slot renames its markers.
			await As(ma, "+desc/slot face/name=features");
			await Assert.That(await Eval($"get(#{ma.DbRef.Number}/MULTIDESC`PIECE`1`TEXT)")).Contains("<features>");
			await Assert.That(await Described(ma)).IsEqualTo($"A tall woman with {eyes}, wearing .\n\nBraid");
		}
		finally
		{
			await UninstallAsync();
		}
	}
}
