using SharpMUSH.Library.Models;

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
}
