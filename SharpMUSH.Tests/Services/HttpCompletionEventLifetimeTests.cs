using System.Collections.Immutable;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Server;

namespace SharpMUSH.Tests.Services;

public class HttpCompletionEventLifetimeTests
{
	private sealed class Fixture
	{
		public IMediator Mediator { get; } = Substitute.For<IMediator>();
		public IAttributeService Attributes { get; } = Substitute.For<IAttributeService>();
		public IMUSHCodeParser Parser { get; } = Substitute.For<IMUSHCodeParser>();
		public HttpHandlerCommandService Service { get; }
		public EventService Events { get; }
		public Func<string, CancellationToken, ValueTask> Visit { get; set; } = (_, _) => ValueTask.CompletedTask;
		public Func<ParserState, ValueTask> Handler { get; set; } = _ => ValueTask.CompletedTask;
		public Func<ParserState, ValueTask> Event { get; set; } = _ => ValueTask.CompletedTask;
		public ParserState? HandlerState { get; private set; }
		public ParserState? EventState { get; private set; }
		public int EventCalls { get; private set; }
		public int EventLookups { get; private set; }

		public Fixture(uint milliseconds)
		{
			var objects = new TestObjectFactory();
			AnySharpObject handler = objects.CreateThing(8, "HTTP handler");
			AnySharpObject events = objects.CreateThing(9, "Events");
			var reads = 0;
			Mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(async ValueTask<AnyOptionalSharpObject> (call) =>
			{
				var stage = Interlocked.Increment(ref reads) switch { 1 => "handler", 2 => "lookup", _ => "enactor" };
				if (stage == "lookup") EventLookups++;
				await Visit(stage, call.Arg<CancellationToken>());
				return (stage == "lookup" ? events : handler).AsThing;
			});
			Attributes.GetAttributeAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<string>(), IAttributeService.AttributeMode.Execute, false)
				.Returns(async ValueTask<OptionalSharpAttributeOrError> (call) =>
				{
					var name = call.Arg<string>();
					if (name != "GET") await Visit("attribute", ExecutionBudget.CurrentToken);
					return (OptionalSharpAttributeOrError)new[] { new SharpAttribute("", "", name, [], null, name, null!, null!, null!) { Value = MarkupText.Plain(name == "GET" ? "handler" : "event") } };
				});
			Parser.State.Returns(ImmutableStack<ParserState>.Empty);
			ParserState? current = null;
			Parser.Push(Arg.Any<ParserState>()).Returns(call => { current = call.Arg<ParserState>(); return Parser; });
			Parser.CommandListParse(Arg.Any<MarkupText>()).Returns(async ValueTask<CallState?> (call) =>
			{
				if (call.Arg<MarkupText>().ToPlainText() == "handler")
				{
					HandlerState = current;
					current!.HttpResponse!.StatusLine = "201 Created";
					current.HttpResponse.Body.Append("original response");
					await Handler(current);
				}
				else
				{
					EventCalls++;
					EventState = current;
					await Visit("body", ExecutionBudget.CurrentToken);
					await Event(current!);
				}
				return CallState.Empty;
			});
			var baseline = TestSharpMushOptions.Create();
			var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
			options.CurrentValue.Returns(baseline with { Database = baseline.Database with { HttpHandler = 8, EventHandler = 9 }, Limit = baseline.Limit with { QueueEntryCpuTime = milliseconds } });
			Events = new EventService(Mediator, Attributes, options, NullLogger<EventService>.Instance);
			Service = new(Mediator, Attributes, Parser, new HttpOutputCapture(), Events, options, NullLogger<HttpHandlerCommandService>.Instance);
		}
	}

	[Test]
	[Arguments("lookup", false, false)]
	[Arguments("lookup", true, false)]
	[Arguments("enactor", false, false)]
	[Arguments("enactor", true, false)]
	[Arguments("attribute", false, false)]
	[Arguments("attribute", true, false)]
	[Arguments("body", false, false)]
	[Arguments("body", true, false)]
	[Arguments("body", true, true)]
	public async Task CompletionEventHonorsTheOriginalRequestLifetime(string stage, bool deadline, bool ambient)
	{
		var fixture = new Fixture(deadline && !ambient ? 100u : 0u);
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var release = new CancellationTokenSource();
		fixture.Visit = async (current, token) =>
		{
			if (current != stage) return;
			entered.TrySetResult();
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, release.Token);
			await Task.Delay(Timeout.Infinite, linked.Token);
		};
		using var request = new CancellationTokenSource();
		using var parent = new ExecutionBudget(ambient ? TimeSpan.FromMilliseconds(100) : Timeout.InfiniteTimeSpan);
		using var parentScope = parent.Enter();
		var pending = fixture.Service.DispatchAsync("GET", "/event", "request", [], request.Token).AsTask();
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			if (deadline)
			{
				var response = (await pending.WaitAsync(TimeSpan.FromSeconds(1))).AsT0;
				await Assert.That(response.Status).IsEqualTo(503);
				await Assert.That(response.Body).IsEqualTo(ExecutionBudget.Error);
			}
			else
			{
				request.Cancel();
				await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(1)));
			}
		}
		finally
		{
			release.Cancel();
			try { await pending; } catch (OperationCanceledException) { }
		}
	}

	[Test]
	public async Task ExpiredHandlerDoesNotStartACompletionEvent()
	{
		var fixture = new Fixture(10) { Handler = async _ => await Task.Delay(30) };
		var response = await fixture.Service.DispatchAsync("GET", "/expired", "", []);
		await Assert.That(response.AsT0.Status).IsEqualTo(503);
		await Assert.That(fixture.EventCalls).IsEqualTo(0);
		await Assert.That(fixture.EventLookups).IsEqualTo(0);
	}

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	public async Task NormalCompletionPreservesResponseAndSharesParserBudget(int eventFailure)
	{
		var fixture = new Fixture(5000);
		if (eventFailure == 1) fixture.Event = _ => throw new IOException("ordinary event failure");
		if (eventFailure == 2) fixture.Event = _ => throw new OperationCanceledException("unrelated event cancellation");
		var response = (await fixture.Service.DispatchAsync("GET", "/normal", "request", [])).AsT0;
		await Assert.That(response.Status).IsEqualTo(201);
		await Assert.That(response.Body).IsEqualTo("original response");
		await Assert.That(fixture.EventCalls).IsEqualTo(1);
		await Assert.That(ReferenceEquals(fixture.HandlerState!.ExecutionBudget, fixture.EventState!.ExecutionBudget)).IsTrue();
		await Assert.That(fixture.EventState.EnvironmentRegisters["3"].Message!.ToPlainText()).IsEqualTo("201");
	}
	private sealed class LegacyEvents : IEventService
	{
		public CancellationToken ObservedToken { get; private set; }
		public async ValueTask TriggerEventAsync(IMUSHCodeParser parser, string eventName, DBRef? enactor, params string[] args)
		{
			ObservedToken = ExecutionBudget.CurrentToken;
			await Task.Delay(Timeout.Infinite, ObservedToken);
		}
	}

	[Test]
	public async Task LegacyEventSlotAndImplementationRemainUsable()
	{
		var signature = new[] { typeof(IMUSHCodeParser), typeof(string), typeof(DBRef?), typeof(string[]) };
		await Assert.That(typeof(IEventService).GetMethod(nameof(IEventService.TriggerEventAsync), signature)!.ReturnType).IsEqualTo(typeof(ValueTask));
		await Assert.That(typeof(EventService).GetMethod(nameof(EventService.TriggerEventAsync), signature)!.ReturnType).IsEqualTo(typeof(ValueTask));
		var legacy = new LegacyEvents();
		IEventService service = legacy;
		using var request = new CancellationTokenSource();
		var pending = service.TriggerEventAsync(Substitute.For<IMUSHCodeParser>(), "TEST", null, request.Token).AsTask();
		await Assert.That(legacy.ObservedToken.CanBeCanceled).IsTrue();
		request.Cancel();
		await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(1)));
	}

	private sealed class HeldTimerProvider : TimeProvider
	{
		private Action? _fire;
		public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
		{
			_fire = () => callback(state);
			return new HeldTimer();
		}
		public void Fire() => _fire!();
		private sealed class HeldTimer : ITimer
		{
			public bool Change(TimeSpan dueTime, TimeSpan period) => true;
			public void Dispose() { }
			public ValueTask DisposeAsync() => ValueTask.CompletedTask;
		}
	}

	private sealed class ExpiringLegacyEvent : IEventService
	{
		public async ValueTask TriggerEventAsync(IMUSHCodeParser parser, string eventName, DBRef? enactor, params string[] args)
		{
			while (!ExecutionBudget.Current!.IsExpired) await Task.Delay(1);
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task MonotonicExpiryPropagatesBeforeTimerCallback(bool legacy)
	{
		var fixture = new Fixture(0);
		fixture.Event = async _ =>
		{
			while (!ExecutionBudget.Current!.IsExpired) await Task.Delay(1);
			ExecutionBudget.Current.ThrowIfExceeded();
		};
		using var budget = new ExecutionBudget(TimeSpan.FromMilliseconds(100), default, new HeldTimerProvider());
		using var scope = budget.Enter();
		IEventService service = legacy ? new ExpiringLegacyEvent() : fixture.Events;
		await Assert.ThrowsAsync<OperationCanceledException>(async () =>
			await service.TriggerEventAsync(fixture.Parser, "TEST", null, budget.Token));
		await Assert.That(budget.Token.IsCancellationRequested).IsFalse();
	}

	[Test]
	[Arguments("handler", false)]
	[Arguments("body", false)]
	[Arguments("handler", true)]
	[Arguments("body", true)]
	public async Task ParentTimerExpiryIsNotMistakenForRequestCancellation(string stage, bool cancelRequest)
	{
		var fixture = new Fixture(0);
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		fixture.Visit = async (current, token) =>
		{
			if (current != stage) return;
			entered.TrySetResult();
			await Task.Delay(Timeout.Infinite, token);
		};
		var timer = new HeldTimerProvider();
		using var parent = new ExecutionBudget(TimeSpan.FromMinutes(1), default, timer);
		using var scope = parent.Enter();
		using var request = new CancellationTokenSource();
		var pending = fixture.Service.DispatchAsync("GET", "/parent-timer", "", [], request.Token).AsTask();
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		if (cancelRequest) request.Cancel();
		timer.Fire();
		await Assert.That(parent.Remaining).IsGreaterThan(TimeSpan.Zero);
		if (cancelRequest)
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(1)));
		else
		{
			var response = (await pending.WaitAsync(TimeSpan.FromSeconds(1))).AsT0;
			await Assert.That(response.Status).IsEqualTo(503);
			await Assert.That(response.Body).IsEqualTo(ExecutionBudget.Error);
		}
	}

}
