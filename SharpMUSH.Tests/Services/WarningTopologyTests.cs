using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Tests for topology warning checks (exit-oneway, exit-multiple, exit-unlinked)
/// </summary>
[NotInParallel]
public class WarningTopologyTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IWarningService WarningService => WebAppFactoryArg.Services.GetRequiredService<IWarningService>();


	[Test]
	public async Task WarningType_ExitUnlinked_IsDefined()
	{
		var value = (uint)WarningType.ExitUnlinked;
		await Assert.That(value).IsEqualTo(0x10u);
	}

	[Test]
	public async Task WarningType_ExitOneway_IsDefined()
	{
		var value = (uint)WarningType.ExitOneway;
		await Assert.That(value).IsEqualTo(0x1u);
	}

	[Test]
	public async Task WarningType_ExitMultiple_IsDefined()
	{
		var value = (uint)WarningType.ExitMultiple;
		await Assert.That(value).IsEqualTo(0x2u);
	}

	[Test]
	public async Task ParseWarnings_ExitUnlinked_ParsesCorrectly()
	{
		// Act
		var result = WarningTypeHelper.ParseWarnings("exit-unlinked");

		// Assert
		await Assert.That(result).IsEqualTo(WarningType.ExitUnlinked);
		await Assert.That(result.HasFlag(WarningType.ExitUnlinked)).IsTrue();
	}

	[Test]
	public async Task ParseWarnings_ExitOneway_ParsesCorrectly()
	{
		// Act
		var result = WarningTypeHelper.ParseWarnings("exit-oneway");

		// Assert
		await Assert.That(result).IsEqualTo(WarningType.ExitOneway);
		await Assert.That(result.HasFlag(WarningType.ExitOneway)).IsTrue();
	}

	[Test]
	public async Task ParseWarnings_ExitMultiple_ParsesCorrectly()
	{
		// Act
		var result = WarningTypeHelper.ParseWarnings("exit-multiple");

		// Assert
		await Assert.That(result).IsEqualTo(WarningType.ExitMultiple);
		await Assert.That(result.HasFlag(WarningType.ExitMultiple)).IsTrue();
	}

	[Test]
	public async Task WarningType_Normal_IncludesTopologyChecks()
	{
		// Normal should include exit-oneway and exit-multiple
		var normal = WarningType.Normal;

		await Assert.That(normal.HasFlag(WarningType.ExitOneway)).IsTrue();
		await Assert.That(normal.HasFlag(WarningType.ExitMultiple)).IsTrue();
	}

	[Test]
	public async Task UnparseWarnings_TopologyFlags_UnparsesCorrectly()
	{
		// Test unparsing individual topology flags
		var exitUnlinked = WarningTypeHelper.UnparseWarnings(WarningType.ExitUnlinked);
		var exitOneway = WarningTypeHelper.UnparseWarnings(WarningType.ExitOneway);
		var exitMultiple = WarningTypeHelper.UnparseWarnings(WarningType.ExitMultiple);

		await Assert.That(exitUnlinked).Contains("exit-unlinked");
		await Assert.That(exitOneway).Contains("exit-oneway");
		await Assert.That(exitMultiple).Contains("exit-multiple");
	}

	[Test]
	public async Task CheckExitWarnings_UnlinkedExit_DetectsWarning()
	{
		// Create an unlinked exit (no destination specified)
		var exitResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@open WarnUnlinkedExit_48701"));
		var exitDbRef = DBRef.Parse(exitResult.Message!.ToPlainText()!);

		var godNode = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		var exitNode = await Mediator.Send(new GetObjectNodeQuery(exitDbRef));

		await Assert.That(exitNode.IsNone).IsFalse();
		var godObj = godNode.Known;

		await Mediator.Send(new SetObjectWarningsCommand(godObj, WarningType.All));

		var hadWarnings = await WarningService.CheckObjectAsync(godObj, exitNode.Known);

		// An unlinked exit (destination = room #0 with Number=0) should trigger ExitUnlinked
		await Assert.That(hadWarnings).IsTrue();

		await Mediator.Send(new SetObjectWarningsCommand(godObj, WarningType.None));
	}

	[Test]
	public async Task CheckExitWarnings_OnewayExit_DetectsWarning()
	{
		// Create two rooms, then a directed exit from room A → room B (no return)
		var roomAResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@dig WarnRoomA_Oneway_48702"));
		var roomADbRef = DBRef.Parse(roomAResult.Message!.ToPlainText()!);

		var roomBResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@dig WarnRoomB_Oneway_48702"));
		var roomBDbRef = DBRef.Parse(roomBResult.Message!.ToPlainText()!);

		var godNode = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		var roomANode = await Mediator.Send(new GetObjectNodeQuery(roomADbRef));
		var roomBNode = await Mediator.Send(new GetObjectNodeQuery(roomBDbRef));
		var godPlayer = godNode.Known.AsPlayer;

		// Create an exit from room A to room B using Mediator directly (avoids God's location)
		var forwardExitDbRef = await Mediator.Send(new CreateExitCommand(
			"WarnForwardExit_48702", [], roomANode.Known.AsContainer, godPlayer));
		var forwardExitNode = await Mediator.Send(new GetObjectNodeQuery(forwardExitDbRef));
		await Mediator.Send(new LinkExitCommand(forwardExitNode.Known.AsExit, roomBNode.Known.AsContainer));

		// Re-fetch after linking
		var forwardExitFresh = await Mediator.Send(new GetObjectNodeQuery(forwardExitDbRef));
		var godObj = godNode.Known;

		await Mediator.Send(new SetObjectWarningsCommand(godObj, WarningType.All));

		var hadWarnings = await WarningService.CheckObjectAsync(godObj, forwardExitFresh.Known);

		// Forward exit with no return path should trigger exit-oneway
		await Assert.That(hadWarnings).IsTrue();

		await Mediator.Send(new SetObjectWarningsCommand(godObj, WarningType.None));
	}

	[Test]
	public async Task CheckExitWarnings_MultipleReturnExits_DetectsWarning()
	{
		// Create two rooms, a forward exit A→B and two return exits B→A
		var roomAResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@dig WarnRoomA_Multi_48703"));
		var roomADbRef = DBRef.Parse(roomAResult.Message!.ToPlainText()!);

		var roomBResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@dig WarnRoomB_Multi_48703"));
		var roomBDbRef = DBRef.Parse(roomBResult.Message!.ToPlainText()!);

		var godNode = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		var roomANode = await Mediator.Send(new GetObjectNodeQuery(roomADbRef));
		var roomBNode = await Mediator.Send(new GetObjectNodeQuery(roomBDbRef));
		var godPlayer = godNode.Known.AsPlayer;

		// Create forward exit A→B
		var forwardDbRef = await Mediator.Send(new CreateExitCommand("WarnMultiForward_48703", [], roomANode.Known.AsContainer, godPlayer));
		var forwardNode = await Mediator.Send(new GetObjectNodeQuery(forwardDbRef));
		await Mediator.Send(new LinkExitCommand(forwardNode.Known.AsExit, roomBNode.Known.AsContainer));

		// Create two return exits B→A
		var return1DbRef = await Mediator.Send(new CreateExitCommand("WarnMultiReturn1_48703", [], roomBNode.Known.AsContainer, godPlayer));
		var return1Node = await Mediator.Send(new GetObjectNodeQuery(return1DbRef));
		await Mediator.Send(new LinkExitCommand(return1Node.Known.AsExit, roomANode.Known.AsContainer));

		var return2DbRef = await Mediator.Send(new CreateExitCommand("WarnMultiReturn2_48703", [], roomBNode.Known.AsContainer, godPlayer));
		var return2Node = await Mediator.Send(new GetObjectNodeQuery(return2DbRef));
		await Mediator.Send(new LinkExitCommand(return2Node.Known.AsExit, roomANode.Known.AsContainer));

		// Re-fetch forward exit
		var forwardFresh = await Mediator.Send(new GetObjectNodeQuery(forwardDbRef));
		var godObj = godNode.Known;

		await Mediator.Send(new SetObjectWarningsCommand(godObj, WarningType.All));

		var hadWarnings = await WarningService.CheckObjectAsync(godObj, forwardFresh.Known);

		// Multiple return exits (2) should trigger exit-multiple
		await Assert.That(hadWarnings).IsTrue();

		await Mediator.Send(new SetObjectWarningsCommand(godObj, WarningType.None));
	}
}
