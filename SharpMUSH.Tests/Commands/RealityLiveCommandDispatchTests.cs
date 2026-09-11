using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class RealityLiveCommandDispatchTests
{
	[ClassDataSource<RealityGameServerFactory>(Shared = SharedType.PerTestSession)]
	public required RealityGameServerFactory Factory { get; init; }
	private T Get<T>() where T : notnull => Factory.Services.GetRequiredService<T>();

	[Test, NotInParallel]
	[Arguments("nearby", true, false)]
	[Arguments("location", true, false)]
	[Arguments("zone", true, false)]
	[Arguments("personal", true, false)]
	[Arguments("global-room", true, false)]
	[Arguments("global-contents", true, false)]
	[Arguments("precedence", true, false)]
	[Arguments("nearby", true, true)]
	[Arguments("location", true, true)]
	[Arguments("zone", true, true)]
	[Arguments("personal", true, true)]
	[Arguments("global-room", true, true)]
	[Arguments("global-contents", true, true)]
	[Arguments("nearby", false, false)]
	[Arguments("location", false, false)]
	[Arguments("zone", false, false)]
	[Arguments("personal", false, false)]
	[Arguments("global-room", false, false)]
	[Arguments("global-contents", false, false)]
	public async Task LiveDispatchOnlyExecutesPerceivedSearchCandidates(string source, bool enabled, bool visible)
	{
		var objects = Get<IObjectStore>();
		var mediator = Get<IMediator>();
		var policy = Get<RealityPolicy>();
		var god = (await objects.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		var suffix = Guid.NewGuid().ToString("N")[..12];
		var room = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateRoomCommand("dispatch room", god)))).Expect<SharpRoom>();
		var actor = (await objects.GetObjectNodeAsync(await mediator.Send(new CreatePlayerCommand("Dispatcher" + suffix, "test-password", room.Object.DBRef, room.Object.DBRef, 20)))).Expect<SharpPlayer>();
		var masterRef = new DBRef((int)Get<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Database.MasterRoom);
		var master = (await objects.GetObjectNodeAsync(masterRef)).Expect<AnySharpObject>();
		var zone = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateRoomCommand("dispatch zone", god)))).Expect<AnySharpObject>();
		if (source == "zone") await mediator.Send(new SetObjectZoneCommand(room, zone));
		if (source == "personal") await mediator.Send(new SetObjectZoneCommand(actor, zone));
		AnySharpContainer container = source is "zone" or "personal" ? zone.AsContainer
			: source == "global-contents" ? master.AsContainer : room;
		AnySharpObject host = source == "location" ? room : source == "global-room" ? master
			: (await objects.GetObjectNodeAsync(await mediator.Send(new CreateThingCommand("dispatch host", container, actor, room)))).Expect<AnySharpObject>();
		var setup = Factory.CommandParser.FromState(ParserState.RootFor(god.Object.DBRef));
		var commandAttribute = "CMD_" + suffix;
		var marker = "RAN_" + suffix.ToUpperInvariant();
		var word = "dispatch" + suffix;
		var originalConfiguration = await policy.ConfigurationAsync();
		var originalProfile = await policy.ReadObjectAsync(host.Object().DBRef);
		var noCommand = await host.HasFlag("NO_COMMAND");
		AnySharpObject? fallback = null;
		try
		{
			await setup.CommandListParse(MarkupText.Plain($"@set {host.Object().DBRef}=!NO_COMMAND"));
			await setup.CommandListParse(MarkupText.Plain($"&{commandAttribute} {host.Object().DBRef}=${word}:think {marker}"));
			if (source == "precedence")
			{
				fallback = (await objects.GetObjectNodeAsync(await mediator.Send(new CreateThingCommand("visible fallback", master.AsContainer, actor, room)))).Expect<AnySharpObject>();
				await setup.CommandListParse(MarkupText.Plain($"@set {fallback.Object().DBRef}=!NO_COMMAND"));
				await setup.CommandListParse(MarkupText.Plain($"&{commandAttribute} {fallback.Object().DBRef}=${word}:think {marker}"));
			}
			await policy.SaveObjectAsync(host.Object().Id!, ObjectReality.Default(host.Object().DBRef) with { Transmit = [visible ? "normal" : "ghost"] }, default);
			await policy.SaveConfigurationAsync(new(1, enabled, ["normal", "ghost"]), default);
			var output = new HttpResponseContext();
			using (Get<IHttpOutputCapture>().BeginCapture((fallback ?? host).Object().Key, output))
				await Factory.CommandParser.FromState(ParserState.RootFor(actor.Object.DBRef)).CommandListParse(MarkupText.Plain(word));
			await Assert.That(output.Body.ToString().Contains(marker)).IsEqualTo(fallback is not null || !enabled || visible);
		}
		finally
		{
			await policy.SaveConfigurationAsync(originalConfiguration, default);
			await policy.SaveObjectAsync(host.Object().Id!, originalProfile ?? ObjectReality.Default(host.Object().DBRef), default);
			await setup.CommandListParse(MarkupText.Plain($"@wipe {host.Object().DBRef}/{commandAttribute}"));
			if (noCommand) await setup.CommandListParse(MarkupText.Plain($"@set {host.Object().DBRef}=NO_COMMAND"));
		}
	}
}
