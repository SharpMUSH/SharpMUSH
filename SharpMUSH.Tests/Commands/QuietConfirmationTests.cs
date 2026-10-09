using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// PennMUSH's QUIET flag silences the confirmations a command prints about the player's own work.
/// Most sites test <c>AreQuiet(player, thing)</c> (<c>hdrs/dbdefs.h:198</c>: the player is QUIET, or
/// the thing is QUIET and the player owns it); the semaphore commands test <c>IsQuiet(executor)</c>
/// through <c>quiet_notify</c> (<c>hdrs/dbdefs.h:197</c>, <c>hdrs/notify.h:153-155</c>).
/// </summary>
/// <remarks>
/// Each player stands in a room of its own, so what it hears is only what its own commands caused;
/// assertions name a unique attribute or object, never "the last line".
/// </remarks>
public class QuietConfirmationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task<TestIsolationHelpers.TestPlayer> PlayerInOwnRoom(string prefix, bool quiet)
	{
		var room = await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName($"{prefix}Room")}"));
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, DBRef.Parse(room.Message.ToPlainText()));
		if (quiet)
		{
			await God($"@set {player.DbRef}=QUIET");
		}

		return player;
	}

	private ValueTask<CallState> God(string command)
		=> Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	private ValueTask<CallState> As(TestIsolationHelpers.TestPlayer player, string command)
		=> Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));

	/// <summary>
	/// Everything <paramref name="who"/> was notified of while <paramref name="action"/> ran, whether
	/// addressed to the object or to its connection (the standard-attribute commands answer the
	/// connection that typed them).
	/// </summary>
	private async Task<List<string>> MessagesWhile(DBRef who, long handle, Func<Task> action)
	{
		var recorder = WebAppFactoryArg.Notifications;
		var before = recorder.CountFor(who);
		var beforeHandle = recorder.CountForHandle(handle);
		await action();
		return [.. recorder.For(who).Skip(before), .. recorder.ForHandle(handle).Skip(beforeHandle)];
	}

	private static bool Says(List<string> said, string text)
		=> said.Any(message => message.Contains(text, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// <c>&amp;attr</c> reaches <c>do_set_atr(..., 1)</c> (<c>src/cmds.c:1790</c>), whose
	/// "- Set." / "- Cleared." line is gated on <c>!AreQuiet(player, thing)</c>
	/// (<c>src/attrib.c:2446</c>).
	/// </summary>
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async ValueTask AttributeSetCommand_IsSilentForAQuietPlayer(bool quiet)
	{
		var player = await PlayerInOwnRoom("QuietAmp", quiet);
		var attribute = TestIsolationHelpers.GenerateUniqueName("QAMP").ToUpperInvariant();

		var said = await MessagesWhile(player.DbRef, player.Handle, async () =>
		{
			await As(player, $"&{attribute} me=value");
			await As(player, $"&{attribute} me");
		});

		await Assert.That(Says(said, $"/{attribute} - Set.")).IsEqualTo(!quiet);
		await Assert.That(Says(said, $"/{attribute} - Cleared.")).IsEqualTo(!quiet);
	}

	/// <summary>
	/// The standard-attribute commands (<c>@desc</c>, <c>@succ</c>, ...) are <c>do_set_atr</c> with the
	/// report flag too, and share its QUIET gate (<c>src/attrib.c:2446</c>).
	/// </summary>
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async ValueTask StandardAttributeCommand_IsSilentForAQuietPlayer(bool quiet)
	{
		var player = await PlayerInOwnRoom("QuietDesc", quiet);
		var thing = TestIsolationHelpers.GenerateUniqueName("QuietDescThing");
		await As(player, $"@create {thing}");

		var said = await MessagesWhile(player.DbRef, player.Handle, async () =>
		{
			await As(player, $"@desc {thing}=A plain thing.");
			await As(player, $"@desc {thing}");
		});

		await Assert.That(Says(said, $"{thing}/DESCRIBE - Set.")).IsEqualTo(!quiet).Because(string.Join(" | ", said));
		await Assert.That(Says(said, $"{thing}/DESCRIBE - Cleared.")).IsEqualTo(!quiet);
	}

	/// <summary>
	/// <c>AreQuiet</c>'s second half: a player who is not QUIET is still not told about writes to a
	/// QUIET object they own. <c>&amp;attr</c> (<c>src/attrib.c:2446</c>), <c>@edit</c>
	/// (<c>src/set.c:936</c>), <c>@parent</c> (<c>src/set.c:1488</c>) and <c>@link</c>'s "Home set."
	/// (<c>src/create.c:419</c>) all ask it.
	/// </summary>
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async ValueTask WritesToAQuietOwnedObject_AreSilent(bool quietThing)
	{
		var player = await PlayerInOwnRoom("QuietOwned", quiet: false);
		var thing = TestIsolationHelpers.GenerateUniqueName("QuietOwnedThing");
		var parent = TestIsolationHelpers.GenerateUniqueName("QuietOwnedParent");
		var attribute = TestIsolationHelpers.GenerateUniqueName("QOWN").ToUpperInvariant();
		await As(player, $"@create {thing}");
		await As(player, $"@create {parent}");
		if (quietThing)
		{
			await As(player, $"@set {thing}=QUIET");
		}

		var said = await MessagesWhile(player.DbRef, player.Handle, async () =>
		{
			await As(player, $"&{attribute} {thing}=alpha");
			await As(player, $"@edit {thing}/{attribute}=alpha,beta");
			await As(player, $"@parent {thing}={parent}");
			await As(player, $"@link {thing}=me");
		});

		await Assert.That(Says(said, $"{thing}/{attribute} - Set.")).IsEqualTo(!quietThing)
			.Because("do_set_atr asks AreQuiet(player, thing)");
		await Assert.That(said.Any(message => message.Equals($"{attribute} - Set: beta", StringComparison.OrdinalIgnoreCase)))
			.IsEqualTo(!quietThing).Because("do_edit's Set line asks AreQuiet(player, thing)");
		await Assert.That(said.Contains("Parent changed.")).IsEqualTo(!quietThing)
			.Because("do_parent asks AreQuiet(player, thing)");
		await Assert.That(said.Contains("Home set.")).IsEqualTo(!quietThing)
			.Because("do_link asks AreQuiet(player, thing) before Home set: " + string.Join(" | ", said));
	}

	/// <summary>
	/// <c>set_power</c> reports a grant only when <c>!AreQuiet(player, thing)</c>
	/// (<c>src/flags.c:1978</c>).
	/// </summary>
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async ValueTask PowerGrant_ToAQuietOwnedObject_IsSilent(bool quietThing)
	{
		var thing = TestIsolationHelpers.GenerateUniqueName("QuietPowerThing");
		await God($"@create {thing}");
		if (quietThing)
		{
			await God($"@set {thing}=QUIET");
		}

		var said = await MessagesWhile(WebAppFactoryArg.ExecutorDBRef, 1, async () => await God($"@power {thing}=Builder"));

		await Assert.That(Says(said, $"{thing} - Builder granted.")).IsEqualTo(!quietThing).Because(string.Join(" | ", said));
	}

	/// <summary>
	/// <c>cmd_notify_drain</c> confirms through <c>quiet_notify</c> (<c>src/cque.c:1509, 1539, 1541</c>):
	/// "Notified." and "Drained." reach an executor only when neither it nor its owner is QUIET.
	/// </summary>
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async ValueTask SemaphoreConfirmations_AreSilentForAQuietPlayer(bool quiet)
	{
		var player = await PlayerInOwnRoom("QuietSem", quiet);

		var said = await MessagesWhile(player.DbRef, player.Handle, async () =>
		{
			await As(player, "@notify me");
			await As(player, "@drain me");
		});

		await Assert.That(said.Contains("Notified.")).IsEqualTo(!quiet);
		await Assert.That(said.Contains("Drained.")).IsEqualTo(!quiet);
	}
}
