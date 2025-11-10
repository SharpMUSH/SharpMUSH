using Mediator;
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
/// </summary>
public class EventService(
	IMediator mediator,
	IAttributeService attributeService,
	IOptionsWrapper<SharpMUSHOptions> options) : IEventService
{
	/// <inheritdoc />
	public async ValueTask TriggerEventAsync(IMUSHCodeParser parser, string eventName, DBRef? enactor, params string[] args)
	{
		// Get the event_handler config option
		var eventHandlerDbRef = options.CurrentValue.Database.EventHandler;
		
		// If no event handler is configured, return early
		if (eventHandlerDbRef == null || eventHandlerDbRef == 0)
		{
			return;
		}
		
		// Get the event handler object from the database
		var eventHandlerRef = new DBRef((int)eventHandlerDbRef.Value, null);
		var eventHandlerResult = await mediator.Send(new GetObjectNodeQuery(eventHandlerRef));
		
		// If the event handler object doesn't exist, return early
		if (eventHandlerResult.IsNone)
		{
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
		if (!attributeResult.IsAttribute)
		{
			return;
		}
		
		// Build the arguments dictionary for the attribute execution
		var argsDict = new Dictionary<string, CallState>();
		for (int i = 0; i < args.Length; i++)
		{
			argsDict[i.ToString()] = new CallState(args[i]);
		}
		
		// Determine the enactor for the event
		// Use the provided enactor, or create a system enactor (#-1) if null
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
				// If enactor doesn't exist, use the event handler
				enactorObject = eventHandler;
			}
			else
			{
				enactorObject = enactorResult.Known;
			}
		}
		
		// Execute the event handler attribute with modified parser state
		await parser.With(state => state with { 
			Executor = eventHandler.Object().DBRef,
			Enactor = eventEnactor 
		}, async newParser =>
		{
			// Execute the event handler attribute
			await attributeService.EvaluateAttributeFunctionAsync(
				newParser,
				enactorObject,
				eventHandler,
				eventName,
				argsDict,
				evalParent: false,
				ignorePermissions: true);
		});
	}
}
