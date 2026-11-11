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

	/// <summary>
	/// A switch the command lacks is read as the dialect's: RhostMUSH's <c>@edit/single</c> is <c>@edit/first</c>,
	/// and its <c>@set/noisy</c> is what <c>@set</c> does anyway. Outside the dialect both are unknown switches.
	/// </summary>
	[Test]
	public async Task FlaggedAttributeReadsRhostSwitches()
	{
		var thing = DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("DialectSwitch")}"));
		await Cmd($"&TARGET {thing}=aaa");
		await Cmd($"&PLAIN {thing}=@edit/single me/TARGET=a,b");
		await Cmd($"@trigger {thing}/PLAIN");
		await WebAppFactoryArg.QueueBarrierAsync();
		await Assert.That(await Eval($"get({thing}/TARGET)")).IsEqualTo("aaa");

		await Cmd($"&FLAGGED {thing}=@edit/single me/TARGET=a,b;@set/noisy me=SET`AFTER:[get(me/TARGET)]");
		await Cmd($"@set {thing}/FLAGGED=rhost");
		await Cmd($"@trigger {thing}/FLAGGED");
		await WebAppFactoryArg.QueueBarrierAsync();
		await Assert.That(await Eval($"get({thing}/SET`AFTER)")).IsEqualTo("baa");
	}

	/// <summary>
	/// In a dialect a switch may be shortened as the source server allows: <c>@switch/f</c> is <c>/first</c> and
	/// <c>@edit/sing</c> is RhostMUSH's <c>/single</c>. A start is read against that server's own switches, so it never
	/// becomes a different SharpMUSH switch, and outside the dialect a shortened switch is unknown.
	/// </summary>
	[Test]
	public async Task FlaggedAttributeTakesAShortenedSwitch()
	{
		var thing = DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("DialectShort")}"));
		await Cmd($"&TARGET {thing}=aaa");
		await Cmd($"&FLAGGED {thing}=@switch/f 1=1,&FIRST me=one,1,&SECOND me=two;@edit/sing me/TARGET=a,b");
		await Cmd($"@set {thing}/FLAGGED=rhost");
		await Cmd($"@trigger {thing}/FLAGGED");
		// @switch queues the action it picks, so it runs after the trigger's own entry.
		await WebAppFactoryArg.QueueBarrierAsync();
		await WebAppFactoryArg.QueueBarrierAsync();
		await Assert.That(await Eval($"[get({thing}/FIRST)] [hasattr({thing},SECOND)] [get({thing}/TARGET)]"))
			.IsEqualTo("one 0 baa");

		var rhost = SoftcodeDialects.All["rhost"];
		string[] switchSwitches = ["NOTIFY", "FIRST", "ALL", "REGEXP", "REGALL", "REGFIRST"];
		await Assert.That(rhost.SwitchMeaning("@SWITCH", switchSwitches, "reg")).IsNull();
		await Assert.That(rhost.SwitchMeaning("@SWITCH", switchSwitches, "regf")).IsEquivalentTo(["REGFIRST"]);
		await Assert.That(rhost.SwitchMeaning("@EDIT", ["FIRST"], "sing")).IsEquivalentTo(["FIRST"]);
		// RhostMUSH reads /r as its own /raw, which SharpMUSH lacks, never as /regexp.
		await Assert.That(rhost.SwitchMeaning("@EDIT", ["FIRST", "REGEXP"], "r")).IsNull();
		// The first switch in RhostMUSH's table that the start fits, even where SharpMUSH has two.
		await Assert.That(rhost.SwitchMeaning("@DOLIST", ["CLEARREG", "CLEARREGS", "NOBREAK"], "c")).IsEquivalentTo(["CLEARREG"]);
		await Assert.That(SoftcodeDialects.All["mux"].SwitchMeaning("@DOLIST", ["NOTIFY", "NOBREAK", "NOW"], "n"))
			.IsEquivalentTo(["NOTIFY"]);

		await Cmd($"&PLAIN {thing}=@switch/f 1=1,&PLAINRAN me=yes");
		await Cmd($"@trigger {thing}/PLAIN");
		await WebAppFactoryArg.QueueBarrierAsync();
		await WebAppFactoryArg.QueueBarrierAsync();
		await Assert.That(await Eval($"hasattr({thing},PLAINRAN)")).IsEqualTo("0");
	}

	/// <summary>TinyMUX's <c>@decompile/dbref</c> is <c>@decompile/db</c>: the commands name the object by its dbref.</summary>
	[Test]
	public async Task FlaggedAttributeReadsTinyMuxSwitches()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DialectDecomp");
		var mark = TestIsolationHelpers.GenerateUniqueName("dialectmark");
		try
		{
			await Cmd($"&MARK {actor.DbRef}={mark}");
			await Cmd($"&FLAGGED {actor.DbRef}=@decompile/dbref me/MARK");
			await Cmd($"@set {actor.DbRef}/FLAGGED=mux");
			await Cmd($"@trigger {actor.DbRef}/FLAGGED");
			await WebAppFactoryArg.QueueBarrierAsync();

			await Assert.That(Notifications.For(actor.DbRef).Where(message => message.Contains(mark, StringComparison.Ordinal)))
				.IsEquivalentTo([$"&MARK #{actor.DbRef.Number}={mark}"]);
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
