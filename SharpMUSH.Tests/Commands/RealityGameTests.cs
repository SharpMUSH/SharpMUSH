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

public class RealityGameServerFactory : ServerWebAppFactory
{
	protected override bool UseRealNotifications => true;
}

public class RealityGameTests
{
	[ClassDataSource<RealityGameServerFactory>(Shared = SharedType.PerTestSession)]
	public required RealityGameServerFactory Factory { get; init; }
	private T Get<T>() where T : notnull => Factory.Services.GetRequiredService<T>();

	[Test, NotInParallel]
	[Arguments(false)]
	[Arguments(true)]
	public async Task MovementAnnouncementsDoNotRevealAnUnperceivedMover(bool arriving)
	{
		var objects = Get<IObjectStore>();
		var mediator = Get<IMediator>();
		var policy = Get<RealityPolicy>();
		var original = await policy.ConfigurationAsync();
		var actor = (await objects.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var origin = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateRoomCommand("reality origin", actor)))).AsRoom;
		var destination = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateRoomCommand("reality destination", actor)))).AsRoom;
		var mover = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateThingCommand("hidden mover", origin, actor, origin)))).AsThing;
		var observer = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateThingCommand("reality observer", arriving ? destination : origin, actor, origin)))).AsThing;
		try
		{
			await policy.SaveObjectAsync(observer.Object.Id!, ObjectReality.Default(observer.Object.DBRef) with { Receive = ["ghost"] }, default);
			await policy.SaveConfigurationAsync(new(1, true, ["normal", "ghost"]), default);
			var output = new HttpResponseContext();
			using (Get<IHttpOutputCapture>().BeginCapture(observer.Object.Key, output))
			{
				var moved = await Get<IMoveService>().ExecuteMoveAsync(Factory.CommandParser.FromState(ParserState.RootFor(actor.Object.DBRef)), mover, destination, actor.Object.DBRef);
				await Assert.That(moved.IsT0).IsTrue();
			}
			await Assert.That(output.Body.ToString()).IsEmpty();
		}
		finally { await policy.SaveConfigurationAsync(original, default); }
	}

	[Test, NotInParallel]
	[Arguments(false)]
	[Arguments(true)]
	public async Task FollowingNotificationsDoNotRevealAnUnperceivedActor(bool dismiss)
	{
		var objects = Get<IObjectStore>();
		var mediator = Get<IMediator>();
		var policy = Get<RealityPolicy>();
		var original = await policy.ConfigurationAsync();
		var actor = (await objects.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var home = await actor.Location.WithCancellation(default);
		var target = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateThingCommand("reality follower", home, actor, home)))).AsThing;
		try
		{
			await policy.SaveConfigurationAsync(new(1, false, ["normal", "ghost"]), default);
			if (dismiss)
				await Factory.CommandParser.FromState(ParserState.RootFor(target.Object.DBRef)).CommandListParse(MarkupText.Plain($"follow {actor.Object.DBRef}"));
			await policy.SaveObjectAsync(target.Object.Id!, ObjectReality.Default(target.Object.DBRef) with { Receive = ["ghost"] }, default);
			await policy.SaveConfigurationAsync(new(1, true, ["normal", "ghost"]), default);
			var output = new HttpResponseContext();
			using (Get<IHttpOutputCapture>().BeginCapture(target.Object.Key, output))
				await Factory.CommandParser.FromState(ParserState.RootFor(actor.Object.DBRef)).CommandListParse(
					MarkupText.Plain($"{(dismiss ? "dismiss" : "follow")} {target.Object.DBRef}"));
			await Assert.That(output.Body.ToString()).IsEmpty();
		}
		finally { await policy.SaveConfigurationAsync(original, default); }
	}

	[Test, NotInParallel]
	public async Task TelCannotMoveAnObjectIntoAnUnperceivedDestination()
	{
		var objects = Get<IObjectStore>();
		var mediator = Get<IMediator>();
		var policy = Get<RealityPolicy>();
		var original = await policy.ConfigurationAsync();
		var player = (await objects.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var originalProfile = (await policy.ReadObjectAsync(player.Object.DBRef))!;
		var home = await player.Location.WithCancellation(default);
		var destination = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateRoomCommand("tel ghost room", player)))).AsRoom;
		var item = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateThingCommand("tel normal item", home, player, home)))).AsThing;
		try
		{
			await policy.SaveConfigurationAsync(new(1, true, ["normal", "ghost"]), default);
			await policy.SaveObjectAsync(player.Object.Id!, originalProfile with { Receive = ["normal", "ghost"] }, default);
			await policy.SaveObjectAsync(destination.Object.Id!, ObjectReality.Default(destination.Object.DBRef) with { Transmit = ["ghost"] }, default);
			await Factory.FunctionParser.FromState(ParserState.RootFor(player.Object.DBRef)).FunctionParse(
				MarkupText.Plain($"tel({item.Object.DBRef},{destination.Object.DBRef})"));
			var current = (await objects.GetObjectNodeAsync(item.Object.DBRef)).AsThing;
			await Assert.That((await current.Location.WithCancellation(default)).Object().DBRef).IsEqualTo(home.Object().DBRef);
		}
		finally
		{
			await policy.SaveObjectAsync(player.Object.Id!, originalProfile, default);
			await policy.SaveConfigurationAsync(original, default);
		}
	}

	[Test, NotInParallel]
	[Arguments(false, false)]
	[Arguments(true, false)]
	[Arguments(false, true)]
	[Arguments(true, true)]
	public async Task PageTargetResolutionUsesTheRecipientsReceivingDirection(bool byName, bool overrideLock)
	{
		var objects = Get<IObjectStore>();
		var mediator = Get<IMediator>();
		var policy = Get<RealityPolicy>();
		var original = await policy.ConfigurationAsync();
		var player = (await objects.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var home = await player.Location.WithCancellation(default);
		var name = "RealityPager" + Guid.NewGuid().ToString("N")[..12];
		var target = (await objects.GetObjectNodeAsync(await mediator.Send(new CreatePlayerCommand(name, "TestPassword123!", home.Object().DBRef, home.Object().DBRef, 10)))).AsPlayer;
		try
		{
			await policy.SaveConfigurationAsync(new(1, true, ["normal", "ghost"]), default);
			await policy.SaveObjectAsync(target.Object.Id!, ObjectReality.Default(target.Object.DBRef) with { Transmit = ["ghost"] }, default);
			if (overrideLock) await mediator.Send(new SetLockCommand(target.Object, "Interact", "#FALSE", player));
			var output = new HttpResponseContext();
			using (Get<IHttpOutputCapture>().BeginCapture(target.Object.Key, output))
				await Factory.CommandParser.FromState(ParserState.RootFor(player.Object.DBRef)).CommandListParse(
					MarkupText.Plain($"page{(overrideLock ? "/override" : "")} {(byName ? "*" + name : target.Object.DBRef.ToString())}=directional-page-message"));
			await Assert.That(output.Body.ToString()).Contains("directional-page-message");
		}
		finally { await policy.SaveConfigurationAsync(original, default); }
	}

	[Test, NotInParallel]
	[Arguments(false)]
	[Arguments(true)]
	public async Task GiveLocatesRecipientsInTheGiversReceivingDirection(bool byName)
	{
		var objects = Get<IObjectStore>();
		var mediator = Get<IMediator>();
		var policy = Get<RealityPolicy>();
		var original = await policy.ConfigurationAsync();
		var player = (await objects.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var home = await player.Location.WithCancellation(default);
		var name = "RealityReceiver" + Guid.NewGuid().ToString("N")[..12];
		var target = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateThingCommand(name, home, player, home)))).AsThing;
		var item = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateThingCommand("reality gift", player, player, home)))).AsThing;
		try
		{
			await policy.SaveConfigurationAsync(new(1, true, ["normal", "ghost"]), default);
			await policy.SaveObjectAsync(target.Object.Id!, ObjectReality.Default(target.Object.DBRef) with { Receive = ["ghost"] }, default);
			await Factory.CommandParser.FromState(ParserState.RootFor(player.Object.DBRef)).CommandListParse(
				MarkupText.Plain($"give {(byName ? name : target.Object.DBRef.ToString())}={item.Object.DBRef}"));
			var current = (await objects.GetObjectNodeAsync(item.Object.DBRef)).AsThing;
			await Assert.That((await current.Location.WithCancellation(default)).Object().DBRef).IsEqualTo(target.Object.DBRef);
		}
		finally { await policy.SaveConfigurationAsync(original, default); }
	}

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

	[Test, NotInParallel]
	public async Task EmptyRejectsBothHopsBeforeRemovingAnItem()
	{
		var objects = Get<IObjectStore>();
		var mediator = Get<IMediator>();
		var policy = Get<RealityPolicy>();
		var original = await policy.ConfigurationAsync();
		var player = (await objects.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var originalLocation = await player.Location.WithCancellation(default);
		var room = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateRoomCommand("empty target", player)))).AsRoom;
		var name = "empty-bag-" + Guid.NewGuid().ToString("N");
		var bag = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateThingCommand(name, room, player, room)))).AsThing;
		var item = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateThingCommand("item", bag, player, room)))).AsThing;
		await policy.SaveObjectAsync(room.Object.Id!, ObjectReality.Default(room.Object.DBRef) with { Transmit = ["ghost"] }, default);
		await mediator.Send(new MoveObjectCommand(player, room));
		try
		{
			await policy.SaveConfigurationAsync(new(1, true, ["normal", "ghost"]), default);
			await Factory.CommandParser.FromState(ParserState.RootFor(player.Object.DBRef)).CommandListParse(MarkupText.Plain($"empty {name}"));
			var current = (await objects.GetObjectNodeAsync(item.Object.DBRef)).AsThing;
			await Assert.That((await current.Location.WithCancellation(default)).Object().DBRef).IsEqualTo(bag.Object.DBRef);
		}
		finally
		{
			await policy.SaveConfigurationAsync(original, default);
			await mediator.Send(new MoveObjectCommand(player, originalLocation));
		}
	}

	[Test, NotInParallel]
	public async Task CorruptProfileProducesAnAdministrativeError()
	{
		var objects = Get<IObjectStore>();
		var player = (await objects.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var location = await player.Location.WithCancellation(default);
		var target = (await objects.GetObjectNodeAsync(await Get<IMediator>().Send(new CreateThingCommand("corrupt reality", player, player, location)))).AsThing;
		await Get<RealityPolicy>().SaveObjectAsync(target.Object.Id!, ObjectReality.Default(target.Object.DBRef) with { Version = 2 }, default);
		var output = await Factory.CommandParser.FromState(ParserState.RootFor(player.Object.DBRef))
			.CommandListParse(MarkupText.Plain($"@reality/inspect {target.Object.DBRef}"));
		await Assert.That(output!.Message!.ToPlainText()).IsEqualTo("#-1 Invalid object reality data.");
	}

}
