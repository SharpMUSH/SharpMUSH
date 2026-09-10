using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class RealityContentsProjectionTests
{
	[ClassDataSource<RealityGameServerFactory>(Shared = SharedType.PerTestSession)]
	public required RealityGameServerFactory Factory { get; init; }
	private T Get<T>() where T : notnull => Factory.Services.GetRequiredService<T>();

	[Test, NotInParallel]
	[Arguments("examine", true)]
	[Arguments("examine-format", true)]
	[Arguments("examine-exits", true)]
	[Arguments("brief", true)]
	[Arguments("inventory", true)]
	[Arguments("whisper/list", true)]
	[Arguments("@sweep/here", true)]
	[Arguments("@sweep/exits", true)]
	[Arguments("@sweep/inventory", true)]
	[Arguments("@scan/room", true)]
	[Arguments("examine", false)]
	[Arguments("brief", false)]
	[Arguments("inventory", false)]
	[Arguments("whisper/list", false)]
	[Arguments("@sweep/here", false)]
	[Arguments("@sweep/exits", false)]
	[Arguments("@sweep/inventory", false)]
	[Arguments("@scan/room", false)]
	public async Task ContentsReportsHideImperceptibleObjectsEvenFromTheirController(string projection, bool enabled)
	{
		var objects = Get<IObjectStore>();
		var mediator = Get<IMediator>();
		var policy = Get<RealityPolicy>();
		var original = await policy.ConfigurationAsync();
		var actor = (await objects.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var home = await actor.Location.WithCancellation(default);
		var room = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateRoomCommand("projection room", actor)))).AsRoom;
		var suffix = Guid.NewGuid().ToString("N")[..12];
		var visibleName = "Visible" + suffix;
		var hiddenName = "Hidden" + suffix;
		var inInventory = projection is "inventory" or "@sweep/inventory";
		AnySharpContainer container = inInventory ? actor : room;
		async Task<DBRef> Create(string name)
		{
			if (projection == "whisper/list")
				return await mediator.Send(new CreatePlayerCommand(name, "test-password", room.Object.DBRef, room.Object.DBRef, 20));
			if (projection is "examine-exits" or "@sweep/exits")
				return await mediator.Send(new CreateExitCommand(name, [], room, actor));
			return await mediator.Send(new CreateThingCommand(name, container, actor, room));
		}
		var visible = (await objects.GetObjectNodeAsync(await Create(visibleName))).Known;
		var hidden = (await objects.GetObjectNodeAsync(await Create(hiddenName))).Known;
		await policy.SaveObjectAsync(hidden.Object().Id!, ObjectReality.Default(hidden.Object().DBRef) with { Transmit = ["ghost"] }, default);
		if (projection == "@sweep/exits")
		{
			var audible = (await mediator.Send(new GetObjectFlagQuery("AUDIBLE")))!;
			await mediator.Send(new SetObjectFlagCommand(room, audible));
			await mediator.Send(new SetObjectFlagCommand(visible, audible));
			await mediator.Send(new SetObjectFlagCommand(hidden, audible));
		}
		var match = "projection" + suffix;
		if (projection.StartsWith("@sweep") || projection.StartsWith("@scan"))
		{
			var setup = Factory.CommandParser.FromState(ParserState.RootFor(actor.Object.DBRef));
			foreach (var target in new[] { visible, hidden })
			{
				await setup.CommandListParse(MarkupText.Plain($"@set {target.Object().DBRef}=!NO_COMMAND"));
				await setup.CommandListParse(MarkupText.Plain($"&CMD {target.Object().DBRef}=${match}:@pemit me=matched"));
			}
		}
		if (projection == "examine-format")
			await mediator.Send(new SetAttributeCommand(room.Object.DBRef, ["CONFORMAT"], MarkupText.Plain("strcat(%0,|,%1)"), actor));
		await mediator.Send(new MoveObjectCommand(actor, room));
		try
		{
			await policy.SaveConfigurationAsync(new(1, enabled, ["normal", "ghost"]), default);
			var command = projection.StartsWith("examine") ? $"examine {room.Object.DBRef}"
				: projection == "brief" ? $"brief {room.Object.DBRef}"
				: projection == "@scan/room" ? $"@scan/room {match}" : projection;
			var output = new HttpResponseContext();
			using (Get<IHttpOutputCapture>().BeginCapture(actor.Object.Key, output))
				await Factory.CommandParser.FromState(ParserState.RootFor(actor.Object.DBRef)).CommandListParse(MarkupText.Plain(command));
			var text = output.Body.ToString();
			await Assert.That(text.Contains(visibleName)).IsTrue();
			await Assert.That(text.Contains(hiddenName)).IsEqualTo(!enabled);
			if (projection == "examine-format")
				await Assert.That(text.Contains(hidden.Object().DBRef.ToString())).IsFalse();
		}
		finally
		{
			await policy.SaveConfigurationAsync(original, default);
			await mediator.Send(new MoveObjectCommand(actor, home));
		}
	}
}
