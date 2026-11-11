using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Commands;

/// <summary>TinyMUX's <c>@addcommand</c>, <c>@delcommand</c> and <c>@listcommands</c>, under the <c>mux</c> dialect.</summary>
public class DialectAddedCommandTests : ServerTestBase
{
	private static IDisposable Mux() => TestOptionsOverride.Scope(options => options with
	{
		Compatibility = options.Compatibility with { Dialect = "mux" }
	});

	private DBRef God => WebAppFactoryArg.ExecutorDBRef;

	private bool GodHeard(string text)
		=> Notifications.For(God).Any(line => line.Contains(text, StringComparison.Ordinal));

	/// <summary>A thing whose CMD attribute answers <c>&lt;command&gt; &lt;text&gt;</c> with a unique token.</summary>
	private async Task<(DBRef Thing, string Token)> HandlerAsync(string command)
	{
		var token = TestIsolationHelpers.GenerateUniqueName("added");
		var thing = DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("Adder")}"));
		await Cmd($"&CMD {thing}=${command} *:@pemit %#={token} %0");
		await Cmd($"@set {thing}=!NO_COMMAND");
		// Out of reach, so only the added command runs it: a $-command here would match the line by itself.
		var room = DBRef.Parse(await Cmd($"@dig {TestIsolationHelpers.GenerateUniqueName("AdderRoom")}"));
		await Cmd($"@tel {thing}={room}");
		return (thing, token);
	}

	[Test]
	public async Task AddedCommandRunsTheAttributeUntilDeleted()
	{
		var command = $"{TestIsolationHelpers.GenerateUniqueName("dial").ToLowerInvariant()}";
		var (thing, token) = await HandlerAsync(command);

		using (Mux())
		{
			await Cmd($"@addcommand {command}={thing}/CMD");
			await Cmd($"{command} first");
			await WebAppFactoryArg.QueueBarrierAsync();
			await Assert.That(GodHeard($"{token} first")).IsTrue();

			await Cmd("@listcommands");
			await Assert.That(GodHeard($"{command.ToUpperInvariant()}: #{thing.Number}/CMD")).IsTrue();

			await Cmd($"@delcommand {command}");
			await Cmd($"{command} second");
			await WebAppFactoryArg.QueueBarrierAsync();
		}

		await Assert.That(GodHeard($"{token} second")).IsFalse();
	}

	[Test]
	public async Task AddedCommandOverABuiltInKeepsItAsDoubleUnderscore()
	{
		var command = $"{TestIsolationHelpers.GenerateUniqueName("base").ToLowerInvariant()}";
		var (thing, token) = await HandlerAsync(command);
		await Cmd($"@command/add {command}");

		using (Mux())
		{
			await Cmd($"@addcommand {command}={thing}/CMD");
			await Cmd($"{command} hooked");
			await WebAppFactoryArg.QueueBarrierAsync();
			await Assert.That(GodHeard($"{token} hooked")).IsTrue();
			await Assert.That(await Eval($"words(match(listcommands(),__{command}))")).IsNotEqualTo("0");

			await Cmd($"@delcommand {command}={thing}/CMD");
		}

		await Assert.That(await Eval($"match(listcommands(),__{command})")).IsEqualTo("0");
		await Assert.That(await Eval($"match(listcommands(),{command})")).IsNotEqualTo("0");
	}

	/// <summary>An alias adds to the command it names; a command deleted behind @addcommand's back can be added again.</summary>
	[Test]
	public async Task AddedCommandFollowsAliasesAndSurvivesADelete()
	{
		var command = TestIsolationHelpers.GenerateUniqueName("aliased").ToLowerInvariant();
		var alias = TestIsolationHelpers.GenerateUniqueName("alias").ToLowerInvariant();
		var (thing, token) = await HandlerAsync(command);
		await Cmd($"@command/add {command}");
		await Cmd($"@command/alias {command}={alias}");

		using (Mux())
		{
			await Cmd($"@addcommand {alias}={thing}/CMD");
			await Cmd($"{alias} viaalias");
			await WebAppFactoryArg.QueueBarrierAsync();
			await Assert.That(GodHeard($"{token} viaalias")).IsTrue();
			await Cmd($"@delcommand {alias}");
		}

		var fresh = TestIsolationHelpers.GenerateUniqueName("fresh").ToLowerInvariant();
		var (other, freshToken) = await HandlerAsync(fresh);
		using (Mux())
		{
			await Cmd($"@addcommand {fresh}={other}/CMD");
			await Cmd($"@command/delete {fresh}");
			await Cmd($"@addcommand {fresh}={other}/CMD");
			await Cmd($"{fresh} again");
			await WebAppFactoryArg.QueueBarrierAsync();
			await Cmd($"@delcommand {fresh}");
		}

		await Assert.That(GodHeard($"{freshToken} again")).IsTrue();
		await Assert.That(await Eval($"match(listcommands(),__{command})")).IsEqualTo("0");
	}

	[Test]
	public async Task OnlyGodAddsCommands()
	{
		var command = $"{TestIsolationHelpers.GenerateUniqueName("mortal").ToLowerInvariant()}";
		var (thing, _) = await HandlerAsync(command);
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AddCmdMortal");
		try
		{
			using (Mux())
				await CmdAs(mortal.DbRef, mortal.Handle, $"@addcommand {command}={thing}/CMD");

			await Assert.That(Notifications.For(mortal.DbRef).Any(line => line.StartsWith("Permission denied", StringComparison.Ordinal))).IsTrue();
			await Assert.That(await Eval($"match(listcommands(),{command})")).IsEqualTo("0");
		}
		finally
		{
			await ConnectionService.Disconnect(mortal.Handle);
		}
	}
}
