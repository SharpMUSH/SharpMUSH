using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Service for triggering PennMUSH-compatible events.
/// Events allow administrators to designate an object as an event handler
/// that receives notifications when specific system events occur.
/// <para>
/// The handler runs the attribute named after the event, then each attribute one level below it
/// (<c>PAGE`MESSAGE`SCENE</c>), so several packages can each hear an event without editing one another's code.
/// </para>
/// </summary>
public class EventService(
	IMediator mediator,
	IAttributeService attributeService,
	Lazy<ITaskScheduler> scheduler,
	Lazy<IMUSHCodeParser> parser,
	IOptionsWrapper<SharpMUSHOptions> options,
	ILogger<EventService> logger) : IEventService
{
	/// <summary>One of the handler's attributes that hears the event.</summary>
	private sealed record Hearer(string Attribute, MString Body);

	/// <inheritdoc />
	public ValueTask TriggerEventAsync(string eventName, DBRef? enactor, params string[] args)
		=> TriggerEventAsync(eventName, enactor, args.Select(MarkupText.Plain).ToArray());

	/// <inheritdoc />
	public async ValueTask TriggerEventAsync(string eventName, DBRef? enactor, IReadOnlyList<MString> args)
	{
		try
		{
			var eventHandlerDbRef = options.CurrentValue.Database.EventHandler;

			if (eventHandlerDbRef is null or 0)
			{
				return;
			}

			var eventHandlerRef = new DBRef((int)eventHandlerDbRef.Value, null);
			if (await mediator.Send(new GetObjectNodeQuery(eventHandlerRef), ExecutionBudget.CurrentToken) is not AnySharpObject eventHandler)
			{
				logger.LogWarning(
					"Event handler object #{EventHandlerDbRef} not found for event {EventName}",
					eventHandlerDbRef.Value,
					eventName);
				return;
			}

			var name = eventName.ToUpperInvariant();
			var hearers = await HearersAsync(eventHandler, name);

			// Not every event needs a handler; one with no attribute for it queues nothing.
			if (hearers.Count == 0)
			{
				return;
			}

			// %# is the object that caused the event (the player who connected, the wizard who ran @tel).
			// A system event, or one whose enactor is gone, runs with God (#1) as its enactor so that %#
			// is always a real dbref inside handler code (PennMUSH uses #-1, which many functions reject).
			var resolvedEnactorRef = enactor is { Number: >= 0 } eventEnactor
				&& await mediator.Send(new GetObjectNodeQuery(eventEnactor), ExecutionBudget.CurrentToken) is AnySharpObject
					? eventEnactor
					: new DBRef(1, null);

			var handlerRef = eventHandler.Object().DBRef;
			var arguments = EventArguments.Of(name, args);
			if (EventCatalogue.Find(name) is { Stoppable: true })
			{
				await QueueInTurnAsync(name, handlerRef, resolvedEnactorRef, arguments, hearers);
				return;
			}

			// queue_event (src/cque.c:392-517) queues the handler's attribute as an entry of its own, run
			// by the handler and charged to it, so the code that raised the event never waits on it or
			// shares its time limit. Each attribute below it is queued the same way, as @trigger would.
			// The handler runs with its own permissions: the seeded Event Handler (#9) is a WIZARD object;
			// a custom, non-wizard handler runs unprivileged.
			foreach (var hearer in hearers)
			{
				var admission = await QueueHold.AdmitAsync(scheduler.Value, Body(hearer),
					State(handlerRef, resolvedEnactorRef, arguments, hearer));
				if (!admission.Accepted)
				{
					logger.LogWarning("Event {EventName} was not queued for {Attribute}: {Reason}", name, hearer.Attribute,
						admission.Reason);
				}
			}
		}
		catch (OperationCanceledException) when (ExecutionBudget.Current?.IsExceeded == true)
		{
			throw;
		}
		catch (Exception ex)
		{
			// An event failure must not break the code that raised it.
			logger.LogError(
				ex,
				"Error triggering event {EventName} with enactor {Enactor}",
				eventName,
				enactor);
		}
	}

	/// <summary>
	/// The handler's attribute named after the event, then each attribute one level below it in name order. An
	/// empty attribute (a branch that only holds the ones below it) runs nothing.
	/// </summary>
	private async ValueTask<IReadOnlyList<Hearer>> HearersAsync(AnySharpObject eventHandler, string eventName)
	{
		var hearers = new List<Hearer>();

		// atr_get_noparent (src/cque.c:418): an event is the handler's own attribute, never a parent's.
		if (await attributeService.GetAttributeAsync(eventHandler, eventHandler, eventName,
					IAttributeService.AttributeMode.Execute, parent: false) is SharpAttribute[] own
				&& own.Last().Value.Length > 0)
		{
			hearers.Add(new Hearer(eventName, own.Last().Value));
		}

		if (await attributeService.GetAttributePatternAsync(eventHandler, eventHandler, $"{eventName}`*", checkParents: false,
					IAttributeService.AttributePatternMode.Wildcard) is SharpAttribute[] below)
		{
			hearers.AddRange(below
				.Where(attribute => attribute.Value.Length > 0)
				.OrderBy(attribute => attribute.LongName, StringComparer.Ordinal)
				.Select(attribute => new Hearer(attribute.LongName, attribute.Value)));
		}

		return hearers;
	}

	/// <summary>
	/// A stoppable event's attributes run in turn in one queue entry, as @include would run them, so one may call
	/// <c>stopevent()</c> to keep the ones after it from running.
	/// </summary>
	private async ValueTask QueueInTurnAsync(string eventName, DBRef handler, DBRef enactor,
		Dictionary<string, CallState> arguments, IReadOnlyList<Hearer> hearers)
	{
		var admission = await scheduler.Value.AdmitWork(async () =>
		{
			CallState? result = null;
			foreach (var hearer in hearers)
			{
				var stop = new EventStop();
				using (EventStop.Enter(stop))
				{
					result = await parser.Value
						.FromState(State(handler, enactor, arguments, hearer) with { ExecutionBudget = ExecutionBudget.Current })
						.CommandListParse(Body(hearer));
				}

				if (stop.Requested)
				{
					break;
				}
			}

			return result;
		}, $"event:{eventName}", "event", handler, notifyOnRejection: false);

		if (!admission.Accepted)
		{
			logger.LogWarning("Event {EventName} was not queued: {Reason}", eventName, admission.Reason);
		}
	}

	/// <summary>
	/// The body is re-wrapped as plain text (as @include does) so the stored markup cannot interfere with parsing.
	/// </summary>
	private static MString Body(Hearer hearer) => MarkupText.Plain(hearer.Body.ToPlainText());

	/// <summary>
	/// The arguments are %0-%9, and each also by its name in EventArguments. Every attribute gets its own copy.
	/// </summary>
	private static ParserState State(DBRef handler, DBRef enactor, Dictionary<string, CallState> arguments, Hearer hearer)
		=> ParserState.RootFor(handler) with
		{
			Enactor = enactor,
			Arguments = new Dictionary<string, CallState>(arguments, StringComparer.OrdinalIgnoreCase),
			EnvironmentRegisters = new(arguments, StringComparer.OrdinalIgnoreCase),
			CurrentEvaluation = new DBAttribute(handler, hearer.Attribute)
		};
}
