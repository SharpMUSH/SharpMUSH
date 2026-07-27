using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class MovementCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	/// <summary>
	/// Creates a fresh test player with a registered connection handle,
	/// so movement commands execute against an isolated player instead of God (#1).
	/// </summary>
	private Task<TestIsolationHelpers.TestPlayer> CreateTestPlayerAsync(string namePrefix) =>
		TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, namePrefix);

	/// <summary>
	/// PennMUSH <c>do_move</c> (<c>move.c:435</c>): when nothing matches as an exit the answer is
	/// "You can't go that way.", not a generic "I don't see that here."
	/// </summary>
	[Test]
	public async ValueTask GotoSomethingThatIsNotAnExitReportsCantGoThatWay()
	{
		var player = await CreateTestPlayerAsync("GotoNonExit");

		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single("goto #0"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(
			NotifyService, nameof(ErrorMessages.Notifications.CantGoThatWay), player.DbRef, player.DbRef)).IsTrue();
	}

	/// <summary>
	/// PennMUSH <c>move.c:449</c> → <c>predicat.c:75</c>: <c>could_doit</c> fails when an exit has no
	/// destination, so <c>do_move</c> falls through to
	/// <c>fail_lock(..., "You can't go that way.")</c>.
	/// </summary>
	[Test]
	public async ValueTask WalkingAnExitWithNoDestinationReportsCantGoThatWay()
	{
		var player = await CreateTestPlayerAsync("NoDestWalker");

		var roomName = TestIsolationHelpers.GenerateUniqueName("NoDestRoom");
		var digResult = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@dig {roomName}"));
		var roomDbRef = digResult.Message!.ToPlainText()!.Trim();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {player.DbRef}={roomDbRef}"));

		var exitName = TestIsolationHelpers.GenerateUniqueName("NoDestExit");
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@open {exitName}"));

		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single(exitName));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(
			NotifyService, nameof(ErrorMessages.Notifications.CantGoThatWay), player.DbRef, player.DbRef)).IsTrue();
	}

	/// <summary>
	/// PennMUSH routes the failure through <c>fail_lock</c>, so the exit's own <c>@fail</c> replaces the
	/// default message rather than being ignored.
	/// </summary>
	[Test]
	public async ValueTask WalkingAnExitWithNoDestinationUsesTheExitsFailureAttribute()
	{
		var player = await CreateTestPlayerAsync("NoDestFailAttr");

		var roomName = TestIsolationHelpers.GenerateUniqueName("FailAttrRoom");
		var digResult = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@dig {roomName}"));
		var roomDbRef = digResult.Message!.ToPlainText()!.Trim();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {player.DbRef}={roomDbRef}"));

		var exitName = TestIsolationHelpers.GenerateUniqueName("FailAttrExit");
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@open {exitName}"));
		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single($"&FAILURE {exitName}=The door is bricked up."));

		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single(exitName));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(player.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessagePlainTextContains(msg, "The door is bricked up.")),
				TestHelpers.MatchingObject(player.DbRef), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// <c>@unlink</c> removes the destination edge entirely. Walking the exit afterwards must report the
	/// same PennMUSH failure rather than throwing out of the database layer.
	/// </summary>
	[Test]
	public async ValueTask WalkingAnUnlinkedExitReportsCantGoThatWay()
	{
		var player = await CreateTestPlayerAsync("UnlinkWalker");

		var sourceName = TestIsolationHelpers.GenerateUniqueName("UnlinkSource");
		var sourceResult = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@dig {sourceName}"));
		var sourceDbRef = sourceResult.Message!.ToPlainText()!.Trim();

		var destName = TestIsolationHelpers.GenerateUniqueName("UnlinkDest");
		var destResult = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@dig {destName}"));
		var destDbRef = destResult.Message!.ToPlainText()!.Trim();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {player.DbRef}={sourceDbRef}"));

		var exitName = TestIsolationHelpers.GenerateUniqueName("UnlinkExitWalk");
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@open {exitName}={destDbRef}"));
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@unlink {exitName}"));

		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single(exitName));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(
			NotifyService, nameof(ErrorMessages.Notifications.CantGoThatWay), player.DbRef, player.DbRef)).IsTrue();
	}

	/// <summary>
	/// A linked exit still has to work: this pins the destination edge the other three tests depend on.
	/// </summary>
	[Test]
	public async ValueTask WalkingALinkedExitMovesThePlayerToTheDestination()
	{
		var player = await CreateTestPlayerAsync("LinkedWalker");

		var sourceName = TestIsolationHelpers.GenerateUniqueName("LinkedSource");
		var sourceResult = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@dig {sourceName}"));
		var sourceDbRef = sourceResult.Message!.ToPlainText()!.Trim();

		var destName = TestIsolationHelpers.GenerateUniqueName("LinkedDest");
		var destResult = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@dig {destName}"));
		var destDbRef = destResult.Message!.ToPlainText()!.Trim();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {player.DbRef}={sourceDbRef}"));

		var exitName = TestIsolationHelpers.GenerateUniqueName("LinkedExitWalk");
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@open {exitName}={destDbRef}"));

		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single(exitName));

		var location = await Mediator.Send(new GetLocationQuery(player.DbRef));

		await Assert.That(location.WithExitOption().Known().Object().DBRef.ToString()).IsEqualTo(destDbRef);
	}

	[Test]
	public async ValueTask TeleportPreventsLoops()
	{
		var boxName = TestIsolationHelpers.GenerateUniqueName("TelBox");
		var itemName = TestIsolationHelpers.GenerateUniqueName("TelItem");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {boxName}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {itemName}"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {boxName}=ENTER_OK"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"get {boxName}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"get {itemName}"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"give {boxName}={itemName}"));

		var result = await Parser.CommandParse(1, ConnectionService, MModule.single($"@teleport {boxName}={itemName}"));

		await Assert.That(result).IsNotNull();
	}

	/// <summary>
	/// Tests @tel with a single argument (teleports the executor to a room by dbref).
	/// Usage: @tel destination
	/// </summary>
	[Test]
	public async ValueTask TeleportSelfToRoom()
	{
		var player = await CreateTestPlayerAsync("TeleportSelf");

		var roomName = TestIsolationHelpers.GenerateUniqueName("TelSelfRoom");
		var digResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@dig {roomName}"));
		var roomDbRef = digResult.Message!.ToPlainText()!.Trim();

		await Assert.That(roomDbRef).StartsWith("#");

		var telResult = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@tel {roomDbRef}"));

		await Assert.That(telResult).IsNotNull();
	}

	/// <summary>
	/// Tests @tel with two arguments (teleports an object to a destination by dbref).
	/// Usage: @tel object=destination
	/// </summary>
	[Test]
	public async ValueTask TeleportObjectToRoom()
	{
		var objName = TestIsolationHelpers.GenerateUniqueName("TelObj");
		var roomName = TestIsolationHelpers.GenerateUniqueName("TelObjDest");

		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {objName}"));
		var objDbRef = createResult.Message!.ToPlainText()!.Trim();

		var digResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@dig {roomName}"));
		var roomDbRef = digResult.Message!.ToPlainText()!.Trim();

		await Assert.That(objDbRef).StartsWith("#");
		await Assert.That(roomDbRef).StartsWith("#");

		var telResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {objDbRef}={roomDbRef}"));

		await Assert.That(telResult).IsNotNull();
	}

	/// <summary>
	/// Tests @tel with object names instead of dbrefs.
	/// Usage: @tel objectname=destinationname
	/// </summary>
	[Test]
	public async ValueTask TeleportByName()
	{
		var objName = TestIsolationHelpers.GenerateUniqueName("TelByNObj");
		var roomName = TestIsolationHelpers.GenerateUniqueName("TelByNRoom");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {objName}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@dig {roomName}"));

		// Teleport using names (this exercises the name-based locate path)
		var telResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {objName}={roomName}"));

		await Assert.That(telResult).IsNotNull();
	}

	/// <summary>
	/// Tests @tel to an invalid destination.
	/// Should produce an error notification.
	/// </summary>
	[Test]
	public async ValueTask TeleportToInvalidDestination()
	{
		var result = await Parser.CommandParse(1, ConnectionService,
			MModule.single("@tel #99999"));

		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask HomeCommand()
	{
		var player = await CreateTestPlayerAsync("HomeCmd");

		var roomName = TestIsolationHelpers.GenerateUniqueName("HomeRoom");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@dig {roomName}"));

		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@link me={roomName}"));

		var result = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single("home"));

		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask HomeCommandAlreadyHome()
	{
		var player = await CreateTestPlayerAsync("HomeAlready");

		var result = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single("home"));

		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask EnterCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("enter #1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "You can't enter that.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask LeaveCommand()
	{
		var player = await CreateTestPlayerAsync("LeaveCmd");

		var boxName = TestIsolationHelpers.GenerateUniqueName("LeaveBox");
		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {boxName}"));
		var boxDbRef = createResult.Message!.ToPlainText()!.Trim();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {boxName}=ENTER_OK"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {player.DbRef}={boxDbRef}"));

		var result = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single("leave"));

		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask LeaveCommandInRoom()
	{
		var player = await CreateTestPlayerAsync("LeaveRoom");

		var result = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single("leave"));

		await Assert.That(result).IsNotNull();
	}

	/// <summary>
	/// PennMUSH variable exits: <c>@link &lt;exit&gt;=variable</c> leaves the destination unresolved, and
	/// <c>find_var_dest</c> (<c>move.c:360</c>) reads it from the exit's <c>DESTINATION</c> attribute at
	/// move time.
	/// </summary>
	[Test]
	public async ValueTask VariableExitMovesToTheDestinationAttribute()
	{
		var player = await CreateTestPlayerAsync("VarDestPlain");

		var sourceName = TestIsolationHelpers.GenerateUniqueName("VarDestSource");
		var sourceResult = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@dig {sourceName}"));
		var sourceDbRef = sourceResult.Message!.ToPlainText()!.Trim();

		var destName = TestIsolationHelpers.GenerateUniqueName("VarDestTarget");
		var destResult = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@dig {destName}"));
		var destDbRef = destResult.Message!.ToPlainText()!.Trim();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {player.DbRef}={sourceDbRef}"));

		var exitName = TestIsolationHelpers.GenerateUniqueName("VarDestExit");
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@open {exitName}"));
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@link {exitName}=variable"));
		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single($"&DESTINATION {exitName}={destDbRef}"));

		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single(exitName));

		var location = await Mediator.Send(new GetLocationQuery(player.DbRef));

		await Assert.That(location.WithExitOption().Known().Object().DBRef.ToString()).IsEqualTo(destDbRef);
	}

	/// <summary>
	/// PennMUSH <c>find_var_dest</c> uses <c>call_attrib</c>, so <c>DESTINATION</c> is softcode that is
	/// evaluated — that is the whole point of a variable exit.
	/// </summary>
	[Test]
	public async ValueTask VariableExitEvaluatesTheDestinationAttribute()
	{
		var player = await CreateTestPlayerAsync("VarDestEval");

		var sourceName = TestIsolationHelpers.GenerateUniqueName("VarEvalSource");
		var sourceResult = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@dig {sourceName}"));
		var sourceDbRef = sourceResult.Message!.ToPlainText()!.Trim();

		var destName = TestIsolationHelpers.GenerateUniqueName("VarEvalTarget");
		var destResult = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@dig {destName}"));
		var destDbRef = destResult.Message!.ToPlainText()!.Trim();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {player.DbRef}={sourceDbRef}"));

		var exitName = TestIsolationHelpers.GenerateUniqueName("VarEvalExit");
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@open {exitName}"));
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@link {exitName}=variable"));
		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single($"&DESTINATION {exitName}=[first({destDbRef} {sourceDbRef})]"));

		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single(exitName));

		var location = await Mediator.Send(new GetLocationQuery(player.DbRef));

		await Assert.That(location.WithExitOption().Known().Object().DBRef.ToString()).IsEqualTo(destDbRef);
	}

	/// <summary>
	/// PennMUSH <c>move.c:377</c> falls back to <c>EXITTO</c> when <c>DESTINATION</c> is absent, "for
	/// portability".
	/// </summary>
	[Test]
	public async ValueTask VariableExitFallsBackToTheExitToAttribute()
	{
		var player = await CreateTestPlayerAsync("VarDestExitTo");

		var sourceName = TestIsolationHelpers.GenerateUniqueName("VarExitToSource");
		var sourceResult = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@dig {sourceName}"));
		var sourceDbRef = sourceResult.Message!.ToPlainText()!.Trim();

		var destName = TestIsolationHelpers.GenerateUniqueName("VarExitToTarget");
		var destResult = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@dig {destName}"));
		var destDbRef = destResult.Message!.ToPlainText()!.Trim();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {player.DbRef}={sourceDbRef}"));

		var exitName = TestIsolationHelpers.GenerateUniqueName("VarExitToExit");
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@open {exitName}"));
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@link {exitName}=variable"));
		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single($"&EXITTO {exitName}={destDbRef}"));

		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single(exitName));

		var location = await Mediator.Send(new GetLocationQuery(player.DbRef));

		await Assert.That(location.WithExitOption().Known().Object().DBRef.ToString()).IsEqualTo(destDbRef);
	}

	/// <summary>
	/// PennMUSH <c>move.c:369</c> passes the exit name or alias the player actually typed as <c>%0</c>,
	/// so one exit can route differently per alias.
	/// </summary>
	[Test]
	public async ValueTask VariableExitPassesTheUsedExitNameAsPercentZero()
	{
		var player = await CreateTestPlayerAsync("VarDestArg");

		var sourceName = TestIsolationHelpers.GenerateUniqueName("VarArgSource");
		var sourceResult = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@dig {sourceName}"));
		var sourceDbRef = sourceResult.Message!.ToPlainText()!.Trim();

		var northName = TestIsolationHelpers.GenerateUniqueName("VarArgNorth");
		var northResult = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@dig {northName}"));
		var northDbRef = northResult.Message!.ToPlainText()!.Trim();

		var southName = TestIsolationHelpers.GenerateUniqueName("VarArgSouth");
		var southResult = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@dig {southName}"));
		var southDbRef = southResult.Message!.ToPlainText()!.Trim();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {player.DbRef}={sourceDbRef}"));

		var exitName = TestIsolationHelpers.GenerateUniqueName("VarArgExit");
		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single($"@open {exitName};{exitName}North;{exitName}South"));
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@link {exitName}=variable"));
		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single($"&DESTINATION {exitName}=[switch(%0,*South,{southDbRef},{northDbRef})]"));

		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"{exitName}South"));

		var location = await Mediator.Send(new GetLocationQuery(player.DbRef));

		await Assert.That(location.WithExitOption().Known().Object().DBRef.ToString()).IsEqualTo(southDbRef);
	}

	/// <summary>
	/// PennMUSH <c>do_move</c> (<c>move.c:451</c>): an exit linked to HOME sends the mover to their own
	/// home, not to a fixed room.
	/// </summary>
	[Test]
	public async ValueTask HomeLinkedExitSendsTheMoverToTheirOwnHome()
	{
		var player = await CreateTestPlayerAsync("HomeLinkWalker");

		var homeName = TestIsolationHelpers.GenerateUniqueName("HomeLinkHome");
		var homeResult = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@dig {homeName}"));
		var homeDbRef = homeResult.Message!.ToPlainText()!.Trim();
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@link me={homeDbRef}"));

		var sourceName = TestIsolationHelpers.GenerateUniqueName("HomeLinkSource");
		var sourceResult = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@dig {sourceName}"));
		var sourceDbRef = sourceResult.Message!.ToPlainText()!.Trim();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {player.DbRef}={sourceDbRef}"));

		var exitName = TestIsolationHelpers.GenerateUniqueName("HomeLinkExit");
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@open {exitName}"));
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@link {exitName}=home"));

		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single(exitName));

		var location = await Mediator.Send(new GetLocationQuery(player.DbRef));

		await Assert.That(location.WithExitOption().Known().Object().DBRef.ToString()).IsEqualTo(homeDbRef);
	}

	/// <summary>
	/// PennMUSH <c>move.c:459</c> names the offending dbref rather than giving the generic exit failure.
	/// </summary>
	[Test]
	public async ValueTask VariableExitWithNoDestinationAttributeReportsAnInvalidDestination()
	{
		var player = await CreateTestPlayerAsync("VarDestMissing");

		var sourceName = TestIsolationHelpers.GenerateUniqueName("VarMissingSource");
		var sourceResult = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@dig {sourceName}"));
		var sourceDbRef = sourceResult.Message!.ToPlainText()!.Trim();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {player.DbRef}={sourceDbRef}"));

		var exitName = TestIsolationHelpers.GenerateUniqueName("VarMissingExit");
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@open {exitName}"));
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@link {exitName}=variable"));

		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single(exitName));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(
			NotifyService, nameof(ErrorMessages.Notifications.VariableExitDestinationInvalidFormat),
			player.DbRef, player.DbRef)).IsTrue();
	}

	/// <summary>
	/// SharpMUSH lets <c>@teleport</c> target an exit, meaning "go where it leads". That has to agree with
	/// what walking the exit does, so a variable exit must resolve its DESTINATION here too.
	/// </summary>
	[Test]
	public async ValueTask TeleportToAVariableExitUsesTheDestinationAttribute()
	{
		var player = await CreateTestPlayerAsync("TelVarDest");

		var sourceName = TestIsolationHelpers.GenerateUniqueName("TelVarSource");
		var sourceResult = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@dig {sourceName}"));
		var sourceDbRef = sourceResult.Message!.ToPlainText()!.Trim();

		var destName = TestIsolationHelpers.GenerateUniqueName("TelVarTarget");
		var destResult = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@dig {destName}"));
		var destDbRef = destResult.Message!.ToPlainText()!.Trim();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {player.DbRef}={sourceDbRef}"));

		var exitName = TestIsolationHelpers.GenerateUniqueName("TelVarExit");
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@open {exitName}"));
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@link {exitName}=variable"));
		await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single($"&DESTINATION {exitName}={destDbRef}"));

		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@tel {exitName}"));

		var location = await Mediator.Send(new GetLocationQuery(player.DbRef));

		await Assert.That(location.WithExitOption().Known().Object().DBRef.ToString()).IsEqualTo(destDbRef);
	}

	/// <summary>
	/// A home-linked exit resolves against whoever is moving, not against whoever typed the command — so
	/// teleporting someone else through one must send them to <em>their</em> home.
	/// </summary>
	[Test]
	public async ValueTask TeleportToAHomeLinkedExitSendsTheTargetToTheirOwnHome()
	{
		var player = await CreateTestPlayerAsync("TelHomeTarget");

		var homeName = TestIsolationHelpers.GenerateUniqueName("TelHomeTargetHome");
		var homeResult = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@dig {homeName}"));
		var homeDbRef = homeResult.Message!.ToPlainText()!.Trim();
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@link me={homeDbRef}"));

		// Opened where God stands, so God can see it to teleport the player through it.
		var exitName = TestIsolationHelpers.GenerateUniqueName("TelHomeExit");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@open {exitName}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@link {exitName}=home"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {player.DbRef}={exitName}"));

		var location = await Mediator.Send(new GetLocationQuery(player.DbRef));

		await Assert.That(location.WithExitOption().Known().Object().DBRef.ToString()).IsEqualTo(homeDbRef)
			.Because("the exit is linked to home, and the mover is the player, not the executor");
	}
}
