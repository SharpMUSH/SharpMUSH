using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class RealityGameTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private T Get<T>() where T : notnull => Factory.Services.GetRequiredService<T>();

	[Test, NotInParallel]
	public async Task HiddenCandidatesDoNotMatchOrMakeVisibleNamesAmbiguous()
	{
		var objects = Get<IObjectStore>();
		var mediator = Get<IMediator>();
		var policy = Get<RealityPolicy>();
		var originalConfiguration = await policy.ConfigurationAsync();
		var player = (await objects.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var originalLocation = await player.Location.WithCancellation(default);
		var roomRef = await mediator.Send(new CreateRoomCommand("reality room", player));
		var room = (await objects.GetObjectNodeAsync(roomRef)).AsRoom;
		var name = "reality-twin-" + Guid.NewGuid().ToString("N");
		var visible = await mediator.Send(new CreateThingCommand(name, room, player, room));
		var hidden = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateThingCommand(name, room, player, room)))).Known;
		await policy.SaveObjectAsync(hidden.Object().Id!, ObjectReality.Default(hidden.Object().DBRef) with { Transmit = ["ghost"] }, default);
		await mediator.Send(new MoveObjectCommand(player, room));
		try
		{
			await policy.SaveConfigurationAsync(new(1, true, ["normal", "ghost"]), default);
			var parser = Factory.FunctionParserFor(player.Object.DBRef);
			var matched = await parser.FunctionParse(MarkupText.Plain($"locate(%#,{name},n)"));
			await Assert.That(matched!.Message!.ToPlainText()).IsEqualTo($"#{visible.Number}");
			var direct = await parser.FunctionParse(MarkupText.Plain($"locate(%#,{hidden.Object().DBRef},a)"));
			await Assert.That(direct!.Message!.ToPlainText()).StartsWith("#-");
			var output = new HttpResponseContext();
			using (Get<IHttpOutputCapture>().BeginCapture(player.Object.Key, output))
				await Factory.CommandParser.FromState(ParserState.RootFor(player.Object.DBRef)).CommandListParse(MarkupText.Plain($"look {room.Object.DBRef}"));
			await Assert.That(output.Body.ToString().Split(name).Length - 1).IsEqualTo(1);
			await policy.SaveObjectAsync(room.Object.Id!, ObjectReality.Default(room.Object.DBRef) with { Transmit = ["ghost"] }, default);
			await Assert.That(await Get<IMoveService>().CanMoveAsync(player, player, room)).IsFalse();
		}
		finally
		{
			await policy.SaveConfigurationAsync(originalConfiguration, default);
			await mediator.Send(new MoveObjectCommand(player, originalLocation));
		}
	}
	[Test, NotInParallel]
	public async Task GameAdministrationUsesLinkedPlayerCapability()
	{
		var policy = Get<RealityPolicy>();
		var original = await policy.ConfigurationAsync();
		var player = (await Get<IObjectStore>().GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var parser = Factory.CommandParser.FromState(ParserState.RootFor(player.Object.DBRef));
		var name = "layer_" + Guid.NewGuid().ToString("N")[..12];
		try
		{
			var added = await parser.CommandListParse(MarkupText.Plain($"@reality/add {name}"));
			await Assert.That(added!.Message!.ToPlainText()).IsEqualTo("Reality configuration updated.");
			var listed = await parser.CommandListParse(MarkupText.Plain("@reality/list"));
			await Assert.That(listed!.Message!.ToPlainText()).Contains(name);
			var inspected = await parser.CommandListParse(MarkupText.Plain($"@reality/inspect {player.Object.DBRef}"));
			await Assert.That(inspected!.Message!.ToPlainText()).Contains("RX: normal");
		}
		finally { await policy.SaveConfigurationAsync(original, default); }
	}

	[Test, NotInParallel]
	public async Task LayerDescriptionUsesNormalAttributeReadAndEvaluation()
	{
		var policy = Get<RealityPolicy>();
		var original = await policy.ConfigurationAsync();
		var player = (await Get<IObjectStore>().GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var roomRef = await Get<IMediator>().Send(new CreateRoomCommand("layer description", player));
		var room = (await Get<IObjectStore>().GetObjectNodeAsync(roomRef)).Known;
		await Get<IMediator>().Send(new SetAttributeCommand(room.Object().DBRef, ["LAYERDESC"], MarkupText.Plain("layer-secret-[add(1,2)]"), player));
		await policy.SaveObjectAsync(room.Object().Id!, ObjectReality.Default(room.Object().DBRef) with
		{ Descriptions = new() { ["normal"] = "LAYERDESC" } }, default);
		async Task<string> Look()
		{
			var response = new HttpResponseContext();
			using (Get<IHttpOutputCapture>().BeginCapture(player.Object.Key, response))
				await Factory.CommandParser.FromState(ParserState.RootFor(player.Object.DBRef)).CommandListParse(MarkupText.Plain($"look {room.Object().DBRef}"));
			return response.Body.ToString();
		}
		try
		{
			await policy.SaveConfigurationAsync(new(1, true, ["normal"]), default);
			await Assert.That(await Look()).Contains("layer-secret-3");
			var attribute = await Get<IAttributeStore>().GetAttributeAsync(room.Object().DBRef, ["LAYERDESC"]).LastAsync();
			var flag = await Get<IAttributeStore>().GetAttributeFlagAsync("INTERNAL");
			await Get<IMediator>().Send(new SetAttributeFlagCommand(room.Object().DBRef, attribute, flag!));
			await Assert.That(await Look()).DoesNotContain("layer-secret");
		}
		finally { await policy.SaveConfigurationAsync(original, default); }
	}

	[Test, NotInParallel]
	[Arguments(true)]
	[Arguments(false)]
	public async Task ExitAdmissionHonorsRealityAndBasicLocks(bool hiddenDestination)
	{
		var objects = Get<IObjectStore>();
		var mediator = Get<IMediator>();
		var policy = Get<RealityPolicy>();
		var original = await policy.ConfigurationAsync();
		var player = (await objects.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var originalLocation = await player.Location.WithCancellation(default);
		var start = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateRoomCommand("reality start", player)))).AsRoom;
		var destination = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateRoomCommand("reality destination", player)))).AsRoom;
		var exit = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateExitCommand("way", [], start, player)))).AsExit;
		await mediator.Send(new LinkExitCommand(exit, destination));
		if (hiddenDestination) await policy.SaveObjectAsync(destination.Object.Id!, ObjectReality.Default(destination.Object.DBRef) with { Transmit = ["ghost"] }, default);
		else await mediator.Send(new SetLockCommand(exit.Object, "Basic", "#FALSE", player));
		await mediator.Send(new MoveObjectCommand(player, start));
		try
		{
			await policy.SaveConfigurationAsync(new(1, hiddenDestination, ["normal", "ghost"]), default);
			await Factory.CommandParser.FromState(ParserState.RootFor(player.Object.DBRef)).CommandListParse(MarkupText.Plain("goto way"));
			var current = (await objects.GetObjectNodeAsync(player.Object.DBRef)).AsPlayer;
			await Assert.That((await current.Location.WithCancellation(default)).Object().DBRef).IsEqualTo(start.Object.DBRef);
		}
		finally
		{
			await policy.SaveConfigurationAsync(original, default);
			await mediator.Send(new MoveObjectCommand(player, originalLocation));
		}
	}

	[Test, NotInParallel]
	public async Task EmptyDoesNotDiscoverOrMoveHiddenContents()
	{
		var objects = Get<IObjectStore>();
		var mediator = Get<IMediator>();
		var policy = Get<RealityPolicy>();
		var original = await policy.ConfigurationAsync();
		var player = (await objects.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var home = await player.Location.WithCancellation(default);
		var bagName = "reality-bag-" + Guid.NewGuid().ToString("N");
		var bag = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateThingCommand(bagName, player, player, home)))).AsThing;
		var hidden = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateThingCommand("hidden item", bag, player, home)))).AsThing;
		await policy.SaveObjectAsync(hidden.Object.Id!, ObjectReality.Default(hidden.Object.DBRef) with { Transmit = ["ghost"] }, default);
		try
		{
			await policy.SaveConfigurationAsync(new(1, true, ["normal", "ghost"]), default);
			var output = new HttpResponseContext();
			using (Get<IHttpOutputCapture>().BeginCapture(player.Object.Key, output))
				await Factory.CommandParser.FromState(ParserState.RootFor(player.Object.DBRef)).CommandListParse(MarkupText.Plain($"empty {bagName}"));
			var current = (await objects.GetObjectNodeAsync(hidden.Object.DBRef)).AsThing;
			await Assert.That((await current.Location.WithCancellation(default)).Object().DBRef).IsEqualTo(bag.Object.DBRef);
			await Assert.That(output.Body.ToString()).Contains("already empty");
		}
		finally { await policy.SaveConfigurationAsync(original, default); }
	}

	[Test, NotInParallel]
	public async Task DropToDoesNotRevealDestinationOnlyTheObjectCanPerceive()
	{
		var objects = Get<IObjectStore>();
		var mediator = Get<IMediator>();
		var policy = Get<RealityPolicy>();
		var original = await policy.ConfigurationAsync();
		var player = (await objects.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var originalLocation = await player.Location.WithCancellation(default);
		var start = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateRoomCommand("drop start", player)))).AsRoom;
		var destination = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateRoomCommand("secret-drop-destination", player)))).AsRoom;
		await mediator.Send(new LinkRoomCommand(start, destination));
		var name = "reality-drop-" + Guid.NewGuid().ToString("N");
		var item = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateThingCommand(name, player, player, start)))).AsThing;
		await policy.SaveObjectAsync(destination.Object.Id!, ObjectReality.Default(destination.Object.DBRef) with { Transmit = ["ghost"] }, default);
		await policy.SaveObjectAsync(item.Object.Id!, ObjectReality.Default(item.Object.DBRef) with { Receive = ["normal", "ghost"] }, default);
		await mediator.Send(new MoveObjectCommand(player, start));
		try
		{
			await policy.SaveConfigurationAsync(new(1, true, ["normal", "ghost"]), default);
			var output = new HttpResponseContext();
			using (Get<IHttpOutputCapture>().BeginCapture(player.Object.Key, output))
				await Factory.CommandParser.FromState(ParserState.RootFor(player.Object.DBRef)).CommandListParse(MarkupText.Plain($"drop {name}"));
			var current = (await objects.GetObjectNodeAsync(item.Object.DBRef)).AsThing;
			await Assert.That((await current.Location.WithCancellation(default)).Object().DBRef).IsEqualTo(destination.Object.DBRef);
			await Assert.That(output.Body.ToString()).DoesNotContain(destination.Object.Name);
		}
		finally
		{
			await policy.SaveConfigurationAsync(original, default);
			await mediator.Send(new MoveObjectCommand(player, originalLocation));
		}
	}

}
