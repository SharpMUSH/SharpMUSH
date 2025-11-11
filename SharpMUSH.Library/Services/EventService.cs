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
	IOptionsWrapper<SharpMUSHOptions> options,
	ILogger<EventService> logger) : IEventService
{
	/// <inheritdoc />
	public async ValueTask TriggerEventAsync(IMUSHCodeParser parser, string eventName, DBRef? enactor, params string[] args)
	{
		try
		{
			// Get the event_handler config option
			var eventHandlerDbRef = options.CurrentValue.Database.EventHandler;
			
			// If no event handler is configured, return early
			if (eventHandlerDbRef is null or 0)
			{
				return;
			}
			
			// Get the event handler object from the database
			var eventHandlerRef = new DBRef((int)eventHandlerDbRef.Value, null);
			var eventHandlerResult = await mediator.Send(new GetObjectNodeQuery(eventHandlerRef));
			
			// If the event handler object doesn't exist, log warning and return
			if (eventHandlerResult.IsNone)
			{
				logger.LogWarning(
					"Event handler object #{EventHandlerDbRef} not found for event {EventName}",
					eventHandlerDbRef.Value,
					eventName);
				return;
			}
			
			var eventHandler = eventHandlerResult.Known;
			
			// Check if the event handler has an attribute matching the event name
			var attributeResult = await attributeService.GetAttributeAsync(
				eventHandler,
				eventHandler,
				eventName,
				IAttributeService.AttributeMode.Execute,
				parent: false);
			
			// If the attribute doesn't exist, return early (no handler for this event)
			// This is not an error - not all events need handlers
			if (!attributeResult.IsAttribute)
			{
				return;
			}
			
			// Build the arguments dictionary for the attribute execution
			// Arguments are passed as %0, %1, %2, etc. in the attribute code
			var argsDict = new Dictionary<string, CallState>();
			for (int i = 0; i < args.Length; i++)
			{
				argsDict[i.ToString()] = new CallState(args[i]);
			}
			
			// Determine the enactor for the event
			// PennMUSH uses #-1 for system events (no player enactor)
			var eventEnactor = enactor ?? new DBRef(-1, null);
			
			// Get the enactor object for passing to EvaluateAttributeFunctionAsync
			AnySharpObject enactorObject;
			if (eventEnactor.Number == -1)
			{
				// For system events (#-1), use the event handler as the enactor
				enactorObject = eventHandler;
			}
			else
			{
				var enactorResult = await mediator.Send(new GetObjectNodeQuery(eventEnactor));
				if (enactorResult.IsNone)
				{
					// If enactor doesn't exist, use the event handler as fallback
					logger.LogWarning(
						"Event enactor {Enactor} not found for event {EventName}, using event handler as enactor",
						eventEnactor,
						eventName);
					enactorObject = eventHandler;
				}
				else
				{
					enactorObject = enactorResult.Known;
				}
			}
			
			// Execute the event handler attribute with modified parser state
			// Set executor to event handler and enactor as determined above
			await parser.With(state => state with { 
				Executor = eventHandler.Object().DBRef,
				Enactor = eventEnactor 
			}, async newParser =>
			{
				// Execute the event handler attribute
				// ignorePermissions: true because events run with elevated privileges
				await attributeService.EvaluateAttributeFunctionAsync(
					newParser,
					enactorObject,
					eventHandler,
					eventName,
					argsDict,
					evalParent: false,
					ignorePermissions: true);
			});
			
			logger.LogDebug(
				"Triggered event {EventName} with {ArgCount} arguments",
				eventName,
				args.Length);
		}
		catch (Exception ex)
		{
			// Log error but don't propagate - event failures shouldn't break the triggering code
			logger.LogError(
				ex,
				"Error triggering event {EventName} with enactor {Enactor}",
				eventName,
				enactor);
		}
	}
}
