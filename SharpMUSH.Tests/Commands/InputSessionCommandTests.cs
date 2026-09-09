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
