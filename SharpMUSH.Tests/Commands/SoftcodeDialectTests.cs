using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Softcode;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// An attribute with the <c>rhost</c> flag, and anything run under <c>dialect=rhost</c>, reads RhostMUSH's names
/// for functions and commands SharpMUSH has under its own.
/// </summary>
public class SoftcodeDialectTests : ServerTestBase
{
	[Test]
	public async Task FlaggedAttributeCallsRhostFunctionNames()
	{
		var thing = DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("DialectFn")}"));
		await Cmd($"&FLAGGED {thing}=[wildmatch(a b c,b)]");
		await Cmd($"@set {thing}/FLAGGED=rhost");
		await Cmd($"&PLAIN {thing}=[wildmatch(a b c,b)]");

		await Assert.That(await Eval($"u({thing}/FLAGGED)")).IsEqualTo("2");
		await Assert.That(await Eval($"u({thing}/PLAIN)")).IsEqualTo("#-1 FUNCTION (WILDMATCH) NOT FOUND");
	}

	[Test]
	public async Task GameDialectCoversTypedInput()
	{
		await Assert.That(await Eval("[wildmatch(a b c,c)]")).IsEqualTo("#-1 FUNCTION (WILDMATCH) NOT FOUND");

		using var dialect = TestOptionsOverride.Scope(options => options with
		{
			Compatibility = options.Compatibility with { Dialect = "rhost" }
		});
		await Assert.That(await Eval("[wildmatch(a b c,c)]")).IsEqualTo("3");
	}

	[Test]
	public async Task FlaggedAttributeRunsRhostCommandWithItsSwitches()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DialectCmd");
		var thing = DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("DialectCmd")}"));
		var flagged = TestIsolationHelpers.GenerateUniqueName("dialectflagged");
		var plain = TestIsolationHelpers.GenerateUniqueName("dialectplain");
		try
		{
			await Cmd($"&FLAGGED {thing}=@sudo me=@pemit %0={flagged}");
			await Cmd($"@set {thing}/FLAGGED=rhost");
			await Cmd($"&PLAIN {thing}=@sudo me=@pemit %0={plain}");
			await Cmd($"@trigger {thing}/FLAGGED={actor.DbRef}");
			await Cmd($"@trigger {thing}/PLAIN={actor.DbRef}");
			await WebAppFactoryArg.QueueBarrierAsync();

			await Assert.That(Notifications.For(actor.DbRef).Where(message => message.StartsWith(flagged, StringComparison.Ordinal)))
				.IsEquivalentTo([flagged]);
			await Assert.That(Notifications.For(actor.DbRef).Any(message => message.Contains(plain, StringComparison.Ordinal)))
				.IsFalse();
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	[Test]
	public async Task GameDialectCoversTypedCommands()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DialectTyped");
		var token = TestIsolationHelpers.GenerateUniqueName("dialecttyped");
		try
		{
			using var dialect = TestOptionsOverride.Scope(options => options with
			{
				Compatibility = options.Compatibility with { Dialect = "rhost" }
			});
			await CmdAs(actor.DbRef, actor.Handle, $"@sudo me=@pemit me={token}");
			await WebAppFactoryArg.QueueBarrierAsync();

			await Assert.That(Notifications.For(actor.DbRef).Where(message => message.StartsWith(token, StringComparison.Ordinal)))
				.IsEquivalentTo([token]);
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	/// <summary>A dialect name typed alone runs its command with no argument, not with the name as one.</summary>
	[Test]
	public async Task BareDialectCommandTakesNoArgument()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DialectBare");
		try
		{
			await Cmd($"@set {actor.DbRef}=WIZARD");
			using var dialect = TestOptionsOverride.Scope(options => options with
			{
				Compatibility = options.Compatibility with { Dialect = "rhost" }
			});
			await CmdAs(actor.DbRef, actor.Handle, "wizhelp");
			await WebAppFactoryArg.QueueBarrierAsync();

			await Assert.That(Notifications.For(actor.DbRef)).IsNotEmpty();
			await Assert.That(Notifications.For(actor.DbRef).Any(message => message.Contains("wizhelp", StringComparison.OrdinalIgnoreCase)))
				.IsFalse();
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	[Test]
	public async Task FlaggedAttributeCallsTinyMuxFunctionNames()
	{
		var thing = DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("DialectMux")}"));
		await Cmd($"&FLAGGED {thing}=[strtrunc(abcdef,3)] [iadd(1,2,3)] [rpad(ab,4,.)]");
		await Cmd($"@set {thing}/FLAGGED=mux");

		await Assert.That(await Eval($"u({thing}/FLAGGED)")).IsEqualTo("abc 6 ..ab");
		await Assert.That(await Eval("[strtrunc(abcdef,3)]")).StartsWith("#-1 FUNCTION (STRTRUNC) NOT FOUND");
	}

	[Test]
	public async Task GameDialectReadsTinyMuxCommandNames()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DialectMuxCmd");
		var token = TestIsolationHelpers.GenerateUniqueName("dialectmux");
		try
		{
			using var dialect = TestOptionsOverride.Scope(options => options with
			{
				Compatibility = options.Compatibility with { Dialect = "mux" }
			});
			await CmdAs(actor.DbRef, actor.Handle, $"@pe me={token}");
			await CmdAs(actor.DbRef, actor.Handle, $"nsay {token}-[add(1,1)]");
			await WebAppFactoryArg.QueueBarrierAsync();

			await Assert.That(Notifications.For(actor.DbRef).Where(message => message.StartsWith(token, StringComparison.Ordinal)))
				.IsEquivalentTo([token]);
			await Assert.That(Notifications.For(actor.DbRef).Any(message => message.Contains($"{token}-[add(1,1)]", StringComparison.Ordinal)))
				.IsTrue();
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	/// <summary>An alias that names nothing would read as a missing function or <c>Huh?</c>, as if it were not there.</summary>
	[Test]
	public async Task EveryDialectAliasNamesSomethingSharpMushHas()
	{
		var commands = CommandTrie.For(Parser.CommandLibrary);
		var missing = SoftcodeDialects.All.Values
			.SelectMany(dialect => dialect.FunctionAliases
					.Where(alias => !Parser.FunctionLibrary.ContainsKey(alias.Value))
					.Select(alias => $"{dialect.Key} {alias.Key}()")
				.Concat(dialect.CommandAliases
					.Where(alias => commands.FindExact(alias.Value.Split('/')[0]) is null)
					.Select(alias => $"{dialect.Key} {alias.Key}")))
			.ToList();

		await Assert.That(missing).IsEmpty();
	}
}
