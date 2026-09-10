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
	[Arguments(false)]
	[Arguments(true)]
	public async Task RestartCannotEraseAnOwedTimeoutCallback(bool timeoutAlreadyTaken)
	{
		var h = new Harness();
		var caller = await h.Connect();
		await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "CALLBACK", MarkupText.Empty, TimeSpan.FromSeconds(60));
		var original = h.Sessions.GetCapturing(1)!;
		h.Time.Now += TimeSpan.FromMinutes(2);
		if (timeoutAlreadyTaken) await Assert.That(h.Sessions.TakeExpired().Single().Id).IsEqualTo(original.Id);
		await Assert.That(h.Sessions.GetCapturing(1)).IsNull();
		var restarted = await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "REPLACEMENT", MarkupText.Empty, TimeSpan.FromSeconds(60));
		await Assert.That(restarted).IsNotNull();
		if (!timeoutAlreadyTaken) await Assert.That(h.Sessions.TakeExpired().Single().Id).IsEqualTo(original.Id);
		await h.Sessions.DeliverAsync(h.Parser, original, MarkupText.Empty, true);
		await Assert.That(h.Deliveries.Count).IsEqualTo(1);
		await Assert.That(h.Deliveries.Single().EnvironmentRegisters["1"].Message!.ToPlainText()).IsEqualTo("timeout");
		await Assert.That(await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "REPLACEMENT", MarkupText.Empty, TimeSpan.FromSeconds(60))).IsNull();
		await Assert.That(h.Sessions.GetCapturing(1)!.Id).IsNotEqualTo(original.Id);
	}

	[Test]
	[Arguments("object")]
	[Arguments("actor-owner")]
	[Arguments("target-owner")]
	[Arguments("control")]
	[Arguments("flag")]
	public async Task CallbackRevalidationStopsWhenExecutionIsCancelled(string boundary)
	{
		var h = new Harness();
		var session = await h.Start();
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		async ValueTask<AnyOptionalSharpObject> ReadObject(CancellationToken token)
		{
			entered.TrySetResult();
			await release.Task.WaitAsync(token);
			return h.Actor;
		}
		async ValueTask<bool> ReadControl()
		{ entered.TrySetResult(); await release.Task; return true; }
		CancellationToken flagToken = default;
		async IAsyncEnumerable<SharpObjectFlag> ReadFlags([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token = default)
		{
			flagToken = token;
			entered.TrySetResult();
			await release.Task.WaitAsync(token);
			yield break;
		}
		if (boundary == "object")
			h.Mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(call => ReadObject(call.Arg<CancellationToken>()));
		else if (boundary == "control")
			h.Permissions.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>()).Returns(_ => ReadControl());
		else if (boundary == "flag") h.Actor.Object.Flags = new(() => ReadFlags());
		else
			(boundary == "actor-owner" ? h.Actor : h.Target).Object.Owner = new(async token =>
			{ entered.TrySetResult(); await release.Task.WaitAsync(token); return h.Owner; });
		using var caller = new CancellationTokenSource();
		using var budget = ExecutionBudget.FromMilliseconds(0, caller.Token);
		using var scope = budget.Enter();
		var delivery = h.Sessions.DeliverAsync(h.Parser, session, MarkupText.Plain("literal")).AsTask();
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
		caller.Cancel();
		try
		{
			await Assert.That(async () => await delivery.WaitAsync(TimeSpan.FromSeconds(2))).Throws<OperationCanceledException>();
			await Assert.That(h.Sessions.GetCapturing(1)).IsNull();
			await Assert.That(h.Deliveries.Count).IsEqualTo(0);
			if (boundary == "flag") await Assert.That(flagToken.IsCancellationRequested).IsTrue();
		}
		finally
		{
			release.TrySetResult();
			try { await delivery; } catch (OperationCanceledException) { }
		}
	}

	[Test]
	[Arguments("cancel")]
	[Arguments("escape")]
	[Arguments("too-large")]
	[Arguments("revoked")]
	[Arguments("timeout-rejected")]
	public async Task SessionStatusPublicationCarriesCapturedTransportIdentity(string action)
	{
		var h = new Harness();
		var caller = await h.Connect();
		var bus = Substitute.For<SharpMUSH.Messaging.Abstractions.IMessageBus>();
		var publication = new TaskCompletionSource<MarkupOutputMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
		bus.HandlePublish(Arg.Any<MarkupOutputMessage>(), Arg.Any<CancellationToken>()).Returns(call =>
		{ publication.TrySetResult(call.Arg<MarkupOutputMessage>()); return Task.CompletedTask; });
		var localization = Substitute.For<ILocalizationService>();
		localization.Format(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<object[]>()).Returns(call => call.ArgAt<string>(0));
		var notify = new NotifyService(bus, h.Connections, localization);
		var sessions = new InputSessionService(h.Connections, h.Mediator, h.Attributes, h.Permissions, notify, h.Time);
		await sessions.StartAsync(caller, h.Target.Object.DBRef, "CALLBACK", MarkupText.Empty, TimeSpan.FromSeconds(60));
		var session = sessions.GetCapturing(1)!;
		switch (action)
		{
			case "cancel": await sessions.CancelAsync(caller); break;
			case "escape": await sessions.TryEscapeAsync(1, "transport", MarkupText.Plain("@input/cancel")); break;
			case "too-large": await sessions.DeliverAsync(h.Parser, session, MarkupText.Plain(new string('x', InputSessionService.MaxInputCodeUnits + 1))); break;
			case "revoked": h.CanControl = false; await sessions.DeliverAsync(h.Parser, session, MarkupText.Plain("answer")); break;
			case "timeout-rejected":
				h.Time.Now += TimeSpan.FromMinutes(2);
				var scheduler = Substitute.For<ITaskScheduler>();
				scheduler.WriteInputSessionTimeout(session).Returns(new SharpMUSH.Library.Models.SchedulerModels.QueueAdmissionResult(null,
					SharpMUSH.Library.Models.SchedulerModels.QueueRejectionReason.OwnerLimit));
				using (var timeout = new SharpMUSH.Server.Services.InputSessionTimeoutService(sessions, scheduler, notify, h.Connections,
					NullLogger<SharpMUSH.Server.Services.InputSessionTimeoutService>.Instance))
				{
					await timeout.StartAsync(CancellationToken.None);
					try { await publication.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
					finally { await timeout.StopAsync(CancellationToken.None); }
				}
				break;
		}
		var message = await publication.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var serialized = System.Text.Json.JsonSerializer.SerializeToElement(message);
		await Assert.That(serialized.TryGetProperty("SessionId", out var identity)).IsTrue();
		await Assert.That(identity.GetString()).IsEqualTo("transport");
		await Assert.That(message.Markup).Contains("InputSession");
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task GuidedPromptPublicationCarriesCapturedTransportIdentity(bool reprompt)
	{
		var h = new Harness();
		var caller = await h.Connect();
		var bus = Substitute.For<SharpMUSH.Messaging.Abstractions.IMessageBus>();
		MarkupPromptMessage? published = null;
		bus.HandlePublish(Arg.Any<MarkupPromptMessage>(), Arg.Any<CancellationToken>()).Returns(call =>
		{ published = call.Arg<MarkupPromptMessage>(); return Task.CompletedTask; });
		var notify = new NotifyService(bus, h.Connections, Substitute.For<ILocalizationService>());
		var sessions = new InputSessionService(h.Connections, h.Mediator, h.Attributes, h.Permissions, notify, h.Time);
		await sessions.StartAsync(caller, h.Target.Object.DBRef, "CALLBACK", MarkupText.Plain("private prompt"), TimeSpan.FromSeconds(60));
		if (reprompt) await sessions.PromptAsync(caller, MarkupText.Plain("another private prompt"));
		var serialized = System.Text.Json.JsonSerializer.SerializeToElement(published);
		await Assert.That(serialized.TryGetProperty("SessionId", out var identity)).IsTrue();
		await Assert.That(identity.GetString()).IsEqualTo("transport");
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
	public async Task AdmissionEscapeCannotCancelReplacementAfterSnapshot()
	{
		var h = new Harness(); var caller = await h.Connect();
		await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "FIRST", MarkupText.Empty, TimeSpan.FromSeconds(60));
		var snapshot = h.Sessions.CapturePendingInput(1);
		await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "SECOND", MarkupText.Empty, TimeSpan.FromSeconds(60));
		var sessions = Substitute.For<IInputSessionService>();
		sessions.CapturePendingInput(1).Returns(snapshot);
		sessions.TryEscapeAsync(1, "transport", Arg.Any<MarkupText>(), Arg.Any<Guid?>())
			.Returns(call => h.Sessions.TryEscapeAsync(1, "transport", call.Arg<MarkupText>(), call.Arg<Guid?>()));
		await using var queue = Queue(h, sessions);
		await Assert.That((await queue.AdmitUserCommand(1, MarkupText.Plain("@input/cancel"),
			ParserState.Empty with { Handle = 1, ConnectionSessionId = "transport" })).Accepted).IsTrue();
		await Assert.That(h.Sessions.GetCapturing(1)?.CallbackAttribute).IsEqualTo("SECOND");
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
	}

	[Test]
	public async Task GenerationBoundEscapeCannotCancelAConcurrentReplacement()
	{
		var h = new Harness(); var caller = await h.Connect();
		await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "FIRST", MarkupText.Empty, TimeSpan.FromSeconds(60));
		var first = h.Sessions.GetCapturing(1)!;
		await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "SECOND", MarkupText.Empty, TimeSpan.FromSeconds(60));
		await Assert.That(await h.Sessions.TryEscapeAsync(1, "transport", MarkupText.Plain("@input/cancel"), first.Id)).IsTrue();
		await Assert.That(h.Sessions.GetCapturing(1)!.CallbackAttribute).IsEqualTo("SECOND");
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

	[Test]
	[Arguments("halt")]
	[Arguments("read")]
	[Arguments("execute")]
	[Arguments("control")]
	[Arguments("owner")]
	public async Task OversizedInputStillRetiresRevokedAuthority(string change)
	{
		var h = new Harness(); var session = await h.Start();
		switch (change)
		{
			case "halt": Halt(h.Actor.Object); break;
			case "read": h.CanRead = false; break;
			case "execute": h.CanExecute = false; break;
			case "control": h.CanControl = false; break;
			case "owner": h.Target.Object.Owner = new(_ => Task.FromResult(h.Character)); break;
		}
		await h.Sessions.DeliverAsync(h.Parser, session, MarkupText.Plain(new string('x', InputSessionService.MaxInputCodeUnits + 1)));
		await Assert.That(h.Sessions.GetCapturing(1)).IsNull();
		await Assert.That(h.Deliveries.Count).IsEqualTo(0);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task TimeoutRejectionOnlyNotifiesCapturedConnection(bool ownerLimit)
	{
		var h = new Harness(); await h.Start();
		await h.Connect(2, "other"); await h.Connections.Bind(2, h.Owner.Object.DBRef);
		h.Notify.ClearReceivedCalls();
		await using var queue = Queue(h, global: ownerLimit ? 10u : 0u, owner: ownerLimit ? 0u : 100u);
		var notice = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		h.Notify.NotifyLocalizedToSession(1, "transport", "InputSessionTimeoutRejected").Returns(_ =>
		{ notice.TrySetResult(); return ValueTask.CompletedTask; });
		h.Time.Now += TimeSpan.FromMinutes(2);
		using var worker = new SharpMUSH.Server.Services.InputSessionTimeoutService(h.Sessions, queue, h.Notify,
			h.Connections, NullLogger<SharpMUSH.Server.Services.InputSessionTimeoutService>.Instance);
		await worker.StartAsync(CancellationToken.None);
		try { await notice.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
		finally { await worker.StopAsync(CancellationToken.None); }
		await Assert.That(h.Notify.ReceivedCalls().Count(call => call.GetMethodInfo().Name == "NotifyLocalized")).IsEqualTo(0);
		await h.Notify.Received(1).NotifyLocalizedToSession(1, "transport", "InputSessionTimeoutRejected");
		h.Notify.ClearReceivedCalls();
		await queue.AdmitWork(() => ValueTask.FromResult<CallState?>(null), "ordinary", "test", h.Actor.Object.DBRef);
		await Assert.That(h.Notify.ReceivedCalls().Count(call => call.GetMethodInfo().Name == "NotifyLocalized")).IsEqualTo(1);
	}

	[Test]
	public async Task TimeoutWorkerCancellationReachesActualQueueAdmission()
	{
		var h = new Harness(); await h.Start();
		h.Time.Now += TimeSpan.FromMinutes(2);
		var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		h.Mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(async ValueTask<AnyOptionalSharpObject> (call) =>
		{
			var ct = call.Arg<CancellationToken>(); entered.TrySetResult(ct);
			await release.Task.WaitAsync(ct);
			return h.Actor;
		});
		await using var queue = new Scheduler(h.Parser, h.Connections, Substitute.For<ISchedulerFactory>(), h.Attributes,
			h.Mediator, NullLogger<Scheduler>.Instance, inputSessions: h.Sessions);
		using var worker = new SharpMUSH.Server.Services.InputSessionTimeoutService(h.Sessions, queue, h.Notify, h.Connections,
			NullLogger<SharpMUSH.Server.Services.InputSessionTimeoutService>.Instance);
		await worker.StartAsync(CancellationToken.None);
		try
		{
			var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
			await Assert.That(token.IsCancellationRequested).IsTrue();
			await Assert.That(worker.ExecuteTask!.IsCompleted).IsTrue();
		}
		finally
		{
			release.TrySetResult();
			await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
		}
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
	[Arguments("replacement")]
	[Arguments("disconnect")]
	[Arguments("incarnation")]
	[Arguments("expired")]
	[Arguments("timeout-pending")]
	[Arguments("ordinary")]
	public async Task CancelRechecksCaptureAfterAuthorityRead(string transition)
	{
		var h = new Harness(); var caller = await h.Connect();
		await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "CALLBACK", MarkupText.Empty, TimeSpan.FromSeconds(60));
		var original = h.Sessions.GetCapturing(1)!;
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var reads = 0;
		async ValueTask<AnyOptionalSharpObject> ReadAuthority()
		{
			if (Interlocked.Increment(ref reads) == 1)
			{
				entered.TrySetResult();
				await release.Task;
			}
			return h.Actor;
		}
		h.Mediator.Send(Arg.Is<GetObjectNodeQuery>(query => query.DBRef == h.Actor.Object.DBRef), Arg.Any<CancellationToken>())
			.Returns(_ => ReadAuthority());
		var pending = h.Sessions.CancelAsync(caller).AsTask();
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			Guid? replacement = null;
			switch (transition)
			{
				case "replacement":
					await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "REPLACEMENT", MarkupText.Empty, TimeSpan.FromSeconds(60));
					replacement = h.Sessions.GetCapturing(1)?.Id;
					await Assert.That(replacement).IsNotNull();
					await Assert.That(replacement).IsNotEqualTo(original.Id);
					break;
				case "disconnect": await h.Connections.Disconnect(1); break;
				case "incarnation": original.Connection.Metadata["SessionId"] = "new-transport"; break;
				case "expired": h.Time.Now += TimeSpan.FromMinutes(2); break;
				case "timeout-pending":
					h.Time.Now += TimeSpan.FromMinutes(2);
					await Assert.That(h.Sessions.TakeExpired().Single().Id).IsEqualTo(original.Id);
					break;
			}
			release.TrySetResult();
			await Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(5)))
				.IsEqualTo(transition == "ordinary" ? null : InputSessionService.NotActive);
			if (transition == "ordinary")
				await h.Notify.Received(1).NotifyLocalizedToSession(1, "transport", "InputSessionCancelled");
			else
				await h.Notify.DidNotReceive().NotifyLocalizedToSession(Arg.Any<long>(), Arg.Any<string>(), "InputSessionCancelled");
			await Assert.That(h.Sessions.GetCapturing(1)?.Id).IsEqualTo(replacement);
			if (transition == "expired") await Assert.That(h.Sessions.TakeExpired().Single().Id).IsEqualTo(original.Id);
			if (transition == "timeout-pending")
			{
				await h.Sessions.DeliverAsync(h.Parser, original, MarkupText.Empty, timeout: true);
				await Assert.That(h.Deliveries.Count).IsEqualTo(1);
			}
		}
		finally
		{
			release.TrySetResult();
			await pending.WaitAsync(TimeSpan.FromSeconds(5));
		}
	}

	[Test]
	public async Task PromptFailureAndUnhandledCallbackFailureEndCapture()
	{
		var h = new Harness();
		var caller = await h.Connect();
		h.Notify.PromptToSession(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<OneOf.OneOf<MarkupText, string>>()).Returns(_ => throw new InvalidOperationException("prompt failed"));
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
	[Arguments(false, "throw")]
	[Arguments(true, "throw")]
	[Arguments(false, "cancel")]
	[Arguments(true, "cancel")]
	[Arguments(false, "budget")]
	[Arguments(true, "budget")]
	[Arguments(false, "errors")]
	[Arguments(true, "errors")]
	public async Task FailedDeliveryRetiresCallbackOwnedReplacement(bool timeout, string failure)
	{
		var h = new Harness(); var caller = await h.Connect();
		await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "CALLBACK", MarkupText.Empty, TimeSpan.FromSeconds(60));
		var original = h.Sessions.GetCapturing(1)!;
		if (timeout) { h.Time.Now += TimeSpan.FromMinutes(2); h.Sessions.TakeExpired(); }
		using var cancellation = new CancellationTokenSource();
		using var budget = ExecutionBudget.FromMilliseconds(0, cancellation.Token);
		using var scope = budget.Enter();
		Guid? replacement = null;
		async ValueTask<CallState?> Callback()
		{
			await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "REPLACEMENT", MarkupText.Empty, TimeSpan.FromSeconds(60));
			replacement = h.Sessions.GetCapturing(1)?.Id;
			if (failure == "throw") throw new InvalidOperationException("callback failed");
			if (failure is "cancel" or "budget") cancellation.Cancel();
			if (failure == "cancel") cancellation.Token.ThrowIfCancellationRequested();
			return new CallState("result") { HadErrors = failure == "errors" };
		}
		h.Parser.CommandListParse(Arg.Any<MarkupText>()).Returns(_ => Callback());
		Exception? caught = null;
		try { await h.Sessions.DeliverAsync(h.Parser, original, MarkupText.Empty, timeout); }
		catch (Exception error) when (error is InvalidOperationException or OperationCanceledException) { caught = error; }
		await Assert.That(replacement).IsNotNull();
		await Assert.That(replacement).IsNotEqualTo(original.Id);
		await Assert.That(caught is not null).IsEqualTo(failure is "throw" or "cancel");
		await Assert.That(h.Sessions.GetCapturing(1)).IsNull();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task NestedDeliveryRestoresOuterReplacementOwnership(bool startAgain)
	{
		var h = new Harness(); var caller = await h.Connect();
		await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "CALLBACK", MarkupText.Empty, TimeSpan.FromSeconds(60));
		var original = h.Sessions.GetCapturing(1)!;
		var calls = 0;
		async ValueTask<CallState?> Callback()
		{
			calls++;
			await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "REPLACEMENT", MarkupText.Empty, TimeSpan.FromSeconds(60));
			if (calls == 1)
			{
				await h.Sessions.DeliverAsync(h.Parser, h.Sessions.GetCapturing(1)!, MarkupText.Empty);
				if (startAgain) await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "OUTER", MarkupText.Empty, TimeSpan.FromSeconds(60));
				return new CallState("failed outer") { HadErrors = true };
			}
			return CallState.Empty;
		}
		h.Parser.CommandListParse(Arg.Any<MarkupText>()).Returns(_ => Callback());
		await h.Sessions.DeliverAsync(h.Parser, original, MarkupText.Empty);
		await Assert.That(calls).IsEqualTo(2);
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

	private static Scheduler Queue(Harness h, IInputSessionService? sessions = null, uint global = 10, IQueueDiagnosticsRecorder? diagnostics = null, uint owner = 100)
	{
		var config = ReadPennMushConfig.Create(Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst"));
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(config with { Limit = config.Limit with { GlobalQueueLimit = global, PlayerQueueLimit = owner, QueueEntryCpuTime = 1000 } });
		return new(h.Parser, h.Connections, Substitute.For<ISchedulerFactory>(), h.Attributes, h.Mediator,
			NullLogger<Scheduler>.Instance, options, h.Notify, sessions ?? h.Sessions, diagnostics);
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
			await Assert.That((await queue.AdmitUserCommand(1, MarkupText.Plain("[dangerous()];@destroy me"), state)).Accepted).IsTrue();
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(2);
			await Assert.That((await queue.AdmitUserCommand(1, MarkupText.Plain("@input/cancel"), state)).Accepted).IsTrue();
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(2);
		}
		finally { release.TrySetResult(); }
		await Drained(queue);
		await Assert.That(h.Deliveries.Count).IsEqualTo(0);
		await h.Parser.DidNotReceive().CommandParse(Arg.Any<long>(), Arg.Any<IConnectionService>(), Arg.Any<MarkupText>());
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task EscapeBehindPendingStartBypassesSaturatedAdmission(bool ownerLimit)
	{
		var h = new Harness(); var caller = await h.Connect();
		await using var queue = Queue(h, global: ownerLimit ? 10u : 2u, owner: ownerLimit ? 1u : 100u);
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		h.Parser.CommandParse(Arg.Any<long>(), Arg.Any<IConnectionService>(), Arg.Any<MarkupText>())
			.Returns(async ValueTask<CallState> (_) =>
			{
				await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "CALLBACK", MarkupText.Empty, TimeSpan.FromSeconds(60));
				return CallState.Empty;
			});
		await queue.EnqueueWork(async () => { entered.SetResult(); await release.Task; return null; }, "block", "test");
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			var state = ParserState.Empty with { Handle = 1, ConnectionSessionId = "transport" };
			await Assert.That((await queue.AdmitUserCommand(1, MarkupText.Plain("@input/start me/CALLBACK"), state)).Accepted).IsTrue();
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(2);
			await Assert.That((await queue.AdmitUserCommand(1, MarkupText.Plain("@input/cancel"), state)).Accepted).IsTrue();
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(2);
		}
		finally { release.TrySetResult(); }
		await Drained(queue);
		await Assert.That(h.Sessions.GetCapturing(1)).IsNull();
		await Assert.That(await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "CALLBACK", MarkupText.Empty, TimeSpan.FromSeconds(60))).IsNull();
		await Assert.That(h.Sessions.GetCapturing(1)).IsNotNull();
	}

	[Test]
	[Arguments("none")]
	[Arguments("stale-transport")]
	[Arguments("ordinary")]
	public async Task PendingEscapeDoesNotBypassUnrelatedSaturation(string mode)
	{
		var h = new Harness(); var caller = await h.Connect();
		await using var queue = Queue(h, global: mode == "none" ? 1u : 2u);
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		h.Parser.CommandParse(Arg.Any<long>(), Arg.Any<IConnectionService>(), Arg.Any<MarkupText>())
			.Returns(async ValueTask<CallState> (_) =>
			{
				await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "CALLBACK", MarkupText.Empty, TimeSpan.FromSeconds(60));
				return CallState.Empty;
			});
		await queue.EnqueueWork(async () => { entered.SetResult(); await release.Task; return null; }, "block", "test");
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			var state = ParserState.Empty with { Handle = 1, ConnectionSessionId = "transport" };
			if (mode != "none")
				await Assert.That((await queue.AdmitUserCommand(1, MarkupText.Plain("@input/start me/CALLBACK"), state)).Accepted).IsTrue();
			var cancel = await queue.AdmitUserCommand(1, MarkupText.Plain(mode == "ordinary" ? "@input/cancel;think ordinary" : "@input/cancel"),
				state with { ConnectionSessionId = mode == "stale-transport" ? "old" : "transport" });
			await Assert.That(cancel.Accepted).IsFalse();
		}
		finally { release.TrySetResult(); }
		await Drained(queue);
		if (mode == "none")
			await Assert.That(await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "CALLBACK", MarkupText.Empty, TimeSpan.FromSeconds(60))).IsNull();
		await Assert.That(h.Sessions.GetCapturing(1)).IsNotNull();
	}

	[Test]
	[Arguments(false, "")]
	[Arguments(true, "")]
	[Arguments(false, "  \t ")]
	[Arguments(true, "  \t ")]
	[Arguments(false, "@input/cancel")]
	[Arguments(true, "@input/cancel")]
	public async Task ReplyQueuedBehindSessionStartPreservesBlankAndEscapeSemantics(bool websocket, string payload)
	{
		var h = new Harness(); var caller = await h.Connect();
		await using var queue = Queue(h);
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		await queue.EnqueueWork(async () =>
		{
			entered.SetResult();
			await release.Task;
			await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "CALLBACK", MarkupText.Empty, TimeSpan.FromSeconds(60));
			return null;
		}, "queued-session-start", "test");
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			if (websocket)
				await new WebSocketInputConsumer(NullLogger<WebSocketInputConsumer>.Instance, queue, h.Connections, h.Sessions)
					.HandleAsync(new WebSocketInputMessage(1, payload, "transport"));
			else
				await new TelnetInputConsumer(NullLogger<TelnetInputConsumer>.Instance, queue, h.Connections, h.Sessions)
					.HandleAsync(new TelnetInputMessage(1, payload, "transport"));
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(2);
		}
		finally { release.TrySetResult(); }
		await Drained(queue);
		if (payload == "@input/cancel")
		{
			await Assert.That(h.Sessions.GetCapturing(1)).IsNull();
			await Assert.That(h.Deliveries.Count).IsEqualTo(0);
		}
		else
			await Assert.That(h.Deliveries.Single().EnvironmentRegisters["0"].Message!.Text).IsEqualTo(payload);
		await h.Parser.DidNotReceive().CommandParse(Arg.Any<long>(), Arg.Any<IConnectionService>(), Arg.Any<MarkupText>());
	}

	[Test]
	[Arguments("escape")]
	[Arguments("expire")]
	[Arguments("cancel")]
	[Arguments("unbind")]
	public async Task ReplyQueuedBeforeStartCannotExecuteAfterThatSessionEnds(string ending)
	{
		var h = new Harness(); var caller = await h.Connect();
		await using var queue = Queue(h);
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		await queue.EnqueueWork(async () =>
		{
			entered.SetResult();
			await release.Task;
			await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "CALLBACK", MarkupText.Empty, TimeSpan.FromSeconds(60));
			switch (ending)
			{
				case "escape": await h.Sessions.TryEscapeAsync(1, "transport", MarkupText.Plain("@input/cancel")); break;
				case "expire": h.Time.Now += TimeSpan.FromSeconds(61); break;
				case "cancel": await h.Sessions.CancelAsync(caller); break;
				case "unbind": await h.Connections.Unbind(1); break;
			}
			return null;
		}, "queued-start-and-end", "test");
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			await queue.WriteUserCommand(1, MarkupText.Plain("@destroy me"),
				ParserState.Empty with { Handle = 1, ConnectionSessionId = "transport" });
		}
		finally { release.TrySetResult(); }
		await Drained(queue);
		await h.Parser.DidNotReceive().CommandParse(Arg.Any<long>(), Arg.Any<IConnectionService>(), Arg.Any<MarkupText>());
		await Assert.That(h.Deliveries.Count).IsEqualTo(0);
		// Tombstones fence previously queued replies, without consuming future ordinary input.
		await queue.WriteUserCommand(1, MarkupText.Plain("think ordinary"),
			ParserState.Empty with { Handle = 1, ConnectionSessionId = "transport" });
		await Drained(queue);
		await h.Parser.Received(1).CommandParse(1, h.Connections,
			Arg.Is<MarkupText>(text => text.Text == "think ordinary"));
	}

	[Test]
	[Arguments("second")]
	[Arguments("@input/cancel")]
	public async Task RepliesQueuedBeforeStartRemainBoundToFirstCapture(string second)
	{
		var h = new Harness(); var caller = await h.Connect();
		await using var queue = Queue(h);
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		await queue.EnqueueWork(async () =>
		{
			entered.SetResult(); await release.Task;
			await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "CALLBACK", MarkupText.Empty, TimeSpan.FromSeconds(60));
			return null;
		}, "queued-start", "test");
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		async ValueTask<CallState?> ReplaceCapture()
		{
			await h.Sessions.StartAsync(caller, h.Target.Object.DBRef, "REPLACEMENT", MarkupText.Empty, TimeSpan.FromSeconds(60));
			return CallState.Empty;
		}
		h.Parser.CommandListParse(Arg.Any<MarkupText>()).Returns(_ => ReplaceCapture());
		try
		{
			var state = ParserState.Empty with { Handle = 1, ConnectionSessionId = "transport" };
			await queue.WriteUserCommand(1, MarkupText.Plain("first"), state);
			await queue.WriteUserCommand(1, MarkupText.Plain(second), state);
		}
		finally { release.TrySetResult(); }
		await Drained(queue);
		await Assert.That(h.Deliveries.Count).IsEqualTo(1);
		await Assert.That(h.Deliveries.Single().EnvironmentRegisters["0"].Message!.Text).IsEqualTo("first");
		await Assert.That(h.Sessions.GetCapturing(1)).IsNotNull();
		await Assert.That(h.Sessions.GetCapturing(1)!.CallbackAttribute).IsEqualTo("REPLACEMENT");
	}

	[Test]
	public async Task BlankInputWithoutCaptureDoesNotInvokeTheCommandParser()
	{
		var h = new Harness(); await h.Connect();
		await using var queue = Queue(h);
		await queue.WriteUserCommand(1, MarkupText.Plain(" \t "), ParserState.Empty with { Handle = 1, ConnectionSessionId = "transport" });
		await Drained(queue);
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
	public async Task ReleaseCallbackCanControlQueueAfterSubmitterBudgetIsDisposed(bool callerSuppressesFlow)
	{
		var h = new Harness(); var session = await h.Start();
		var captures = Substitute.For<IInputSessionService>();
		await using var queue = Queue(h, captures);
		var callback = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
		captures.When(service => service.Discard(session)).Do(_ =>
		{
			try
			{
				queue.PausePending(long.MaxValue, "release callback probe").AsTask().GetAwaiter().GetResult();
				callback.TrySetResult(null);
			}
			catch (Exception error) { callback.TrySetResult(error); }
		});
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var bodyHasBudget = false;
		try
		{
			using (var submittingBudget = new ExecutionBudget(TimeSpan.FromSeconds(30)))
			using (submittingBudget.Enter())
			{
				Func<ValueTask<CallState?>> work = async () =>
				{
					bodyHasBudget = ExecutionBudget.Current is not null;
					entered.TrySetResult();
					await finish.Task.WaitAsync(ExecutionBudget.CurrentToken);
					return null;
				};
				if (callerSuppressesFlow)
				{
					ValueTask<SharpMUSH.Library.Models.SchedulerModels.QueueAdmissionResult> admission;
					using (ExecutionContext.SuppressFlow()) admission = queue.AdmitWork(work, "starts-consumer", "test");
					await admission;
				}
				else await queue.AdmitWork(work, "starts-consumer", "test");
				await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			}
			var timeout = await queue.WriteInputSessionTimeout(session);
			await queue.HaltByPid(timeout.Pid!.Value);
			finish.TrySetResult();
			var error = await callback.Task.WaitAsync(TimeSpan.FromSeconds(5));
			await Assert.That(error).IsNull();
			await Assert.That(bodyHasBudget).IsTrue();
			captures.Received(1).Discard(session);
		}
		finally { finish.TrySetResult(); }
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task HaltAndShutdownReleaseTimeoutRecordsExactlyOnceOutsideAdmissionLock(bool shutdown)
	{
		var h = new Harness(); var session = await h.Start();
		var captures = Substitute.For<IInputSessionService>();
		var diagnostics = new QueueDiagnosticsRecorder();
		var queue = Queue(h, captures, diagnostics: diagnostics);
		var cleanupCouldReadQueue = false;
		var cleanupFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		captures.When(service => service.Discard(session)).Do(_ =>
		{
			// A dedicated thread tests the lock boundary without depending on thread-pool availability.
			var probe = new Thread(() =>
			{
				using var probeBudget = new ExecutionBudget(Timeout.InfiniteTimeSpan);
				using var probeScope = probeBudget.Enter();
				queue.GetQueueUsage();
				queue.PausePending(long.MaxValue, "probe").AsTask().GetAwaiter().GetResult();
			})
			{ IsBackground = true };
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
			await Assert.That(diagnostics.Recent().Single(row => row.Pid == timeout.Pid).Outcome)
				.IsEqualTo(SharpMUSH.Library.Models.Diagnostics.QueueOutcome.Cancelled);
		}
		finally
		{
			release.TrySetResult();
			if (!disposed) await queue.DisposeAsync();
		}
	}

}
