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

/// <summary>
/// An event is queued, as PennMUSH's <c>queue_event</c> queues it: the handler's attribute becomes a
/// queue entry of its own, run by the handler, rather than running inside the code that raised it and
/// spending that code's time limit.
/// </summary>
public class EventServiceTests
{
	private sealed class Fixture
	{
		public IMediator Mediator { get; } = Substitute.For<IMediator>();
		public IAttributeService Attributes { get; } = Substitute.For<IAttributeService>();
		public ITaskScheduler Scheduler { get; } = Substitute.For<ITaskScheduler>();
		public IMUSHCodeParser Parser { get; } = Substitute.For<IMUSHCodeParser>();
		public EventService Events { get; }
		public AnySharpObject Handler { get; }
		public AnySharpObject Player { get; }

		public Fixture(string? attributeBody, string eventName = "TEST`EVENT", params (string Name, string Body)[] below)
		{
			var objects = new TestObjectFactory();
			Handler = objects.CreateThing(9, "Event Handler");
			Player = objects.CreatePlayer(20, "Raiser");
			Mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
				.Returns(call => ValueTask.FromResult<AnyOptionalSharpObject>(call.Arg<GetObjectNodeQuery>().DBRef.Number switch
				{
					9 => Handler,
					20 => Player,
					_ => new None()
				}));
			Attributes.GetAttributeAsync(Handler, Handler, eventName, IAttributeService.AttributeMode.Execute, false)
				.Returns(ValueTask.FromResult<OptionalSharpAttributeOrError>(attributeBody is null
					? new None()
					: new[] { Attribute(eventName, attributeBody) }));
			Attributes.GetAttributePatternAsync(Handler, Handler, $"{eventName}`*", false, IAttributeService.AttributePatternMode.Wildcard)
				.Returns(ValueTask.FromResult(new SharpAttributesOrError(below.Select(x => Attribute(x.Name, x.Body)).ToArray())));
			Parser.FromState(Arg.Any<ParserState>()).Returns(Parser);
			Scheduler.AdmitCommandList(Arg.Any<MString>(), Arg.Any<ParserState>())
				.Returns(ValueTask.FromResult(new QueueAdmissionResult(1, QueueRejectionReason.None)));

			var baseline = TestSharpMushOptions.Create();
			var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
			options.CurrentValue.Returns(baseline with { Database = baseline.Database with { EventHandler = 9 } });
			Events = new EventService(Mediator, Attributes, new Lazy<ITaskScheduler>(Scheduler), new Lazy<IMUSHCodeParser>(Parser),
				options, NullLogger<EventService>.Instance);
		}

		private static SharpAttribute Attribute(string name, string body)
			=> new("", "", name, [], null, name, null!, null!, null!) { Value = MarkupText.Plain(body) };

		/// <summary>What each queued entry runs, and as which attribute, in the order they were queued.</summary>
		public (string Body, string Attribute)[] Queued()
			=> Scheduler.ReceivedCalls()
				.Where(x => x.GetMethodInfo().Name == nameof(ITaskScheduler.AdmitCommandList))
				.Select(x => (((MString)x.GetArguments()[0]!).ToPlainText(), ((ParserState)x.GetArguments()[1]!).CurrentEvaluation!.Name))
				.ToArray();
	}

	[Test]
	public async Task AnEventIsQueuedAsTheHandlersOwnEntry()
	{
		var fixture = new Fixture("@pemit %#=%0 %1");

		using var budget = new ExecutionBudget(TimeSpan.FromMinutes(1));
		using var scope = budget.Enter();
		await fixture.Events.TriggerEventAsync("TEST`EVENT", fixture.Player.Object().DBRef, "first", "second");

		var call = fixture.Scheduler.ReceivedCalls().Single(x => x.GetMethodInfo().Name == nameof(ITaskScheduler.AdmitCommandList));
		var body = (MString)call.GetArguments()[0]!;
		var state = (ParserState)call.GetArguments()[1]!;
		await Assert.That(body.ToPlainText()).IsEqualTo("@pemit %#=%0 %1");
		await Assert.That(state.Executor!.Value.Number).IsEqualTo(9);
		await Assert.That(state.Enactor!.Value.Number).IsEqualTo(20);
		await Assert.That(state.CurrentEvaluation).IsEqualTo(new DBAttribute(fixture.Handler.Object().DBRef, "TEST`EVENT"));
		await Assert.That(state.Arguments["0"].Message.ToPlainText()).IsEqualTo("first");
		await Assert.That(state.EnvironmentRegisters["1"].Message.ToPlainText()).IsEqualTo("second");
		// The entry gets its own time limit when it runs; it does not carry the raiser's.
		await Assert.That(state.ExecutionBudget).IsNull();
	}

