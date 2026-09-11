using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class InputUserCommandResultTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private T Get<T>() where T : notnull => Factory.Services.GetRequiredService<T>();

	[Test]
	[Arguments("nearby", "syntax")]
	[Arguments("nearby", "literal")]
	[Arguments("nearby", "success")]
	[Arguments("location", "syntax")]
	[Arguments("location", "literal")]
	[Arguments("location", "success")]
	[Arguments("zone", "syntax")]
	[Arguments("zone", "literal")]
	[Arguments("zone", "success")]
	[Arguments("personal", "syntax")]
	[Arguments("personal", "literal")]
	[Arguments("personal", "success")]
	[Arguments("global-room", "syntax")]
	[Arguments("global-room", "literal")]
	[Arguments("global-room", "success")]
	[Arguments("global-contents", "syntax")]
	[Arguments("global-contents", "literal")]
	[Arguments("global-contents", "success")]
	[Arguments("unmatched", "syntax")]
	[Arguments("unmatched", "literal")]
	[Arguments("unmatched", "success")]
	public async Task UserCommandTextFailureRetiresCapture(string scope, string mode)
	{
		var mediator = Get<IMediator>();
		var objects = Get<IObjectStore>();
		var connections = Get<IConnectionService>();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, mediator, connections, "InputUserCommand");
		var actor = (await objects.GetObjectNodeAsync(player.DbRef)).Expect<SharpPlayer>();
		var god = (await objects.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		var room = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateRoomCommand("input command room", god)))).Expect<SharpRoom>();
		var actorOrigin = await actor.Location.WithCancellation(default);
		await mediator.Send(new MoveObjectCommand(actor, room, actorOrigin.Object().DBRef, IsSilent: true));
		actor = (await objects.GetObjectNodeAsync(player.DbRef)).Expect<SharpPlayer>();
		var masterId = await mediator.Send(new CreateRoomCommand("input command master", god));
		var master = (await objects.GetObjectNodeAsync(masterId)).Expect<AnySharpObject>();
		using var configuration = TestOptionsOverride.Scope(options => options with
		{
			Database = options.Database with { MasterRoom = (uint)masterId.Number }
		});
		await Assert.That(Get<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Database.MasterRoom)
			.IsEqualTo((uint)masterId.Number)
			.Because("the shared options wrapper must retain its async-flow override instead of a prior test's fixed configuration");
		var zone = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateRoomCommand("input command zone", god)))).Expect<AnySharpObject>();
		if (scope == "zone") await mediator.Send(new SetObjectZoneCommand(room, zone));
		if (scope == "personal") await mediator.Send(new SetObjectZoneCommand(actor, zone));
		AnySharpContainer container = scope is "zone" or "personal" ? zone.AsContainer : scope == "global-contents" ? master.AsContainer : room;
		AnySharpObject host = scope == "location" ? room : scope == "global-room" ? master
				: (await objects.GetObjectNodeAsync(await mediator.Send(new CreateThingCommand("input command host", container, actor, room)))).Expect<AnySharpObject>();
		var suffix = Guid.NewGuid().ToString("N");
		var word = "inputmatch" + suffix;
		var attribute = "CMD_" + suffix;
		var marker = "MATCHED_" + suffix;
		var setup = Factory.CommandParser.FromState(ParserState.RootFor(god.Object.DBRef));
		var noCommand = await host.HasFlag("NO_COMMAND");
		try
		{
			await setup.CommandListParse(MarkupText.Plain($"@set {host.Object().DBRef}=!NO_COMMAND"));
			if (scope != "unmatched")
				await setup.CommandListParse(MarkupText.Plain($"&{attribute} {host.Object().DBRef}=${word} *:think {marker}"));
			var attributes = Get<IAttributeService>();
			await attributes.SetAttributeAsync(actor, actor, "FAIL", MarkupText.Plain(mode switch
			{
				"syntax" => "[",
				"literal" => "#-1 PARSER FAILURE ordinary text",
				_ => "valid"
			}));
			await attributes.SetAttributeAsync(actor, actor, "CALLBACK", MarkupText.Plain($"{word} [ulocal(me/FAIL)]"));
			var parser = Get<IMUSHCodeParser>();
			var sessions = Get<IInputSessionService>();
			await parser.CommandParse(player.Handle, connections, MarkupText.Plain("@input/start me/CALLBACK=Answer:,120"));
			var session = sessions.GetCapturing(player.Handle)!;
			await Assert.That(session).IsNotNull();
			var output = new HttpResponseContext();
			CallState? result;
			using (Get<IHttpOutputCapture>().BeginCapture(host.Object().Key, output))
				result = await sessions.DeliverAsync(parser, session, MarkupText.Plain("reply"));
			TestDiagnostics.WriteLine($"{scope}/{mode}: errors={result?.HadErrors}, output={output.Body}, capture={sessions.GetCapturing(player.Handle)?.Id}");
			await Assert.That(output.Body.ToString().Contains(marker)).IsEqualTo(scope != "unmatched");
			await Assert.That(result?.HadErrors).IsEqualTo(mode == "syntax");
			await Assert.That(sessions.GetCapturing(player.Handle)?.Id).IsEqualTo(mode == "syntax" ? null : session.Id);
		}
		finally
		{
			await setup.CommandListParse(MarkupText.Plain($"@wipe {host.Object().DBRef}/{attribute}"));
			if (noCommand) await setup.CommandListParse(MarkupText.Plain($"@set {host.Object().DBRef}=NO_COMMAND"));
			await connections.Disconnect(player.Handle);
		}
	}
}
