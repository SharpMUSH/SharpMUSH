using System.Collections.Immutable;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;
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
		public ITaskScheduler Scheduler { get; } = Substitute.For<ITaskScheduler>();
		public Func<ParserState, ValueTask> Admit { get; set; } = _ => ValueTask.CompletedTask;
		public ParserState? HandlerState { get; private set; }
		public ParserState? EventState { get; private set; }
		public MString? EventBody { get; private set; }
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
				return stage == "lookup" ? events : handler;
			});
			Attributes.GetAttributeAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<string>(), IAttributeService.AttributeMode.Execute, Arg.Any<bool>())
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
					throw new InvalidOperationException("The completion event ran inside the request.");
				}
				return CallState.Empty;
			});
			var baseline = TestSharpMushOptions.Create();
			var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
			options.CurrentValue.Returns(baseline with { Database = baseline.Database with { HttpHandler = 8, EventHandler = 9 }, Limit = baseline.Limit with { QueueEntryCpuTime = milliseconds } });
			Scheduler.AdmitCommandList(Arg.Any<MString>(), Arg.Any<ParserState>()).Returns(async ValueTask<QueueAdmissionResult> (call) =>
			{
				EventCalls++;
				EventBody = call.Arg<MString>();
				EventState = call.Arg<ParserState>();
				await Admit(EventState);
				return new QueueAdmissionResult(1, QueueRejectionReason.None);
			});
			Events = new EventService(Mediator, Attributes, new Lazy<ITaskScheduler>(Scheduler), options, NullLogger<EventService>.Instance);
			Service = new(Mediator, Attributes, Parser, new HttpOutputCapture(), Events, InlineTaskScheduler.Create(), options, NullLogger<HttpHandlerCommandService>.Instance);
		}
	}

	[Test]
	[Arguments("lookup", false, false)]
	[Arguments("lookup", true, false)]
	[Arguments("enactor", false, false)]
	[Arguments("enactor", true, false)]
	[Arguments("attribute", false, false)]
	[Arguments("attribute", true, false)]
	[Arguments("admit", false, false)]
	[Arguments("admit", true, false)]
	[Arguments("admit", true, true)]
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
		fixture.Admit = async _ => await fixture.Visit("admit", ExecutionBudget.CurrentToken);
		using var request = new CancellationTokenSource();
		using var parent = new ExecutionBudget(ambient ? TimeSpan.FromMilliseconds(100) : Timeout.InfiniteTimeSpan);
		using var parentScope = parent.Enter();
		var pending = fixture.Service.DispatchAsync("GET", "/event", "request", [], request.Token).AsTask();
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			if (deadline)
			{
				var response = (await pending.WaitAsync(TimeSpan.FromSeconds(1))).Expect<HttpHandlerResult>();
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
		await Assert.That(response.Expect<HttpHandlerResult>().Status).IsEqualTo(503);
		await Assert.That(fixture.EventCalls).IsEqualTo(0);
		await Assert.That(fixture.EventLookups).IsEqualTo(0);
	}

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	public async Task NormalCompletionQueuesTheEventWithoutWaitingForIt(int admissionFailure)
	{
		var fixture = new Fixture(5000);
		if (admissionFailure == 1) fixture.Admit = _ => throw new IOException("ordinary admission failure");
		if (admissionFailure == 2) fixture.Admit = _ => throw new OperationCanceledException("unrelated admission cancellation");
		var response = (await fixture.Service.DispatchAsync("GET", "/normal", "request", [])).Expect<HttpHandlerResult>();
		await Assert.That(response.Status).IsEqualTo(201);
		await Assert.That(response.Body).IsEqualTo("original response");
		await Assert.That(fixture.EventCalls).IsEqualTo(1);
		await Assert.That(fixture.EventBody!.ToPlainText()).IsEqualTo("event");
		await Assert.That(fixture.EventState!.Executor!.Value.Number).IsEqualTo(9);
		await Assert.That(fixture.EventState.ExecutionBudget).IsNull();
		await Assert.That(fixture.EventState.EnvironmentRegisters["3"].Message!.ToPlainText()).IsEqualTo("201");
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

	[Test]
	[Arguments("handler", false)]
	[Arguments("admit", false)]
	[Arguments("handler", true)]
	[Arguments("admit", true)]
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
		fixture.Admit = async _ => await fixture.Visit("admit", ExecutionBudget.CurrentToken);
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
			var response = (await pending.WaitAsync(TimeSpan.FromSeconds(1))).Expect<HttpHandlerResult>();
			await Assert.That(response.Status).IsEqualTo(503);
			await Assert.That(response.Body).IsEqualTo(ExecutionBudget.Error);
		}
	}

}
