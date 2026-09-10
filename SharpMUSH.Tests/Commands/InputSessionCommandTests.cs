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
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class InputSessionCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private IInputSessionService Sessions => Factory.Services.GetRequiredService<IInputSessionService>();
	private ITaskScheduler Scheduler => Factory.Services.GetRequiredService<ITaskScheduler>();
	private IMUSHCodeParser Parser => Factory.Services.GetRequiredService<IMUSHCodeParser>();
	private ISharpDatabase Database => Factory.Services.GetRequiredService<ISharpDatabase>();

	private Task<TestIsolationHelpers.TestPlayer> Player() => TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
		Factory.Services, Factory.Services.GetRequiredService<IMediator>(), Connections, "InputSession");
	private async Task<string?> Read(DBRef player, string name) =>
		(await Database.GetAttributeAsync(player, [name], CancellationToken.None).LastOrDefaultAsync())?.Value.Text;
	private Task Command(long handle, string command) => Parser.CommandParse(handle, Connections, MarkupText.Plain(command)).AsTask();
	private ValueTask<SharpMUSH.Library.Models.SchedulerModels.QueueAdmissionResult> Input(long handle, string input) =>
		Scheduler.AdmitUserCommand(handle, MarkupText.Plain(input), ParserState.Empty with { Handle = handle });

	private async Task WaitFor(DBRef player, string attribute, string expected)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
		while (await Read(player, attribute) != expected) await Task.Delay(20, timeout.Token);
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
	[Arguments("nested-syntax", true)]
	[Arguments("nested-throw", true)]
	[Arguments("nested-multiple", true)]
	[Arguments("speech", true)]
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
				if (mode == "literal") return ValueTask.FromResult<SharpMUSH.Library.DiscriminatedUnions.Option<CallState>>(new CallState("#-1 EXCEPTION: ordinary text"));
				if (mode == "nested-multiple" && invocations > 1) return ValueTask.FromResult<SharpMUSH.Library.DiscriminatedUnions.Option<CallState>>(CallState.Empty);
				throw new InvalidOperationException("input callback failed");
			}), true));
		if (mode == "speech") commands["SAY"] = (new SharpMUSH.Library.Definitions.CommandDefinition(
			commands["SAY"].LibraryInformation.Attribute, _ => throw new InvalidOperationException("speech callback failed")), true);
		IEnumerable<(AnySharpObject Obj, SharpAttribute Attr, Dictionary<string, CallState> Arguments)> matches = [];
		var discovery = Substitute.For<ICommandDiscoveryService>();
		discovery.MatchUserDefinedCommand(Arg.Any<IMUSHCodeParser>(), Arg.Any<IAsyncEnumerable<AnySharpObject>>(), Arg.Any<MarkupText>())
			.Returns(_ => ValueTask.FromResult(Option<IEnumerable<(AnySharpObject, SharpAttribute, Dictionary<string, CallState>)>>.FromOption(matches)));
		var provider = Substitute.For<IServiceProvider>();
		provider.GetService(Arg.Any<Type>()).Returns(call => call.Arg<Type>() == typeof(ICommandDiscoveryService) && mode.StartsWith("nested-", StringComparison.Ordinal)
			? discovery : Factory.Services.GetService(call.Arg<Type>()));
		var parser = new SharpMUSH.Implementation.MUSHCodeParser(original.Logger, original.FunctionLibrary,
			commands, original.Configuration, provider);
		try
		{
			var actor = await Factory.Services.GetRequiredService<IMediator>().Send(
				new SharpMUSH.Library.Queries.Database.GetObjectNodeQuery(player.DbRef));
			var callback = mode == "syntax" ? "think [" : mode == "throw-list" ? name + "; think done" : mode == "speech" ? "\"hello" : name;
			if (mode.StartsWith("nested-", StringComparison.Ordinal))
			{
				callback = "inputnested" + Guid.NewGuid().ToString("N");
				var body = mode == "nested-syntax" ? "think [" : name;
				var attribute = new SharpAttribute("nested", "NESTED", "NESTED", [], 0, "NESTED", null!, null!, null!)
				{ Value = MarkupText.Plain(body) };
				matches = Enumerable.Range(0, mode == "nested-multiple" ? 2 : 1)
					.Select(_ => (actor.Known(), attribute, new Dictionary<string, CallState>()));
			}
			await Factory.Services.GetRequiredService<IAttributeService>().SetAttributeAsync(
				actor.Known(), actor.Known(), "CALLBACK", MarkupText.Plain(callback));
			await Command(player.Handle, "@input/start me/CALLBACK=Answer:,120");
			var session = Sessions.GetCapturing(player.Handle);
			await Assert.That(session).IsNotNull();
			var result = await Sessions.DeliverAsync(parser, session!, MarkupText.Plain("reply"));
			await Assert.That(result).IsNotNull();
			if (mode == "nested-throw" || mode == "nested-multiple") await Assert.That(invocations).IsEqualTo(mode == "nested-multiple" ? 2 : 1);
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
			await Command(player.Handle, "&CALLBACK me=&ANSWER me=%0; &REASON me=%1");
			await Command(player.Handle, "@input/start me/CALLBACK=Answer:,120");
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

	[Test]
	public async Task CallbackQRegistersAreUsableAndFreshForEveryReply()
	{
		var player = await Player();
		try
		{
			await Command(player.Handle, "&CALLBACK me=&BEFORE me=listq(); &SET me=setq(LOCAL,%0); &VALUE me=%q<LOCAL>; &RETURN me=setr(OTHER,%0); &KEYS me=sort(listq()); &CLEAR me=unsetq(); &AFTER me=listq(); think setq(LEFTOVER,secret)");
			await Command(player.Handle, "@input/start me/CALLBACK=Answer:,120");
			foreach (var reply in new[] { "first", "second" })
			{
				await Assert.That((await Input(player.Handle, reply)).Accepted).IsTrue();
				var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				await Scheduler.EnqueueWork(() => { drained.SetResult(); return ValueTask.FromResult<CallState?>(null); }, "input-register-check", "test");
				await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
				await Assert.That(await Read(player.DbRef, "VALUE")).IsEqualTo(reply);
				await Assert.That(await Read(player.DbRef, "RETURN")).IsEqualTo(reply);
				await Assert.That(await Read(player.DbRef, "KEYS")).IsEqualTo("LOCAL OTHER");
				await Assert.That(await Read(player.DbRef, "BEFORE") ?? "").IsEqualTo("");
				await Assert.That(await Read(player.DbRef, "SET") ?? "").IsEqualTo("");
				await Assert.That(await Read(player.DbRef, "AFTER") ?? "").IsEqualTo("");
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
			await Command(player.Handle, "@input me/CALLBACK=Lines:,120");
			await Input(player.Handle, "first");
			await Input(player.Handle, "&ATTACK me=bad");
			await WaitFor(player.DbRef, "COUNT", "2");
			await WaitFor(player.DbRef, "ANSWER", "&ATTACK me=bad");
			await Assert.That(await Read(player.DbRef, "ATTACK")).IsNull();
			await Command(player.Handle, "&CALLBACK me=&ANSWER me=%0; @input/cancel");
			await Input(player.Handle, "last");
			await WaitFor(player.DbRef, "ANSWER", "last");
			using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
			while (Sessions.GetCapturing(player.Handle) is not null) await Task.Delay(10, deadline.Token);
		}
		finally { await Connections.Disconnect(player.Handle); }
	}

	[Test]
	public async Task TimeoutWorkerEndsCaptureAndRunsItsAdmittedCallback()
	{
		var player = await Player();
		try
		{
			await Command(player.Handle, "&CALLBACK me=&REASON me=%1");
			await Command(player.Handle, "@input/start me/CALLBACK=Answer:,1");
			await WaitFor(player.DbRef, "REASON", "timeout");
			await Assert.That(Sessions.GetCapturing(player.Handle)).IsNull();
		}
		finally { await Connections.Disconnect(player.Handle); }
	}

	[Test]
	public async Task CallbackTargetRequiresRealControlAndCharacterContext()
	{
		var player = await Player();
		var other = await Player();
		try
		{
			await Command(other.Handle, "&CALLBACK me=think forbidden");
			await Command(player.Handle, $"@input/start {other.DbRef}/CALLBACK=Answer:");
			await Assert.That(Sessions.GetCapturing(player.Handle)).IsNull();
			await Command(player.Handle, "&CALLBACK me=think allowed");
			await Command(player.Handle, "@input/start me/CALLBACK=Answer:");
			await Assert.That(Sessions.GetCapturing(player.Handle)).IsNotNull();
		}
		finally { await Connections.Disconnect(player.Handle); await Connections.Disconnect(other.Handle); }
	}

	[Test]
	public async Task HaltingAnObjectStopsItsFutureCapturedCallbacks()
	{
		var player = await Player();
		try
		{
			var created = await Parser.CommandParse(player.Handle, Connections, MarkupText.Plain("@create GuidedInputCallback"));
			var target = DBRef.Parse(created.Message!.Text);
			await Command(player.Handle, $"&CALLBACK {target}=&ANSWER me=%0");
			var callbackParser = Parser.FromState(ParserState.Empty with
			{
				Executor = target,
				Enactor = player.DbRef,
				Caller = player.DbRef,
				Handle = player.Handle
			});
			await callbackParser.CommandListParse(MarkupText.Plain("@input/start me/CALLBACK=Answer:,120"));
			await Assert.That(Sessions.GetCapturing(player.Handle)).IsNotNull();
			await Command(player.Handle, $"@halt {target}");
			await Input(player.Handle, "must not run");
			using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
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
		await Assert.That(attribute.Switches.SequenceEqual(new[] { "START", "PROMPT", "CANCEL" })).IsTrue();
		await Assert.That(attribute.ParameterNames.SequenceEqual(new[] { "object/attribute", "prompt", "timeout-seconds" })).IsTrue();
	}
}
