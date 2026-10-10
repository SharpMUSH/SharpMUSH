using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using NSubstitute;
using System.Reflection;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class InputSessionCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private IInputSessionService Sessions => Factory.Services.GetRequiredService<IInputSessionService>();
	private ITaskScheduler Scheduler => Factory.Services.GetRequiredService<ITaskScheduler>();
	private IMUSHCodeParser Parser => Factory.Services.GetRequiredService<IMUSHCodeParser>();
	private ISharpDatabase Database => Factory.Services.GetRequiredService<ISharpDatabase>();

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task StrictCommandListVisitorMarksParserFailures(bool depthLimit)
	{
		var text = depthLimit ? new string('[', 1001) + "1" + new string(']', 1001) : "think [";
		var result = await Parser.CommandListParseVisitor(MarkupText.Plain(text))();
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.HadErrors).IsTrue();
		if (depthLimit) await Assert.That(result.Message.Text).IsEqualTo(SharpMUSH.Library.Definitions.ErrorMessages.Returns.Call);
	}

	private Task<TestIsolationHelpers.TestPlayer> Player() => TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
		Factory.Services, Factory.Services.GetRequiredService<IMediator>(), Connections, "InputSession");
	private async Task<string?> Read(DBRef player, string name) =>
		(await Database.GetAttributeAsync(player, [name], CancellationToken.None).LastOrDefaultAsync())?.Value.Text;
	private Task Command(long handle, string command) => Parser.CommandParse(handle, Connections, MarkupText.Plain(command)).AsTask();
	private ValueTask<SharpMUSH.Library.Models.SchedulerModels.QueueAdmissionResult> Input(long handle, string input) =>
		Scheduler.AdmitUserCommand(handle, MarkupText.Plain(input), ParserState.Empty with { Handle = handle });

	/// <summary>
	/// How long a wait for queued work may take before the test calls it hung.
	/// </summary>
	/// <remarks>
	/// Every wait in this class is for the queue to reach an entry, never for the engine to be quick. One
	/// scheduler, one entry at a time, is shared by the whole session, so the wait covers everything queued
	/// ahead — measured at over four seconds for a single unrelated entry in a full suite run. The
	/// deadline is a hang detector; a real failure to run the entry never becomes a pass by waiting longer.
	/// </remarks>
	private static readonly TimeSpan QueueDeadline = TimeSpan.FromSeconds(30);

	private async Task WaitFor(DBRef player, string attribute, string expected)
	{
		using var timeout = new CancellationTokenSource(QueueDeadline);
		string? last;
		while ((last = await Read(player, attribute)) != expected)
		{
			if (timeout.IsCancellationRequested) throw new TimeoutException($"{attribute} was \"{last}\", not \"{expected}\".");
			await Task.Delay(20, CancellationToken.None);
		}
	}

	[Test]
	[Arguments("Name=value:", "Name=value:")]
	[Arguments("A=B,C,D", "A=B,C,D")]
	[Arguments("=value=", "=value=")]
	[Arguments("plain, text", "plain, text")]
	[Arguments("Name=[add(1,2)]", "Name=3")]
	public async Task RepromptPreservesCompleteSingleArgument(string source, string expected)
	{
		var player = await Player();
		try
		{
			var sessions = Substitute.For<IInputSessionService>();
			string? prompt = null;
			sessions.PromptAsync(Arg.Any<IMUSHCodeParser>(), Arg.Any<MarkupText>()).Returns(call =>
			{ prompt = call.Arg<MarkupText>().ToPlainText(); return ValueTask.FromResult<string?>(null); });
			var provider = Substitute.For<IServiceProvider>();
			provider.GetService(Arg.Any<Type>()).Returns(call => call.Arg<Type>() == typeof(IInputSessionService)
				? sessions : Factory.Services.GetService(call.Arg<Type>()));
			var original = (SharpMUSH.Implementation.MUSHCodeParser)Parser;
			var parser = new SharpMUSH.Implementation.MUSHCodeParser(original.Logger, original.FunctionLibrary,
				original.CommandLibrary, original.Configuration, provider);
			await parser.CommandParse(player.Handle, Connections, MarkupText.Plain("@input/prompt " + source));
			await Assert.That(prompt).IsEqualTo(expected);
		}
		finally { await Connections.Disconnect(player.Handle); }
	}

	[Test]
	[Arguments("syntax", true)]
	[Arguments("throw", true)]
	[Arguments("throw-list", true)]
	[Arguments("literal", false)]
	[Arguments("nested-syntax", false)]
	[Arguments("nested-throw", false)]
	[Arguments("nested-multiple", false)]
	[Arguments("speech", true)]
	[Arguments("ifelse-true", true)]
	[Arguments("ifelse-false", true)]
	[Arguments("ifelse-syntax", true)]
	[Arguments("skip", true)]
	[Arguments("switch", true)]
	[Arguments("select", true)]
	[Arguments("force", true)]
	[Arguments("break-syntax", true)]
	[Arguments("assert-syntax", true)]
	[Arguments("break-literal", false)]
	[Arguments("assert-literal", false)]
	[Arguments("break", true)]
	[Arguments("assert", true)]
	[Arguments("teach", true)]
	[Arguments("teach-list", true)]
	[Arguments("dolist-multiple", true)]
	[Arguments("map-multiple", true)]
	[Arguments("include-multiple", true)]
	[Arguments("include-invalid", true)]
	[Arguments("trigger", true)]
	[Arguments("verb", true)]
	[Arguments("ifelse-literal", false)]
	[Arguments("ifelse-skipped", false)]
	public async Task ParsedCallbackFailureRetiresCapture(string mode, bool failed)
	{
		var player = await Player();
		var original = (SharpMUSH.Implementation.MUSHCodeParser)Parser;
		var name = "@inputfailure" + Guid.NewGuid().ToString("N");
		var invocations = 0;
		var commands = new SharpMUSH.Library.Services.CommandLibraryService();
		foreach (var pair in original.CommandLibrary) commands.Add(pair.Key, pair.Value);
		commands.Add(name, (new SharpMUSH.Library.Definitions.CommandDefinition(
			new SharpCommandAttribute { Name = name, Behavior = CommandBehavior.Default, MinArgs = 0, MaxArgs = 0 },
			_ =>
			{
				invocations++;
				if (mode is "literal" or "ifelse-literal" or "break-literal" or "assert-literal") return ValueTask.FromResult<SharpMUSH.Library.DiscriminatedUnions.Option<CallState>>(new CallState("#-1 EXCEPTION: ordinary text"));
				if (mode.EndsWith("multiple", StringComparison.Ordinal) && invocations > 1) return ValueTask.FromResult<SharpMUSH.Library.DiscriminatedUnions.Option<CallState>>(CallState.Empty);
				throw new InvalidOperationException("input callback failed");
			}), true));
		if (mode == "speech") commands["SAY"] = (new SharpMUSH.Library.Definitions.CommandDefinition(
			commands["SAY"].LibraryInformation.Attribute, _ => throw new InvalidOperationException("speech callback failed")), true);
		IEnumerable<(AnySharpObject Obj, SharpAttribute Attr, Dictionary<string, CallState> Arguments)> matches = [];
		var discovery = Substitute.For<ICommandDiscoveryService>();
		discovery.MatchUserDefinedCommand(Arg.Any<IMUSHCodeParser>(), Arg.Any<IAsyncEnumerable<AnySharpObject>>(), Arg.Any<MarkupText>(), Arg.Any<AnySharpObject>(), Arg.Any<ICollection<AnySharpObject>?>())
			.Returns(_ => ValueTask.FromResult(Option<IEnumerable<(AnySharpObject, SharpAttribute, Dictionary<string, CallState>)>>.FromOption(matches)));
		var provider = Substitute.For<IServiceProvider>();
		provider.GetService(Arg.Any<Type>()).Returns(call => call.Arg<Type>() == typeof(ICommandDiscoveryService) && mode.StartsWith("nested-", StringComparison.Ordinal)
			? discovery : Factory.Services.GetService(call.Arg<Type>()));
		var parser = new SharpMUSH.Implementation.MUSHCodeParser(original.Logger, original.FunctionLibrary,
			commands, original.Configuration, provider);
		try
		{
			var actor = (await Factory.Services.GetRequiredService<IMediator>().Send(
				new SharpMUSH.Library.Queries.Database.GetObjectNodeQuery(player.DbRef))).Expect<AnySharpObject>();
			var callback = mode == "syntax" ? "think [" : mode == "throw-list" ? name + "; think done" : mode == "speech" ? "\"hello" : name;
			if (mode.StartsWith("nested-", StringComparison.Ordinal))
			{
				callback = "inputnested" + Guid.NewGuid().ToString("N");
				var body = mode == "nested-syntax" ? "think [" : name;
				var attribute = new SharpAttribute("nested", "NESTED", "NESTED", [], 0, "NESTED", null!, null!, null!)
				{ Value = MarkupText.Plain(body) };
				matches = Enumerable.Range(0, mode == "nested-multiple" ? 2 : 1)
					.Select(_ => (actor, attribute, new Dictionary<string, CallState>()));
			}
			callback = mode switch
			{
				"ifelse-true" or "ifelse-literal" => $"@ifelse 1={name},think clean",
				"ifelse-false" => $"@ifelse 0=think clean,{name}",
				"ifelse-syntax" => "@ifelse 1={think [},think clean",
				"ifelse-skipped" => $"@ifelse 0={name},think clean",
				"skip" => $"@skip 0={name}",
				"switch" => $"@switch/inline 1=1,{name}",
				"select" => $"@select/inline 1=1,{name}",
				"force" => $"@force me={{{name}}}",
				"break" or "break-literal" => $"@break 1={name}",
				"break-syntax" => "@break 1={think [}",
				"assert-syntax" => "@assert 0={think [}",
				"assert" or "assert-literal" => $"@assert 0={name}",
				"teach" => $"teach {name}",
				"teach-list" => $"teach/list {name}",
				"dolist-multiple" => $"@dolist/inline a b={name}",
				"map-multiple" => "@map/inline me/NESTEDBODY=a b",
				"include-multiple" => "@include/chain/nobreak me/NESTEDBODY me/NESTEDBODY",
				"include-invalid" => "@include/chain/nobreak me/NESTEDBODY invalid",
				"trigger" => "@trigger/inline me/NESTEDBODY",
				"verb" => "@verb me=me,,,,,NESTEDBODY",
				_ => callback
			};
			await Factory.Services.GetRequiredService<IAttributeService>().SetAttributeAsync(
				actor, actor, "NESTEDBODY", MarkupText.Plain(name));
			await Factory.Services.GetRequiredService<IAttributeService>().SetAttributeAsync(
				actor, actor, "CALLBACK", MarkupText.Plain(callback));
			await Command(player.Handle, "@input/start Answer:=done,me/CALLBACK,*,me/CALLBACK,120");
			var session = Sessions.GetCapturing(player.Handle);
			await Assert.That(session).IsNotNull();
			var result = await Sessions.DeliverAsync(parser, session!, MarkupText.Plain("reply"));
			await Assert.That(result).IsNotNull();
			// The callback is an action list, so a $-command it matches is queued as its own entry (#1132):
			// the body, and whatever fails in it, is not part of the callback.
			if (mode.StartsWith("nested-", StringComparison.Ordinal)) await Assert.That(invocations).IsEqualTo(0);
			if (mode is "ifelse-true" or "ifelse-false" or "skip" or "switch" or "select" or "force" or "break" or "assert" or "teach" or "teach-list" or "trigger" or "verb" or "include-invalid") await Assert.That(invocations).IsEqualTo(1);
			if (mode is "dolist-multiple" or "map-multiple" or "include-multiple") await Assert.That(invocations).IsEqualTo(2);
			await Assert.That(Sessions.GetCapturing(player.Handle) is null).IsEqualTo(failed);
			if (failed) await Assert.That(result!.HadErrors).IsTrue();
		}
		finally { await Connections.Disconnect(player.Handle); }
	}

	[Test]
	public async Task CallbackStoresLiteralPayloadAndCancelRestoresOrdinaryRouting()
	{
		var player = await Player();
		try
		{
			await Command(player.Handle, "&CALLBACK me=&ANSWER me=%0; &REASON me=%q<reason>");
			await Command(player.Handle, "@input/start Answer:=done,me/CALLBACK,*,me/CALLBACK,120");
			await Assert.That(Sessions.GetCapturing(player.Handle)).IsNotNull();
			const string payload = "[setq(unsafe,yes)];&ATTACK me=bad;%q<unsafe>\r\nnext line";
			await Assert.That((await Input(player.Handle, payload)).Accepted).IsTrue();
			await WaitFor(player.DbRef, "REASON", "input");
			await Assert.That(await Read(player.DbRef, "ANSWER")).IsEqualTo(payload);
			await Assert.That(await Read(player.DbRef, "ATTACK")).IsNull();
			await Assert.That(Sessions.GetCapturing(player.Handle)).IsNotNull();
			await Input(player.Handle, "@input/cancel");
			await Assert.That(Sessions.GetCapturing(player.Handle)).IsNull();
			await Input(player.Handle, "&ORDINARY me=restored");
			await WaitFor(player.DbRef, "ORDINARY", "restored");
		}
		finally { await Connections.Disconnect(player.Handle); }
	}

	/// <summary>
	/// A callback may set and read q-registers across its commands, and each reply starts with none: the
	/// register the first reply leaves behind is gone by the second.
	/// </summary>
	/// <remarks>
	/// Two attributes hold the observations rather than one each. Seven writes made this callback the
	/// slowest entry on the queue in a full suite run — 3 to 4 seconds, most of the barrier's own
	/// window — and the queue is shared by the whole session and runs one entry at a time, so the input
	/// tests behind it waited nearly as long. Two of them are still two commands, which is what shows a
	/// register set in one command being read in the next.
	/// </remarks>
	[Test]
	public async Task CallbackQRegistersAreUsableAndFreshForEveryReply()
	{
		var player = await Player();
		try
		{
			await Command(player.Handle, "&CALLBACK me=&SET me=[listq()]|[setq(LOCAL,%0)]|%q<LOCAL>|[setr(OTHER,%0)]|[sort(listq())]; &READ me=%q<LOCAL>|[unsetq()]|[listq()]; think setq(LEFTOVER,secret)");
			await Command(player.Handle, "@input/start Answer:=done,me/CALLBACK,*,me/CALLBACK,120");
			foreach (var reply in new[] { "first", "second" })
			{
				await Assert.That((await Input(player.Handle, reply)).Accepted).IsTrue();
				var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				await Scheduler.AdmitWork(() => { drained.SetResult(); return ValueTask.FromResult<CallState?>(null); }, "input-register-check", "test");
				await drained.Task.WaitAsync(QueueDeadline);
				await Assert.That(await Read(player.DbRef, "SET")).IsEqualTo($"REASON||{reply}|{reply}|LOCAL OTHER REASON")
					.Because("no register but REASON carries over from the previous reply, and each is readable as it is set");
				await Assert.That(await Read(player.DbRef, "READ")).IsEqualTo($"{reply}||")
					.Because("a register set in one command is readable in the next, and unsetq() empties the set");
				await Assert.That(Sessions.GetCapturing(player.Handle)).IsNotNull();
			}
		}
		finally { await Connections.Disconnect(player.Handle); }
	}

	[Test]
	public async Task PasteLinesRemainCapturedAndCallbackCanEndTheSession()
	{
		var player = await Player();
		try
		{
			await Command(player.Handle, "&COUNT me=0");
			await Command(player.Handle, "&CALLBACK me=&COUNT me=inc(get(me/COUNT)); &ANSWER me=%0");
			await Command(player.Handle, "@input Lines:=done,me/CALLBACK,*,me/CALLBACK,120");
			await Input(player.Handle, "first");
			await Input(player.Handle, "&ATTACK me=bad");
			await WaitFor(player.DbRef, "COUNT", "2");
			await WaitFor(player.DbRef, "ANSWER", "&ATTACK me=bad");
			await Assert.That(await Read(player.DbRef, "ATTACK")).IsNull();
			await Command(player.Handle, "&CALLBACK me=&ANSWER me=%0; @input/cancel");
			await Input(player.Handle, "last");
			await WaitFor(player.DbRef, "ANSWER", "last");
			using var deadline = new CancellationTokenSource(QueueDeadline);
			while (Sessions.GetCapturing(player.Handle) is not null) await Task.Delay(10, deadline.Token);
		}
		finally { await Connections.Disconnect(player.Handle); }
	}

	[Test]
	public async Task ExitLineEndsTheSessionAndRunsTheExitAttribute()
	{
		var player = await Player();
		try
		{
			await Command(player.Handle, "&CALLBACK me=&ANSWER me=%0; &REASON me=%q<reason>");
			await Command(player.Handle, "&FINISH me=&FINISHED me=%0|%q<reason>");
			await Command(player.Handle, "@input/start Answer:=.done,FINISH,*,CALLBACK,120");
			await Input(player.Handle, "first");
			await WaitFor(player.DbRef, "REASON", "input");
			await Input(player.Handle, "  .DONE ");
			await WaitFor(player.DbRef, "FINISHED", "  .DONE |exit");
			await Assert.That(await Read(player.DbRef, "ANSWER")).IsEqualTo("first")
				.Because("the exit line runs the exit attribute, not the one for other lines");
			await Assert.That(Sessions.GetCapturing(player.Handle)).IsNull()
				.Because("the session ends on the exit line, before the exit attribute runs");
			await Input(player.Handle, "&ORDINARY me=restored");
			await WaitFor(player.DbRef, "ORDINARY", "restored");
		}
		finally { await Connections.Disconnect(player.Handle); }
	}

	[Test]
	public async Task EachPatternRunsItsOwnAttributeAndUnmatchedLinesAreRefused()
	{
		var player = await Player();
		try
		{
			await Command(player.Handle, "&ON`HELP me=&SEEN me=[get(me/SEEN)]help:%0|");
			await Command(player.Handle, "&ON`QUIT me=&SEEN me=[get(me/SEEN)]quit:%q<reason>");
			var marker = TestIsolationHelpers.GenerateUniqueName("Leave");
			await Command(player.Handle, $"@input/start Keys:={marker},ON`QUIT,help,ON`HELP,120");
			await Input(player.Handle, "HELP");
			await WaitFor(player.DbRef, "SEEN", "help:HELP|");
			var before = Factory.Notifications.CountForHandle(player.Handle);
			await Input(player.Handle, "look");
			await Assert.That(() => Factory.Notifications.ForHandle(player.Handle).Skip(before))
				.WaitsFor(lines => lines.Contains((string line) => line.Contains(marker, StringComparison.Ordinal)),
					timeout: QueueDeadline, pollingInterval: TimeSpan.FromMilliseconds(20));
			await Assert.That(Sessions.GetCapturing(player.Handle)).IsNotNull()
				.Because("a line nothing matches tells the player the exit and keeps the session");
			await Input(player.Handle, marker);
			await WaitFor(player.DbRef, "SEEN", "help:HELP|quit:exit");
			await Assert.That(Sessions.GetCapturing(player.Handle)).IsNull();
		}
		finally { await Connections.Disconnect(player.Handle); }
	}

	[Test]
	[Arguments("/wild", "go *", "go north", "north|")]
	[Arguments("/regex", @"^go (\[a-z\]+)( fast)?$", "go north fast", "go north fast|north")]
	public async Task WildAndRegexPatternsPassTheirCaptures(string kind, string pattern, string line, string expected)
	{
		var player = await Player();
		try
		{
			await Command(player.Handle, "&ON`GO me=&WENT me=%0|%1");
			await Command(player.Handle, "&ON`QUIT me=think quit");
			await Command(player.Handle, $"@input/start{kind} Where?=quit,ON`QUIT,{pattern},ON`GO,120");
			await Assert.That(Sessions.GetCapturing(player.Handle)).IsNotNull();
			await Input(player.Handle, line);
			await WaitFor(player.DbRef, "WENT", expected);
		}
		finally { await Connections.Disconnect(player.Handle); }
	}

	[Test]
	[Arguments("@input/start Answer:", nameof(InputSessionService.MissingExit))]
	[Arguments("@input/start Answer:=done", nameof(InputSessionService.MissingExit))]
	[Arguments("@input/start Answer:=%b,CALLBACK,120", nameof(InputSessionService.MissingExit))]
	[Arguments("@input/start Answer:=done,CALLBACK,*", nameof(InputSessionService.UnpairedPattern))]
	[Arguments("@input/start Answer:=done,%b,120", nameof(InputSessionService.InvalidCallback))]
	[Arguments("@input/start Answer:=done,NOSUCHEXIT", nameof(InputSessionService.InvalidCallback))]
	[Arguments("@input/start Answer:=@INPUT/CANCEL,CALLBACK", nameof(InputSessionService.ReservedExit))]
	[Arguments("@input/start/regex Answer:=*a,CALLBACK", nameof(InputSessionService.InvalidPattern))]
	[Arguments("@input/start Answer:=done,CALLBACK,0", nameof(InputSessionService.InvalidTimeout))]
	public async Task StartWithoutAWayOutIsRefused(string command, string error)
	{
		var player = await Player();
		try
		{
			await Command(player.Handle, "&CALLBACK me=think unreachable");
			var result = await Parser.CommandParse(player.Handle, Connections, MarkupText.Plain(command));
			var expected = (string)typeof(InputSessionService).GetField(error)!.GetValue(null)!;
			await Assert.That(result.Message.ToPlainText()).IsEqualTo(expected);
			await Assert.That(Sessions.GetCapturing(player.Handle)).IsNull();
		}
		finally { await Connections.Disconnect(player.Handle); }
	}

	[Test]
	public async Task TimeoutWorkerEndsCaptureAndRunsItsAdmittedCallback()
	{
		var player = await Player();
		try
		{
			await Command(player.Handle, "&CALLBACK me=&REASON me=%q<reason>");
			await Command(player.Handle, "@input/start Answer:=done,me/CALLBACK,*,me/CALLBACK,1");
			await WaitFor(player.DbRef, "REASON", "timeout");
			await Assert.That(Sessions.GetCapturing(player.Handle)).IsNull();
		}
		finally { await Connections.Disconnect(player.Handle); }
	}

	[Test]
	public async Task RescueRunsTheTimeoutCallbackNow()
	{
		var player = await Player();
		try
		{
			await Command(player.Handle, "&CALLBACK me=&REASON me=%q<reason>");
			await Command(player.Handle, "@input/start Answer:=done,me/CALLBACK,*,me/CALLBACK,3600");
			await Assert.That(Sessions.GetCapturing(player.Handle)).IsNotNull();
			await Factory.Services.GetRequiredService<IMediator>().Send(new AdmitCommandListRequest(
				MarkupText.Plain($"@input/rescue {player.Name}; &RESCUED {player.DbRef}=%>"),
				Factory.CommandParserFor(new DBRef(1), 1).CurrentState, new DbRefAttribute(new DBRef(1), ["RESCUETEST"]), -1));
			await WaitFor(player.DbRef, "RESCUED", "1");
			await Assert.That(Sessions.GetCapturing(player.Handle)).IsNull()
				.Because("a rescued session stops capturing at once, before its callback runs");
			await WaitFor(player.DbRef, "REASON", "timeout");
		}
		finally { await Connections.Disconnect(player.Handle); }
	}

	[Test]
	public async Task RescueNeedsPlayersModerate()
	{
		var player = await Player();
		var other = await Player();
		try
		{
			await Command(player.Handle, "&CALLBACK me=&REASON me=%q<reason>");
			await Command(player.Handle, "@input/start Answer:=done,me/CALLBACK,*,me/CALLBACK,3600");
			await Command(other.Handle, $"@input/rescue {player.Name}");
			await Assert.That(Sessions.GetCapturing(player.Handle)).IsNotNull();
		}
		finally { await Connections.Disconnect(player.Handle); await Connections.Disconnect(other.Handle); }
	}

	[Test]
	public async Task CallbackTargetRequiresRealControlAndCharacterContext()
	{
		var player = await Player();
		var other = await Player();
		try
		{
			await Command(other.Handle, "&CALLBACK me=think forbidden");
			await Command(player.Handle, $"@input/start Answer:=done,{other.DbRef}/CALLBACK,*,{other.DbRef}/CALLBACK");
			await Assert.That(Sessions.GetCapturing(player.Handle)).IsNull();
			await Command(player.Handle, "&CALLBACK me=think allowed");
			await Command(player.Handle, "@input/start Answer:=done,me/CALLBACK,*,me/CALLBACK");
			await Assert.That(Sessions.GetCapturing(player.Handle)).IsNotNull();
		}
		finally { await Connections.Disconnect(player.Handle); await Connections.Disconnect(other.Handle); }
	}

	[Test]
	public async Task CallbacksKeepTheCallerOfTheStart()
	{
		var player = await Player();
		var command = "+" + TestIsolationHelpers.GenerateUniqueName("ask").ToLowerInvariant();
		try
		{
			var created = await Parser.CommandParse(player.Handle, Connections, MarkupText.Plain("@create CallerKeeper"));
			var keeper = DBRef.Parse(created.Message.Text);
			await Command(player.Handle, $"@set {keeper}=!no_command");
			await Command(player.Handle, $"&INPUT`ASK`LINE {keeper}=&LINE me=%@");
			await Command(player.Handle, $"&INPUT`ASK`DONE {keeper}=&DONE me=%@");
			await Command(player.Handle, $"&CMD`ASK {keeper}=${command}:@input/start Answer:=done,INPUT`ASK`DONE,*,INPUT`ASK`LINE");
			await Input(player.Handle, command);
			using (var deadline = new CancellationTokenSource(QueueDeadline))
				while (Sessions.GetCapturing(player.Handle) is null) await Task.Delay(10, deadline.Token);
			await Input(player.Handle, "hello");
			await WaitFor(keeper, "LINE", $"#{player.DbRef.Number}");
			await Input(player.Handle, "done");
			await WaitFor(keeper, "DONE", $"#{player.DbRef.Number}");
		}
		finally { await Connections.Disconnect(player.Handle); }
	}

	[Test]
	public async Task HaltingAnObjectStopsItsFutureCapturedCallbacks()
	{
		var player = await Player();
		try
		{
			var created = await Parser.CommandParse(player.Handle, Connections, MarkupText.Plain("@create GuidedInputCallback"));
			var target = DBRef.Parse(created.Message.Text);
			await Command(player.Handle, $"&CALLBACK {target}=&ANSWER me=%0");
			var callbackParser = Parser.FromState(ParserState.Empty with
			{
				Executor = target,
				Enactor = player.DbRef,
				Caller = player.DbRef,
				Handle = player.Handle
			});
			await callbackParser.CommandListParse(MarkupText.Plain("@input/start Answer:=done,me/CALLBACK,*,me/CALLBACK,120"));
			await Assert.That(Sessions.GetCapturing(player.Handle)).IsNotNull();
			await Command(player.Handle, $"@halt {target}");
			await Input(player.Handle, "must not run");
			using var deadline = new CancellationTokenSource(QueueDeadline);
			while (Sessions.GetCapturing(player.Handle) is not null) await Task.Delay(10, deadline.Token);
			await Assert.That(await Read(target, "ANSWER")).IsNull();
		}
		finally { await Connections.Disconnect(player.Handle); }
	}

	[Test]
	public async Task HelpAndCommandMetadataDescribeThePublicSurface()
	{
		var help = await Factory.Services.GetRequiredService<ITextFileService>().GetEntryAsync("help/sharpcmd", "@INPUT");
		await Assert.That(help).IsNotNull();
		await Assert.That(help!).Contains("@input/start");
		var attribute = typeof(SharpMUSH.Implementation.Commands.Commands).GetMethod("Input")!.GetCustomAttribute<SharpCommandAttribute>()!;
		await Assert.That(attribute.Name).IsEqualTo("@INPUT");
		await Assert.That(attribute.Switches.SequenceEqual(new[] { "START", "PROMPT", "CANCEL", "RESCUE", "WILD", "REGEX" })).IsTrue();
		await Assert.That(attribute.ParameterNames.SequenceEqual(new[] { "prompt", "exit-pattern", "exit-attribute", "pattern", "attribute", "timeout-seconds" })).IsTrue();
	}
}
