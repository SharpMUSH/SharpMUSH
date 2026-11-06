using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
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

	[Test]
	public async Task FlaggedAttributeEscapesUrlsTheTinyMuxWay()
	{
		var thing = DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("DialectUrl")}"));
		await Cmd($"&FLAGGED {thing}=[url_escape(hello world)] [url_escape(foo@bar.com)] [url_escape(key=value&x=1)]");
		await Cmd($"@set {thing}/FLAGGED=mux");

		await Assert.That(await Eval($"u({thing}/FLAGGED)")).IsEqualTo("hello%20world foo%40bar.com key%3Dvalue%26x%3D1");
	}

	/// <summary><c>@nemit</c>, <c>@npemit</c> and <c>train</c> are TinyMUX's names for <c>@emit/noeval</c>,
	/// <c>@pemit/noeval</c> and <c>teach</c>.</summary>
	[Test]
	public async Task GameDialectReadsTinyMuxSpeechCommands()
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(WebAppFactoryArg.ExecutorDBRef))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var room = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName("DialectMuxRoom"), god));
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DialectMuxSpeech", room);
		var token = TestIsolationHelpers.GenerateUniqueName("dialectspeech");
		try
		{
			using var dialect = TestOptionsOverride.Scope(options => options with
			{
				Compatibility = options.Compatibility with { Dialect = "mux" }
			});
			await CmdAs(actor.DbRef, actor.Handle, $"@nemit {token}-nemit-[add(1,1)]");
			await CmdAs(actor.DbRef, actor.Handle, $"@npemit me={token}-npemit-[add(1,1)]");
			await CmdAs(actor.DbRef, actor.Handle, $"train think {token}-train-[add(1,1)]");
			await WebAppFactoryArg.QueueBarrierAsync();

			var heard = Notifications.For(actor.DbRef).Where(message => message.Contains(token, StringComparison.Ordinal)).ToList();
			await Assert.That(heard).Contains($"{token}-nemit-[add(1,1)]");
			await Assert.That(heard).Contains($"{token}-npemit-[add(1,1)]");
			await Assert.That(heard.Any(message => message.EndsWith($"think {token}-train-[add(1,1)]", StringComparison.Ordinal)))
				.IsTrue();
			await Assert.That(heard).Contains($"{token}-train-2");
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	/// <summary>
	/// TinyMUX's <c>@reject</c> sets the message a refused pager is sent, which SharpMUSH keeps in HAVEN; it is not
	/// SharpMUSH's <c>@rejectmotd</c>, which it would otherwise abbreviate.
	/// </summary>
	[Test]
	public async Task GameDialectSetsHavenForTinyMuxReject()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DialectMuxReject");
		var token = TestIsolationHelpers.GenerateUniqueName("dialectreject");
		try
		{
			using (TestOptionsOverride.Scope(options => options with
			{
				Compatibility = options.Compatibility with { Dialect = "mux" }
			}))
			{
				await CmdAs(actor.DbRef, actor.Handle, $"@reject me={token}");
				await WebAppFactoryArg.QueueBarrierAsync();
			}

			await Assert.That(await Eval($"get({actor.DbRef}/HAVEN)")).IsEqualTo(token);
			await Assert.That(Notifications.For(actor.DbRef).Any(message => message.Contains("MOTD", StringComparison.OrdinalIgnoreCase)))
				.IsFalse();
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	/// <summary>
	/// TinyMUX's <c>@fe</c> (its <c>@femit</c>) and <c>@last</c> (a login history) have no SharpMUSH command, so they
	/// answer <c>Huh?</c> rather than running <c>@feed</c> or setting LAST.
	/// </summary>
	[Test]
	public async Task GameDialectLeavesTinyMuxCommandsSharpMushLacksUnmatched()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DialectMuxHuh");
		var token = TestIsolationHelpers.GenerateUniqueName("dialecthuh");
		try
		{
			using (TestOptionsOverride.Scope(options => options with
			{
				Compatibility = options.Compatibility with { Dialect = "mux" }
			}))
			{
				await CmdAs(actor.DbRef, actor.Handle, $"@fe me={token}");
				await CmdAs(actor.DbRef, actor.Handle, $"@last me={token}");
				await WebAppFactoryArg.QueueBarrierAsync();
			}

			await Assert.That(Notifications.For(actor.DbRef).Count(message => message.StartsWith("Huh?", StringComparison.Ordinal)))
				.IsEqualTo(2);
			await Assert.That(await Eval($"get({actor.DbRef}/LAST)")).IsNotEqualTo(token);
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
		var attributes = await Mediator.CreateStream(new GetAllAttributeEntriesQuery())
			.Select(entry => entry.Name)
			.ToHashSetAsync(StringComparer.OrdinalIgnoreCase);
		var missing = SoftcodeDialects.All.Values
			.SelectMany(dialect => dialect.FunctionAliases
					.Where(alias => !Parser.FunctionLibrary.ContainsKey(alias.Value))
					.Select(alias => $"{dialect.Key} {alias.Key}()")
				.Concat(dialect.CommandAliases
					.Select(alias => (alias.Key, Root: alias.Value.Split('/')[0]))
					.Where(alias => commands.FindExact(alias.Root) is null
						&& !(alias.Root.StartsWith('@') && attributes.Contains(alias.Root[1..])))
					.Select(alias => $"{dialect.Key} {alias.Key}")))
			.ToList();

		await Assert.That(missing).IsEmpty();
	}

	/// <summary>A name SharpMUSH has a command for is never read as the dialect's, so listing it as lacking would do nothing.</summary>
	[Test]
	public async Task EveryUnsupportedDialectCommandIsOneSharpMushLacks()
	{
		var commands = CommandTrie.For(Parser.CommandLibrary);
		var present = SoftcodeDialects.All.Values
			.SelectMany(dialect => dialect.UnsupportedCommands
				.Where(name => commands.FindExact(name) is not null || dialect.CommandAliases.ContainsKey(name))
				.Select(name => $"{dialect.Key} {name}"))
			.ToList();

		await Assert.That(present).IsEmpty();
	}
}
