using Mediator;
using NSubstitute;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Services;

namespace SharpMUSH.Tests.Applications;

public class RoomDocumentImporterTests
{
	private readonly TestObjectFactory _objects = new();
	private readonly IMediator _mediator = Substitute.For<IMediator>();
	private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
	private readonly IAttributeService _attributes = Substitute.For<IAttributeService>();
	private readonly IRealityPolicy _reality = Substitute.For<IRealityPolicy>();
	private readonly IWarningService _warnings = Substitute.For<IWarningService>();
	private readonly IEngineCommandInvoker _commands = Substitute.For<IEngineCommandInvoker>();
	private readonly SharpRoom _a;
	private readonly SharpRoom _b;
	private readonly AnySharpObject _actor;

	public RoomDocumentImporterTests()
	{
		_a = _objects.CreateRoom(10, "Gate"); _b = _objects.CreateRoom(20, "Yard");
		_actor = _objects.CreatePlayer(1, "Builder", _a);
		_mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
			ValueTask.FromResult<AnyOptionalSharpObject>(_objects.GetAllRooms().Select(r => new AnySharpObject(r)).Concat(_objects.GetAllObjects()).FirstOrDefault(o => o.Object().DBRef.Number == call.Arg<GetObjectNodeQuery>().DBRef.Number) is { } found ? found : new None()));
		_permissions.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>()).Returns(true);
		_permissions.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>()).Returns(true);
		_reality.CanPerceiveAsync(Arg.Any<DBRef>(), Arg.Any<DBRef>(), Arg.Any<CancellationToken>()).Returns(true);
		var exit = _objects.CreateExit(11, "North", ["n"], _a, _b).Expect<SharpExit>();
		// The reader drops an exit listed under a room it is not in, so every room may list it.
		_mediator.CreateStream(Arg.Any<GetExitsQuery>(), Arg.Any<CancellationToken>()).Returns(_ => new[] { exit }.ToAsyncEnumerable());
		_attributes.GetAttributeAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<string>(), IAttributeService.AttributeMode.Read, false).Returns(new None());
		_attributes.SetAttributeAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<string>(), Arg.Any<MString>()).Returns(new Success());
		_warnings.FindWarningsAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<WarningType>()).Returns(AsyncEnumerable.Empty<ObjectWarning>());
	}

	private RoomTopologyReader Reader => new(_mediator, _permissions, _reality, _attributes, _warnings);
	private RoomDocumentImporter Importer => new(_mediator, _permissions, _reality, _attributes, _commands, Reader);

	private static RoomDocumentDto Document(params RoomDocumentRoomDto[] rooms)
		=> new(RoomDocumentDto.FormatName, RoomDocumentDto.CurrentVersion, RoomTopologyReader.DefaultCoordinates, rooms);

	private void Answer(string command, string result)
		=> _commands.InvokeAsync(command, Arg.Any<DBRef>(), Arg.Any<Dictionary<string, CallState>>(), Arg.Any<IEnumerable<string>?>())
			.Returns(new ValueTask<CallState?>(new CallState(result)));

	[Test]
	public async Task AnUnchangedExportPlansNothing()
	{
		var export = (await Reader.ExportAsync(_actor, [10, 20], RoomTopologyReader.DefaultCoordinates, default)).Expect<RoomDocumentDto>();
		var plan = await Importer.PlanAsync(_actor, export, default);
		await Assert.That(plan.Steps).IsEmpty();
		await Assert.That(plan.Problems).IsEmpty();
		await Assert.That(plan.Notes).IsEmpty();
	}

	[Test]
	public async Task NewRoomsAreDugBeforeTheExitsThatLeadToThem()
	{
		var plan = await Importer.PlanAsync(_actor, Document(
			new("#10", null, "Iron gates.", null, [new("#11", null, null, null), new("door", "Shed;s", "shed", null)]),
			new RoomDocumentRoomDto("shed", "Tool shed", null, [1, 0, 0], null)), default);
		await Assert.That(plan.Problems).IsEmpty();
		await Assert.That(plan.Steps.Select(step => (step.Action, step.Target))).IsEquivalentTo(
			[("dig", "shed"), ("place", "shed"), ("describe", "#10"), ("open", "door")], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task AnExitTheDocumentLeavesOutIsANoteAndIsNotDestroyed()
	{
		var plan = await Importer.PlanAsync(_actor, Document(new RoomDocumentRoomDto("#10", null, null, null, [])), default);
		await Assert.That(plan.Steps).IsEmpty();
		await Assert.That(plan.Notes.Single()).Contains("#11");
	}

	[Test]
	public async Task ARoomTheBuilderDoesNotControlIsAProblem()
	{
		_permissions.Controls(_actor, Arg.Is<AnySharpObject>(o => o.Object().DBRef.Number == 20)).Returns(false);
		var document = Document(new RoomDocumentRoomDto("#20", "Courtyard", null, null, null));
		var plan = await Importer.PlanAsync(_actor, document, default);
		await Assert.That(plan.Problems.Single()).Contains("do not control");
		await Assert.That(await Importer.ApplyAsync(_actor, document, plan.Fingerprint, default) is Error<string>).IsTrue();
		await _commands.DidNotReceiveWithAnyArgs().InvokeAsync(default!, default!, default!);
	}

	[Test]
	public async Task AnExitToARoomTheBuilderCannotSeeIsAProblem()
	{
		_reality.CanPerceiveAsync(_actor.Object().DBRef, _b.Object.DBRef, Arg.Any<CancellationToken>()).Returns(false);
		var plan = await Importer.PlanAsync(_actor, Document(new RoomDocumentRoomDto("#10", null, null, null, [new("door", "Door", "#20", null)])), default);
		await Assert.That(plan.Problems.Single()).Contains("door");
	}

	[Test]
	public async Task ApplyIsRefusedWhenThePlanChanged()
	{
		var document = Document(new RoomDocumentRoomDto("#10", "Old gate", null, null, null));
		var result = await Importer.ApplyAsync(_actor, document, "not the confirmed plan", default);
		await Assert.That(result is Error<string>).IsTrue();
		await _commands.DidNotReceiveWithAnyArgs().InvokeAsync(default!, default!, default!);
	}

	[Test]
	public async Task ApplyDigsThenOpensFromTheNewRoomByItsDbref()
	{
		Answer("@DIG", "#50:1760000000000");
		Answer("@OPEN", "#51:1760000000001");
		var document = Document(new RoomDocumentRoomDto("shed", "Tool shed", null, null, [new("out", "Out;o", "#10", null)]));
		var plan = await Importer.PlanAsync(_actor, document, default);
		var applied = (await Importer.ApplyAsync(_actor, document, plan.Fingerprint, default)).Expect<RoomApplyDto>();
		await Assert.That(applied.Failed).IsNull();
		await Assert.That(applied.Created["shed"]).IsEqualTo("#50");
		await Assert.That(applied.Created["out"]).IsEqualTo("#51");
		await _commands.Received(1).InvokeAsync("@OPEN", _actor.Object().DBRef,
			Arg.Is<Dictionary<string, CallState>>(args => args["0"].Message!.ToPlainText() == "Out;o"
				&& args["1"].Message!.ToPlainText() == "#10" && args["3"].Message!.ToPlainText() == "#50"),
			Arg.Any<IEnumerable<string>?>());
	}

	[Test]
	public async Task ApplyStopsAtTheFirstRefusal()
	{
		Answer("@DIG", "#-1 You do not have enough quota.");
		var document = Document(new RoomDocumentRoomDto("shed", "Tool shed", null, null, [new("out", "Out", "#10", null)]));
		var plan = await Importer.PlanAsync(_actor, document, default);
		var applied = (await Importer.ApplyAsync(_actor, document, plan.Fingerprint, default)).Expect<RoomApplyDto>();
		await Assert.That(applied.Done).IsEmpty();
		await Assert.That(applied.Failed).Contains("quota");
		await _commands.DidNotReceive().InvokeAsync("@OPEN", Arg.Any<DBRef>(), Arg.Any<Dictionary<string, CallState>>(), Arg.Any<IEnumerable<string>?>());
	}
}