	[Test]
	[Arguments(null)]
	[Arguments(404)]
	public async Task ASystemEventOrAVanishedEnactorRunsAsGod(int? enactor)
	{
		var fixture = new Fixture("think %#");

		await fixture.Events.TriggerEventAsync("TEST`EVENT", enactor is { } number ? new DBRef(number) : null);

		await fixture.Scheduler.Received(1).AdmitCommandList(Arg.Any<MString>(),
			Arg.Is<ParserState>(state => state.Enactor!.Value.Number == 1));
	}

	[Test]
	public async Task AnEventWithNoHandlerAttributeQueuesNothing()
	{
		var fixture = new Fixture(null);

		await fixture.Events.TriggerEventAsync("TEST`EVENT", fixture.Player.Object().DBRef);

		await fixture.Scheduler.DidNotReceiveWithAnyArgs().AdmitCommandList(default!, default!);
	}

	/// <summary>
	/// Each attribute one level below the event's own is queued after it as an entry of its own, as @trigger would
	/// queue it, so a package can hear an event without editing the attribute another package wrote.
	/// </summary>
	[Test]
	public async Task TheAttributesBelowTheEventAreQueuedAfterItInNameOrder()
	{
		var fixture = new Fixture("think own", "TEST`EVENT", ("TEST`EVENT`SCENE", "think scene"), ("TEST`EVENT`COMMFEED", "think comm"));

		await fixture.Events.TriggerEventAsync("TEST`EVENT", fixture.Player.Object().DBRef, "first");

		await Assert.That(fixture.Queued()).IsEquivalentTo(new[]
		{
			("think own", "TEST`EVENT"),
			("think comm", "TEST`EVENT`COMMFEED"),
			("think scene", "TEST`EVENT`SCENE")
		}, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>The event's own attribute, empty because it only holds the ones below it, runs nothing.</summary>
	[Test]
	public async Task AnEmptyBranchRunsOnlyTheAttributesBelowIt()
	{
		var fixture = new Fixture("", "TEST`EVENT", ("TEST`EVENT`SCENE", "think scene"), ("TEST`EVENT`EMPTY", ""));

		await fixture.Events.TriggerEventAsync("TEST`EVENT", fixture.Player.Object().DBRef);

		await Assert.That(fixture.Queued()).IsEquivalentTo(new[] { ("think scene", "TEST`EVENT`SCENE") });
	}

	/// <summary>
	/// A stoppable event's attributes run in turn in one entry, as @include would run them; one that calls
	/// stopevent() keeps the ones after it from running.
	/// </summary>
	[Test]
	public async Task StopEventKeepsTheLaterAttributesOfAStoppableEventFromRunning()
	{
		var fixture = new Fixture("think own", SharpMUSH.Library.Definitions.SharpEvents.SocketGmcp,
			("SOCKET`GMCP`A", "think stop"), ("SOCKET`GMCP`B", "think never"));
		Func<ValueTask<CallState?>>? work = null;
		DBRef? queuedEnactor = null;
		fixture.Scheduler.AdmitWork(Arg.Do<Func<ValueTask<CallState?>>>(x => work = x), Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<DBRef>(), Arg.Do<DBRef>(x => queuedEnactor = x), Arg.Any<bool>())
			.Returns(ValueTask.FromResult(new QueueAdmissionResult(1, QueueRejectionReason.None)));
		var ran = new List<string>();
		fixture.Parser.CommandListParse(Arg.Any<MString>()).Returns(call =>
		{
			var body = call.Arg<MString>().ToPlainText();
			ran.Add(body);
			if (body == "think stop")
			{
				EventStop.Current!.Request();
			}

			return ValueTask.FromResult<CallState?>(CallState.Empty);
		});

		await fixture.Events.TriggerEventAsync(SharpMUSH.Library.Definitions.SharpEvents.SocketGmcp, null, "1", "Core.Hello", "{}", "");
		await Assert.That(fixture.Queued()).IsEmpty();
		// The entry's enactor is who caused the event, so a time-out is reported to them, not to the handler.
		await Assert.That(queuedEnactor!.Value.Number).IsEqualTo(1);
		await work!();

		await Assert.That(ran).IsEquivalentTo(new[] { "think own", "think stop" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(EventStop.Current).IsNull();
	}
}
