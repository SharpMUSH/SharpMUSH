using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
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
/// </summary>
public class EventService(
	IMediator mediator,
	IAttributeService attributeService,
	Lazy<ITaskScheduler> scheduler,
	IOptionsWrapper<SharpMUSHOptions> options,
	ILogger<EventService> logger) : IEventService
{
	/// <inheritdoc />
	public async ValueTask TriggerEventAsync(string eventName, DBRef? enactor, params string[] args)
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

			var handlerRef = eventHandler.Object().DBRef;

			// atr_get_noparent (src/cque.c:418): an event is the handler's own attribute, never a parent's.
			var attributeResult = await attributeService.GetAttributeAsync(
				eventHandler,
				eventHandler,
				eventName,
				IAttributeService.AttributeMode.Execute,
				parent: false);

			// Not every event needs a handler; a missing one queues nothing.
			if (attributeResult is not SharpAttribute[] handler)
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

			// queue_event (src/cque.c:392-517) queues the handler's attribute as an entry of its own, run
			// by the handler and charged to it, so the code that raised the event never waits on it or
			// shares its time limit. The handler runs with its own permissions: the seeded Event Handler
			// (#9) is a WIZARD object; a custom, non-wizard handler runs unprivileged. The arguments are
			// %0-%9. The body is re-wrapped as plain text (as @include does) so the stored markup cannot
			// interfere with parsing.
			var arguments = args.Index().ToDictionary(x => x.Index.ToString(), x => new CallState(x.Item));
			var state = ParserState.RootFor(handlerRef) with
			{
				Enactor = resolvedEnactorRef,
				Arguments = arguments,
				EnvironmentRegisters = new(arguments),
				CurrentEvaluation = new DBAttribute(handlerRef, eventName)
			};

			var admission = await QueueHold.AdmitAsync(scheduler.Value, MarkupText.Plain(handler.Last().Value.ToPlainText()), state);
			if (!admission.Accepted)
			{
				logger.LogWarning("Event {EventName} was not queued: {Reason}", eventName, admission.Reason);
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
}
