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
}
