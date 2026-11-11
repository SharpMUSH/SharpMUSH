using SharpMUSH.Implementation.Commands;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Commands TinyMUX and RhostMUSH have and SharpMUSH has no name for: <c>@femit</c>, <c>@fpose</c>, <c>@fsay</c> and
/// <c>@last</c> under those dialects, and PennMUSH's <c>#&lt;dbref&gt; &lt;command&gt;</c> and <c>PUEBLOCLIENT</c>
/// everywhere.
/// </summary>
public class DialectCommandTests : ServerTestBase
{
	private static IDisposable Dialect(string dialect) => TestOptionsOverride.Scope(options => options with
	{
		Compatibility = options.Compatibility with { Dialect = dialect }
	});

	/// <summary>A player in a room of its own, so what it hears is only what the test made.</summary>
	private async Task<TestIsolationHelpers.TestPlayer> IsolatedPlayerAsync(string prefix)
	{
		var room = DBRef.Parse(await Cmd($"@dig {TestIsolationHelpers.GenerateUniqueName($"{prefix}Room")}"));
		return await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, room);
	}

	/// <summary>A thing <paramref name="actor"/> owns, dropped in its room.</summary>
	private async Task<(DBRef Thing, string Name)> PuppetAsync(TestIsolationHelpers.TestPlayer actor)
	{
		var name = TestIsolationHelpers.GenerateUniqueName("Puppet");
		var thing = DBRef.Parse(await CmdAs(actor.DbRef, actor.Handle, $"@create {name}"));
		await CmdAs(actor.DbRef, actor.Handle, $"drop {thing}");
		return (thing, name);
	}

	private IEnumerable<string> Heard(TestIsolationHelpers.TestPlayer actor, string token)
		=> Notifications.For(actor.DbRef).Where(line => line.Contains(token, StringComparison.Ordinal));

	[Test]
	[Arguments("mux")]
	[Arguments("rhost")]
	public async Task ForcedSpeechSpeaksAsTheObject(string dialect)
	{
		var actor = await IsolatedPlayerAsync("Femit");
		var (thing, name) = await PuppetAsync(actor);
		var emitted = TestIsolationHelpers.GenerateUniqueName("femit");
		var posed = TestIsolationHelpers.GenerateUniqueName("fpose");
		var said = TestIsolationHelpers.GenerateUniqueName("fsay");
		try
		{
			using (Dialect(dialect))
			{
				await CmdAs(actor.DbRef, actor.Handle, $"@femit {thing}={emitted} [add(1,2)]");
				await CmdAs(actor.DbRef, actor.Handle, $"@fpose {thing}={posed}");
				await CmdAs(actor.DbRef, actor.Handle, $"@fpose/nospace {thing}='s {posed}");
				await CmdAs(actor.DbRef, actor.Handle, $"@fsay {thing}={said}");
				await WebAppFactoryArg.QueueBarrierAsync();
			}

			await Assert.That(Heard(actor, emitted)).IsEquivalentTo([$"{emitted} 3"]);
			await Assert.That(Heard(actor, posed)).IsEquivalentTo([$"{name} {posed}", $"{name}'s {posed}"]);
			await Assert.That(Heard(actor, said)).IsEquivalentTo([$"{name} says, \"{said}\""]);
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	/// <summary>TinyMUX's <c>@fe</c> is its <c>@femit</c>, not SharpMUSH's <c>@feed</c>, and <c>/room</c> reaches the room.</summary>
	[Test]
	public async Task TinyMuxAbbreviationEmitsFromTheObject()
	{
		var actor = await IsolatedPlayerAsync("FemitAbbrev");
		var (thing, _) = await PuppetAsync(actor);
		var here = TestIsolationHelpers.GenerateUniqueName("femithere");
		var room = TestIsolationHelpers.GenerateUniqueName("femitroom");
		try
		{
			using (Dialect("mux"))
			{
				await CmdAs(actor.DbRef, actor.Handle, $"@fe {thing}={here}");
				await CmdAs(actor.DbRef, actor.Handle, $"@femit/room {thing}={room}");
				await WebAppFactoryArg.QueueBarrierAsync();
			}

			await Assert.That(Heard(actor, here)).IsEquivalentTo([here]);
			await Assert.That(Heard(actor, room)).IsEquivalentTo([room]);
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	/// <summary>Both switches on an object in a room name that room once; an empty message says nothing.</summary>
	[Test]
	public async Task ForcedEmitHereAndRoomEmitsOnceAndNeedsAMessage()
	{
		var actor = await IsolatedPlayerAsync("FemitBoth");
		var (thing, name) = await PuppetAsync(actor);
		var both = TestIsolationHelpers.GenerateUniqueName("femitboth");
		try
		{
			using (Dialect("mux"))
			{
				await CmdAs(actor.DbRef, actor.Handle, $"@femit/here/room {thing}={both}");
				await CmdAs(actor.DbRef, actor.Handle, $"@fsay {thing}");
				await WebAppFactoryArg.QueueBarrierAsync();
			}

			await Assert.That(Heard(actor, both)).IsEquivalentTo([both]);
			await Assert.That(Heard(actor, $"{name} says")).IsEmpty();
			await Assert.That(Heard(actor, "Don't you have anything to say?")).IsNotEmpty();
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	[Test]
	public async Task ForcedSpeechNeedsControl()
	{
		var actor = await IsolatedPlayerAsync("FsayDenied");
		var owner = await IsolatedPlayerAsync("FsayOwner");
		var (thing, _) = await PuppetAsync(owner);
		await Cmd($"@tel {thing}={await Eval($"loc({actor.DbRef})")}");
		var said = TestIsolationHelpers.GenerateUniqueName("fsaydenied");
		try
		{
			using (Dialect("mux"))
			{
				await CmdAs(actor.DbRef, actor.Handle, $"@fsay {thing}={said}");
				await WebAppFactoryArg.QueueBarrierAsync();
			}

			await Assert.That(Heard(actor, said)).IsEmpty();
			await Assert.That(Notifications.For(actor.DbRef).Any(line => line.StartsWith("Permission denied", StringComparison.Ordinal)))
				.IsTrue();
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
			await ConnectionService.Disconnect(owner.Handle);
		}
	}

	/// <summary>Without a dialect the names are not SharpMUSH's, so a line answers <c>Huh?</c> rather than speaking.</summary>
	[Test]
	public async Task ForcedSpeechNeedsTheDialect()
	{
		var actor = await IsolatedPlayerAsync("FemitPlain");
		var (thing, _) = await PuppetAsync(actor);
		var token = TestIsolationHelpers.GenerateUniqueName("femitplain");
		try
		{
			await CmdAs(actor.DbRef, actor.Handle, $"@fsay {thing}={token}");
			await CmdAs(actor.DbRef, actor.Handle, $"@fpose {thing}={token}");
			await WebAppFactoryArg.QueueBarrierAsync();

			await Assert.That(Heard(actor, token)).IsEmpty();
			await Assert.That(Notifications.For(actor.DbRef).Count(line => line.StartsWith("Huh?", StringComparison.Ordinal)))
				.IsEqualTo(2);
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	/// <summary>PennMUSH's <c>#&lt;dbref&gt; &lt;command&gt;</c>, in every dialect: the object runs the command.</summary>
	[Test]
	public async Task DbrefPrefixForcesTheObject()
	{
		var actor = await IsolatedPlayerAsync("ForcePrefix");
		var (thing, name) = await PuppetAsync(actor);
		var said = TestIsolationHelpers.GenerateUniqueName("forceprefix");
		try
		{
			await CmdAs(actor.DbRef, actor.Handle, $"#{thing.Number} say {said} [add(2,2)]");
			// A dbref with nothing after it is not a force.
			await CmdAs(actor.DbRef, actor.Handle, $"#{thing.Number}");
			await WebAppFactoryArg.QueueBarrierAsync();

			await Assert.That(Heard(actor, said)).IsEquivalentTo([$"{name} says, \"{said} 4\""]);
			await Assert.That(Notifications.For(actor.DbRef).Count(line => line.StartsWith("Huh?", StringComparison.Ordinal)))
				.IsEqualTo(1);
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	[Test]
	public async Task LastShowsTheRecordedConnects()
	{
		var actor = await IsolatedPlayerAsync("LastConnects");
		var stranger = await IsolatedPlayerAsync("LastStranger");
		var site = $"{TestIsolationHelpers.GenerateUniqueName("site").ToLowerInvariant()}.example";
		try
		{
			await Cmd($"&LAST {actor.DbRef}=Mon Nov 10 12:00:00 2026");
			await Cmd($"&LASTSITE {actor.DbRef}={site}");
			await Cmd($"&LASTFAILED {actor.DbRef}=bad.{site} on Sun Nov 09 08:00:00 2026");

			using (Dialect("mux"))
			{
				await CmdAs(actor.DbRef, actor.Handle, "@last");
				await CmdAs(stranger.DbRef, stranger.Handle, $"@last {actor.Name}");
				await WebAppFactoryArg.QueueBarrierAsync();
			}

			await Assert.That(Heard(actor, site)).IsEquivalentTo([
				$"Last connect: from {site} on Mon Nov 10 12:00:00 2026.",
				$"Last failed connect: from bad.{site} on Sun Nov 09 08:00:00 2026."
			]);
			await Assert.That(Heard(stranger, site)).IsEmpty();
			await Assert.That(Notifications.For(stranger.DbRef).Any(line => line.StartsWith("Permission denied", StringComparison.Ordinal)))
				.IsTrue();
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
			await ConnectionService.Disconnect(stranger.Handle);
		}
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task PuebloClientTurnsPuebloOnWhileTheGameOffersIt(bool offered)
	{
		var handle = await TestIsolationHelpers.RegisterTestHandleAsync(ConnectionService);
		try
		{
			using (TestOptionsOverride.Scope(options => options with { Net = options.Net with { Pueblo = offered } }))
			{
				await CommandParser.CommandParse(handle, ConnectionService, MarkupText.Plain("PUEBLOCLIENT 2.50 md5=\"x\""));
			}

			await Assert.That(ConnectionService.Get(handle)!.Metadata.GetValueOrDefault(SocketOptions.PuebloKey) == "1")
				.IsEqualTo(offered);
		}
		finally
		{
			await ConnectionService.Disconnect(handle);
		}
	}
}
