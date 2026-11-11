using SharpMUSH.Implementation.Commands;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Commands TinyMUX and RhostMUSH have and SharpMUSH has no name for: <c>@last</c> under those dialects, and
/// PennMUSH's <c>#&lt;dbref&gt; &lt;command&gt;</c> and <c>PUEBLOCLIENT</c> everywhere.
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

	/// <summary>
	/// A TinyMUX import writes <c>@fsay</c> as <c>@force</c>, and the result evaluates the message once, as the one
	/// forcing: what a player typed into it is never run as code by the object.
	/// </summary>
	[Test]
	public async Task RewrittenForcedSpeechEvaluatesTheMessageOnce()
	{
		var actor = await IsolatedPlayerAsync("Fsay");
		var (thing, name) = await PuppetAsync(actor);
		var said = TestIsolationHelpers.GenerateUniqueName("fsayonce");
		try
		{
			var command = SharpMUSH.Library.Services.DatabaseConversion.Softcode.ForcedSpeechRewriter
				.Rewrite($"@fsay {thing}={said} [add(1,2)] \\[add(2,2)]").Text;
			await CmdAs(actor.DbRef, actor.Handle, command);
			await Assert.That(Heard(actor, said)).IsEquivalentTo([$"{name} says, \"{said} 3 [add(2,2)]\""]);
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
