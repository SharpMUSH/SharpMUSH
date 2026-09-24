using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// <c>@oemit &lt;room&gt;/&lt;list&gt;</c> named from outside that room. <c>do_oemit_list</c> matches
/// the list with <c>match_result_relative(executor, room, p, NOTYPE, MAT_OBJ_CONTENTS)</c>
/// (<c>speech.c:287</c>) and asks nothing of the executor beyond the room's Speech_Lock
/// (<c>speech.c:270-273</c>), which SharpMUSH already asks as <c>MayEmitInAsync</c>.
/// </summary>
/// <remarks>
/// LocateService used to refuse that relative match when the executor was neither near the room nor
/// controlling it, and the caller reads a refusal as "no such recipient" and carries on — so the
/// person named was silently emitted to instead of omitted. #1222 takes the gate out of the service.
/// A mortal drives it: God is See_All and cleared the gate whatever it was.
/// </remarks>
public class OemitRemoteExecutorTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();
	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private readonly List<long> _handles = [];

	private async Task<TestIsolationHelpers.TestPlayer> Player(string prefix)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, prefix);
		_handles.Add(player.Handle);
		return player;
	}

	[After(Test)]
	public async Task DisconnectPlayers()
	{
		foreach (var handle in _handles) await Connections.Disconnect(handle);
	}

	private async Task<CallState> God(string text) => await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain(text));

	private async Task<DBRef> Room(params TestIsolationHelpers.TestPlayer[] players)
	{
		var result = await God($"@dig {Guid.NewGuid():N}");
		var room = DBRef.Parse(result.Message!.ToPlainText().Trim());
		foreach (var player in players) await God($"@tel {player.DbRef}={room}");
		return room;
	}

	private bool Heard(DBRef who, string message) => Factory.Notifications.For(who).Contains(message);

	[Test]
	public async Task AMortalElsewhereStillOmitsTheNamedRecipient()
	{
		var actor = await Player("OemitFarActor");
		var omitted = await Player("OemitFarOmit");
		var listener = await Player("OemitFarHears");
		await Room(actor);
		var target = await Room(omitted, listener);
		var message = $"far_{Guid.NewGuid():N}";

		await Factory.CommandParserFor(actor.DbRef, actor.Handle)
			.CommandParse(actor.Handle, Connections, MarkupText.Plain($"@oemit {target}/{omitted.DbRef}={message}"));

		await Assert.That(Heard(listener.DbRef, message)).IsTrue()
			.Because("everyone else in the named room hears it");
		await Assert.That(Heard(omitted.DbRef, message)).IsFalse()
			.Because("the named recipient is omitted, whether or not the sender stands in that room");
	}
}
