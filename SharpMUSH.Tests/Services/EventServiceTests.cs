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
		public EventService Events { get; }
		public AnySharpObject Handler { get; }
		public AnySharpObject Player { get; }

		public Fixture(string? attributeBody)
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
			Attributes.GetAttributeAsync(Handler, Handler, "TEST`EVENT", IAttributeService.AttributeMode.Execute, false)
				.Returns(ValueTask.FromResult<OptionalSharpAttributeOrError>(attributeBody is null
					? new None()
					: new[] { new SharpAttribute("", "", "TEST`EVENT", [], null, "TEST`EVENT", null!, null!, null!) { Value = MarkupText.Plain(attributeBody) } }));
			Scheduler.AdmitCommandList(Arg.Any<MString>(), Arg.Any<ParserState>())
				.Returns(ValueTask.FromResult(new QueueAdmissionResult(1, QueueRejectionReason.None)));

			var baseline = TestSharpMushOptions.Create();
			var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
			options.CurrentValue.Returns(baseline with { Database = baseline.Database with { EventHandler = 9 } });
			Events = new EventService(Mediator, Attributes, new Lazy<ITaskScheduler>(Scheduler), options,
				NullLogger<EventService>.Instance);
		}
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
}
