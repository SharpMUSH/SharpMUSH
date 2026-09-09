using System.Collections.Concurrent;
using System.Text;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using Quartz;
using SharpMUSH.Server.Consumers;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using Scheduler = SharpMUSH.Library.Services.TaskScheduler;
using NSubstitute;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.InputSessions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class InputSessionServiceTests
{
	private sealed class Clock : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
		public override DateTimeOffset GetUtcNow() => Now;
	}

	private sealed class Harness
	{
		public readonly Clock Time = new();
		public readonly Dictionary<int, SharpPlayer> Objects = [];
		public readonly IMediator Mediator = Substitute.For<IMediator>();
		public readonly INotifyService Notify = Substitute.For<INotifyService>();
		public readonly IAttributeService Attributes = Substitute.For<IAttributeService>();
		public readonly IPermissionService Permissions = Substitute.For<IPermissionService>();
		public readonly ConnectionService Connections = new(Substitute.For<IPublisher>());
		public readonly IMUSHCodeParser Parser = Substitute.For<IMUSHCodeParser>();
		public readonly List<ParserState> Deliveries = [];
		public readonly InputSessionService Sessions;
		public bool CanRead = true;
		public bool CanExecute = true;
		public bool CanControl = true;
		public SharpPlayer Owner => Objects[50];
		public SharpPlayer Actor => Objects[51];
		public SharpPlayer Target => Objects[52];
		public SharpPlayer Character => Objects[53];

		public Harness()
		{
			for (var number = 50; number <= 53; number++) Objects[number] = Player(number);
			foreach (var player in Objects.Values) player.Object.Owner = new(_ => Task.FromResult(Owner));
			Mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
			{
				var reference = call.Arg<GetObjectNodeQuery>().DBRef;
				return ValueTask.FromResult<AnyOptionalSharpObject>(Objects.TryGetValue(reference.Number, out var player)
					&& player.Object.DBRef.Matches(reference) ? player : new None());
			});
			Permissions.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>()).Returns(_ => CanControl);
			Attributes.GetAttributeAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<string>(),
				Arg.Any<IAttributeService.AttributeMode>(), Arg.Any<bool>()).Returns(call =>
			{
				var allowed = call.Arg<IAttributeService.AttributeMode>() == IAttributeService.AttributeMode.Read ? CanRead : CanExecute;
				return ValueTask.FromResult<OptionalSharpAttributeOrError>(allowed
					? new SharpAttribute[] { new("id", "CALLBACK", "CALLBACK", [], null, "CALLBACK", null!, null!, null!) { Value = MarkupText.Plain("think %0") } }
					: new None());
			});
			Parser.FromState(Arg.Any<ParserState>()).Returns(call => { Deliveries.Add(call.Arg<ParserState>()); return Parser; });
			Parser.CommandListParse(Arg.Any<MarkupText>()).Returns(ValueTask.FromResult<CallState?>(CallState.Empty));
			Sessions = new(Connections, Mediator, Attributes, Permissions, Notify, Time);
		}

		public async Task<IMUSHCodeParser> Connect(long handle = 1, string session = "transport", long start = 100)
		{
			await Connections.Register(handle, "127.0.0.1", "localhost", "telnet", _ => ValueTask.CompletedTask,
				_ => ValueTask.CompletedTask, () => Encoding.UTF8, new ConcurrentDictionary<string, string>(new Dictionary<string, string>
				{
					["SessionId"] = session, ["ConnectionStartTime"] = start.ToString(), ["LastConnectionSignal"] = start.ToString(), ["ConnectionType"] = "telnet"
				}));
			if (Connections.Get(handle)!.State != IConnectionService.ConnectionState.LoggedIn)
				await Connections.Bind(handle, Character.Object.DBRef);
			var caller = Substitute.For<IMUSHCodeParser>();
			caller.CurrentState.Returns(ParserState.Empty with
			{
				Handle = handle,
				ConnectionSessionId = session,
				Executor = Actor.Object.DBRef,
				Enactor = Character.Object.DBRef
			});
			return caller;
		}

		public async Task<InputSession> Start(long handle = 1)
		{
			var caller = await Connect(handle);
			await Assert.That(await Sessions.StartAsync(caller, Target.Object.DBRef, "CALLBACK", MarkupText.Plain("Answer: "), TimeSpan.FromSeconds(60))).IsNull();
			return Sessions.GetCapturing(handle)!;
		}

		private static SharpPlayer Player(int number) => new()
		{
			Object = new SharpObject
			{
				Key = number, CreationTime = 1000, Name = "Session test", Type = "PLAYER", Locks = null!, Owner = null!,
				Powers = new(AsyncEnumerable.Empty<SharpPower>), Attributes = null!, LazyAttributes = null!, AllAttributes = null!, LazyAllAttributes = null!,
				Flags = new(AsyncEnumerable.Empty<SharpObjectFlag>), Parent = null!, Zone = null!, Children = null!
			},
			Location = null!, Home = null!, PasswordHash = "", Quota = 0
		};
	}

	[Test]
	[Arguments("[setq(x,evil)];@destroy me;%q<x>\r\nnext line")]
	[Arguments("")]
	[Arguments("  \t  ")]
	public async Task InputIsLiteralAndSessionPersists(string input)
	{
		var h = new Harness(); var session = await h.Start();
		await h.Sessions.DeliverAsync(h.Parser, session, MarkupText.Plain(input));
		await Assert.That(h.Deliveries.Single().EnvironmentRegisters["0"].Message!.Text).IsEqualTo(input);
		await Assert.That(h.Deliveries.Single().EnvironmentRegisters["1"].Message!.Text).IsEqualTo("input");
		await Assert.That(h.Deliveries.Single().Executor).IsEqualTo(h.Actor.Object.DBRef);
		await Assert.That(h.Deliveries.Single().Enactor).IsEqualTo(h.Character.Object.DBRef);
		await Assert.That(h.Deliveries.Single().CurrentEvaluation).IsEqualTo(new DBAttribute(h.Target.Object.DBRef, "CALLBACK"));
		await Assert.That(h.Sessions.GetCapturing(1)?.Id).IsEqualTo(session.Id);
	}

	[Test]
	public async Task TwoConnectionsAndReplacementKeepGenerationsSeparate()
	{
		var h = new Harness(); var first = await h.Start(); var second = await h.Start(2);
		await h.Sessions.DeliverAsync(h.Parser, second, MarkupText.Plain("two"));
		await Assert.That(h.Deliveries.Single().Handle).IsEqualTo(2L);
		var caller = await h.Connect();
		await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "CALLBACK", MarkupText.Empty, TimeSpan.FromSeconds(60));
		await h.Sessions.DeliverAsync(h.Parser, first, MarkupText.Plain("stale"));
		await Assert.That(h.Deliveries.Count).IsEqualTo(1);
	}

	[Test]
	public async Task EscapeIsExactAndRequiresTheCurrentTransport()
	{
		var h = new Harness(); var session = await h.Start();
		await Assert.That(await h.Sessions.TryEscapeAsync(1, "old", MarkupText.Plain("@input/cancel"))).IsFalse();
		await Assert.That(await h.Sessions.TryEscapeAsync(1, null, MarkupText.Plain("@input/cancel"))).IsFalse();
		await Assert.That(await h.Sessions.TryEscapeAsync(1, "transport", MarkupText.Plain("@input/cancel;think unsafe"))).IsFalse();
		await Assert.That(await h.Sessions.TryEscapeAsync(1, "transport", MarkupText.Plain("@INPUT/CANCEL"))).IsTrue();
		await h.Sessions.DeliverAsync(h.Parser, session, MarkupText.Plain("stale"));
		await Assert.That(h.Deliveries.Count).IsEqualTo(0);
		await Assert.That(h.Sessions.GetCapturing(1)).IsNull();
	}

	[Test]
	[Arguments("read")]
	[Arguments("execute")]
	[Arguments("control")]
	[Arguments("owner")]
	[Arguments("target")]
	[Arguments("character")]
	public async Task RevocationOrRecycledIdentityEndsCapture(string change)
	{
		var h = new Harness(); var session = await h.Start();
		switch (change)
		{
			case "read": h.CanRead = false; break;
			case "execute": h.CanExecute = false; break;
			case "control": h.CanControl = false; break;
			case "owner": h.Target.Object.Owner = new(_ => Task.FromResult(h.Character)); break;
			case "target": h.Target.Object.CreationTime++; break;
			case "character": h.Character.Object.CreationTime++; break;
		}
		await h.Sessions.DeliverAsync(h.Parser, session, MarkupText.Plain("ignored"));
		await Assert.That(h.Deliveries.Count).IsEqualTo(0);
		await Assert.That(h.Sessions.GetCapturing(1)).IsNull();
	}

	private static void Halt(SharpObject obj) => obj.Flags = new(() => new[]
	{
		new SharpObjectFlag { Name = "HALT", Symbol = "h", SetPermissions = [], UnsetPermissions = [], System = true, TypeRestrictions = [] }
	}.ToAsyncEnumerable());

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task HaltedExecutorCannotReceiveInputOrTimeoutCallbacks(bool timeout)
	{
		var h = new Harness(); var session = await h.Start();
		Halt(h.Actor.Object);
		if (timeout) { h.Time.Now += TimeSpan.FromMinutes(2); h.Sessions.TakeExpired(); }
		await h.Sessions.DeliverAsync(h.Parser, session, MarkupText.Plain("ignored"), timeout);
		await Assert.That(h.Deliveries.Count).IsEqualTo(0);
		await Assert.That(h.Sessions.GetCapturing(1)).IsNull();
	}

	[Test]
	public async Task HaltedExecutorCannotStartCapture()
	{
		var h = new Harness(); var caller = await h.Connect();
		Halt(h.Actor.Object);
		await Assert.That(await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "CALLBACK", MarkupText.Empty, TimeSpan.FromSeconds(60))).IsNotNull();
		await Assert.That(h.Sessions.GetCapturing(1)).IsNull();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task LateEscapeCannotCancelExpiredTimeoutCallback(bool alreadyPending)
	{
		var h = new Harness(); var session = await h.Start();
		h.Time.Now += TimeSpan.FromMinutes(2);
		if (alreadyPending) h.Sessions.TakeExpired();
		await Assert.That(await h.Sessions.TryEscapeAsync(1, "transport", MarkupText.Plain("@input/cancel"))).IsFalse();
		if (!alreadyPending) h.Sessions.TakeExpired();
		await h.Sessions.DeliverAsync(h.Parser, session, MarkupText.Empty, timeout: true);
		await Assert.That(h.Deliveries.Single().EnvironmentRegisters["1"].Message!.Text).IsEqualTo("timeout");
	}

	[Test]
	public async Task DisconnectSwitchAndTimeoutRemoveCapture()
	{
		var h = new Harness(); var first = await h.Start();
		await h.Connections.Unbind(1);
		await Assert.That(h.Sessions.GetCapturing(1)).IsNull();
		await h.Sessions.DeliverAsync(h.Parser, first, MarkupText.Plain("ignored"));
		var second = await h.Start(2);
		await h.Connections.Bind(2, h.Owner.Object.DBRef);
		await Assert.That(h.Sessions.GetCapturing(2)).IsNull();
		var third = await h.Start(3);
		h.Time.Now += TimeSpan.FromSeconds(61);
		await Assert.That(h.Sessions.GetCapturing(3)).IsNull();
		var expired = h.Sessions.TakeExpired();
		await Assert.That(expired.Count).IsEqualTo(1);
		await Assert.That(h.Sessions.TakeExpired().Count).IsEqualTo(0);
		await h.Sessions.DeliverAsync(h.Parser, third, MarkupText.Empty, timeout: true);
		await Assert.That(h.Deliveries.Single().EnvironmentRegisters["1"].Message!.Text).IsEqualTo("timeout");
		await h.Sessions.DeliverAsync(h.Parser, third, MarkupText.Empty, timeout: true);
		await Assert.That(h.Deliveries.Count).IsEqualTo(1);
	}

	[Test]
	public async Task ExcessiveInputIsConsumedWithoutDispatchingAndOwnerSessionsAreBounded()
	{
		var h = new Harness(); var session = await h.Start();
		await h.Sessions.DeliverAsync(h.Parser, session, MarkupText.Plain(new string('x', InputSessionService.MaxInputCodeUnits + 1)));
		await Assert.That(h.Deliveries.Count).IsEqualTo(0);
		await Assert.That(h.Sessions.GetCapturing(1)).IsNotNull();
		for (var handle = 2; handle <= InputSessionService.MaxOwnerSessions; handle++) await h.Start(handle);
		var caller = await h.Connect(1000);
		await Assert.That(await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "CALLBACK", MarkupText.Empty, TimeSpan.FromSeconds(60)))
			.IsEqualTo(InputSessionService.SessionLimit);
	}
	[Test]
	[Arguments(0)]
	[Arguments(3601)]
	[Arguments(-1)]
	public async Task InvalidTimeoutCannotStartCapture(int seconds)
	{
		var h = new Harness(); var caller = await h.Connect();
		await Assert.That(await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "CALLBACK", MarkupText.Empty, TimeSpan.FromSeconds(seconds)))
			.IsEqualTo(InputSessionService.InvalidTimeout);
		await Assert.That(h.Sessions.GetCapturing(1)).IsNull();
	}

	[Test]
	[Arguments("enactor")]
	[Arguments("transport")]
	[Arguments("handle")]
	public async Task InvalidCharacterOrConnectionContextCannotStartCapture(string invalid)
	{
		var h = new Harness(); var caller = await h.Connect();
		caller.CurrentState.Returns(invalid switch
		{
			"enactor" => caller.CurrentState with { Enactor = h.Owner.Object.DBRef },
			"transport" => caller.CurrentState with { ConnectionSessionId = "old" },
			_ => caller.CurrentState with { Handle = null }
		});
		await Assert.That(await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "CALLBACK", MarkupText.Empty, TimeSpan.FromSeconds(60)))
			.IsEqualTo(InputSessionService.InvalidContext);
		await Assert.That(h.Sessions.GetCapturing(1)).IsNull();
	}

	[Test]
	public async Task PromptFailureAndUnhandledCallbackFailureEndCapture()
	{
		var h = new Harness();
		var caller = await h.Connect();
		h.Notify.Prompt(Arg.Any<long>(), Arg.Any<OneOf.OneOf<MarkupText, string>>()).Returns(_ => throw new InvalidOperationException("prompt failed"));
		try { await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "CALLBACK", MarkupText.Empty, TimeSpan.FromSeconds(60)); }
		catch (InvalidOperationException) { }
		await Assert.That(h.Sessions.GetCapturing(1)).IsNull();
		h = new Harness();
		var session = await h.Start();
		h.Parser.CommandListParse(Arg.Any<MarkupText>()).Returns(ValueTask.FromException<CallState?>(new InvalidOperationException("callback failed")));
		try { await h.Sessions.DeliverAsync(h.Parser, session, MarkupText.Empty); }
		catch (InvalidOperationException) { }
		await Assert.That(h.Sessions.GetCapturing(1)).IsNull();
	}

	[Test]
	public async Task ExpiredExecutionBudgetEndsCaptureBeforeCallback()
	{
		var h = new Harness(); var session = await h.Start();
		using var budget = new ExecutionBudget(TimeSpan.Zero);
		using var scope = budget.Enter();
		try { await h.Sessions.DeliverAsync(h.Parser, session, MarkupText.Empty); }
		catch (OperationCanceledException) { }
		await Assert.That(h.Sessions.GetCapturing(1)).IsNull();
		await Assert.That(h.Deliveries.Count).IsEqualTo(0);
	}

	[Test]
	public async Task ExpiredCallbackRechecksAuthorityBeforeDelivery()
	{
		var h = new Harness(); var session = await h.Start();
		h.Time.Now += TimeSpan.FromMinutes(2);
		h.Sessions.TakeExpired();
		h.CanExecute = false;
		await h.Sessions.DeliverAsync(h.Parser, session, MarkupText.Empty, timeout: true);
		await Assert.That(h.Deliveries.Count).IsEqualTo(0);
		await Assert.That(h.Sessions.TakeExpired().Count).IsEqualTo(0);
	}

	[Test]
	[Arguments("")]
	[Arguments("  \t ")]
	[Arguments("[setq(x,bad)];@destroy me\r\nsecond")]
	public async Task BothTransportConsumersPreserveCapturedPayloadsAndFenceOldConnections(string payload)
	{
		var h = new Harness(); await h.Start();
		var queue = Substitute.For<ITaskScheduler>();
		var telnet = new TelnetInputConsumer(NullLogger<TelnetInputConsumer>.Instance, queue, h.Connections, h.Sessions);
		var websocket = new WebSocketInputConsumer(NullLogger<WebSocketInputConsumer>.Instance, queue, h.Connections, h.Sessions);
		await telnet.HandleAsync(new TelnetInputMessage(1, payload, "transport"));
		await websocket.HandleAsync(new WebSocketInputMessage(1, payload, "transport"));
		await queue.Received(2).WriteUserCommand(1, Arg.Is<MarkupText>(value => value.Text == payload),
			Arg.Is<ParserState>(state => state.ConnectionSessionId == "transport"));
		queue.ClearReceivedCalls();
		await telnet.HandleAsync(new TelnetInputMessage(1, payload, "old"));
		await websocket.HandleAsync(new WebSocketInputMessage(1, payload, "old"));
		await queue.DidNotReceive().WriteUserCommand(Arg.Any<long>(), Arg.Any<MarkupText>(), Arg.Any<ParserState>());
	}

	private static Scheduler Queue(Harness h, IInputSessionService? sessions = null, uint global = 10)
	{
		var config = ReadPennMushConfig.Create(Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst"));
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(config with { Limit = config.Limit with { GlobalQueueLimit = global, PlayerQueueLimit = 100, QueueEntryCpuTime = 1000 } });
		return new(h.Parser, h.Connections, Substitute.For<ISchedulerFactory>(), h.Attributes, h.Mediator,
			NullLogger<Scheduler>.Instance, options, h.Notify, sessions ?? h.Sessions);
	}

	private static async Task Drained(Scheduler queue)
	{
		using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		while (queue.GetQueueUsage().Total != 0) await Task.Delay(10, deadline.Token);
	}

	[Test]
	public async Task EscapeBypassesSaturatedAdmissionAndStaleQueuedInputIsDiscarded()
	{
		var h = new Harness(); await h.Start();
		await using var queue = Queue(h, global: 2);
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		await queue.EnqueueWork(async () => { entered.SetResult(); await release.Task; return null; }, "block", "test");
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			var state = ParserState.Empty with { Handle = 1, ConnectionSessionId = "transport" };
			await Assert.That((await queue.WriteUserCommand(1, MarkupText.Plain("[dangerous()];@destroy me"), state)).Accepted).IsTrue();
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(2);
			await Assert.That((await queue.WriteUserCommand(1, MarkupText.Plain("@input/cancel"), state)).Accepted).IsTrue();
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(2);
		}
		finally { release.TrySetResult(); }
		await Drained(queue);
		await Assert.That(h.Deliveries.Count).IsEqualTo(0);
		await h.Parser.DidNotReceive().CommandParse(Arg.Any<long>(), Arg.Any<IConnectionService>(), Arg.Any<MarkupText>());
	}

	[Test]
	public async Task MissingTransportIncarnationCannotDeliverToModernCapture()
	{
		var h = new Harness(); await h.Start();
		await using var queue = Queue(h);
		await queue.WriteUserCommand(1, MarkupText.Plain("stale"), ParserState.Empty with { Handle = 1 });
		await Drained(queue);
		await Assert.That(h.Deliveries.Count).IsEqualTo(0);
		await h.Parser.DidNotReceive().CommandParse(Arg.Any<long>(), Arg.Any<IConnectionService>(), Arg.Any<MarkupText>());
		await Assert.That(h.Sessions.GetCapturing(1)).IsNotNull();
	}

	[Test]
	public async Task CapturedInputUsesTheExistingExecutionBudget()
	{
		var h = new Harness(); await h.Start();
		var budgetPresent = false;
		h.Parser.CommandListParse(Arg.Any<MarkupText>()).Returns(_ =>
		{
			budgetPresent = ExecutionBudget.Current is not null;
			return ValueTask.FromResult<CallState?>(CallState.Empty);
		});
		await using var queue = Queue(h);
		await queue.WriteUserCommand(1, MarkupText.Plain("literal"), ParserState.Empty with { ConnectionSessionId = "transport" });
		await Drained(queue);
		await Assert.That(budgetPresent).IsTrue();
		await Assert.That(h.Deliveries.Single().EnvironmentRegisters["0"].Message!.Text).IsEqualTo("literal");
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task HaltAndShutdownReleaseTimeoutRecordsExactlyOnceOutsideAdmissionLock(bool shutdown)
	{
		var h = new Harness(); var session = await h.Start();
		var captures = Substitute.For<IInputSessionService>();
		var queue = Queue(h, captures);
		var cleanupCouldReadQueue = false;
		var cleanupFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		captures.When(service => service.Discard(session)).Do(_ =>
		{
			// A dedicated thread tests the lock boundary without depending on thread-pool availability.
			var probe = new Thread(() => queue.GetQueueUsage()) { IsBackground = true };
			probe.Start();
			cleanupCouldReadQueue = probe.Join(TimeSpan.FromSeconds(5));
			cleanupFinished.TrySetResult();
		});
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var disposed = false;
		try
		{
			await queue.EnqueueWork(async () =>
			{
				entered.SetResult();
				await release.Task.WaitAsync(ExecutionBudget.CurrentToken);
				return null;
			}, "block", "test");
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			var timeout = await queue.WriteInputSessionTimeout(session);
			await Assert.That(timeout.Accepted).IsTrue();
			if (shutdown) { await queue.DisposeAsync(); disposed = true; }
			else
			{
				await queue.HaltByPid(timeout.Pid!.Value);
				release.TrySetResult();
				await Drained(queue);
			}
			// Ledger removal precedes the out-of-lock callback; an empty queue alone is not completion.
			await cleanupFinished.Task.WaitAsync(TimeSpan.FromSeconds(10));
			captures.Received(1).Discard(session);
			await captures.DidNotReceive().DeliverAsync(Arg.Any<IMUSHCodeParser>(), Arg.Any<InputSession>(), Arg.Any<MarkupText>(), Arg.Any<bool>());
			await Assert.That(cleanupCouldReadQueue).IsTrue();
		}
		finally
		{
			release.TrySetResult();
			if (!disposed) await queue.DisposeAsync();
		}
	}

}
