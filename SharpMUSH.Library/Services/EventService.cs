using Mediator;
using Microsoft.Extensions.Logging;
using OneOf.Types;
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
			var handlerRef = eventHandler.Object().DBRef;

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
			for (var i = 0; i < args.Length; i++)
			{
				argsDict[i.ToString()] = new CallState(args[i]);
			}

			// Build a fresh parser state with the event arguments bound as %0, %1, ...
			// This mirrors the HTTP handler pattern (HttpHandlerCommandService) and the startup
			// bootstrap pattern (StartupAttributeBootstrapService): when there is no ambient parse
			// context, push a minimal state rather than calling parser.CurrentState (which throws
			// on an empty ImmutableStack). Using CommandListParse (not FunctionParse) so that the
			// attribute body can run commands such as &attr obj=val, @emit, @switch, etc.
			//
			// Both Executor and Enactor are set to handlerRef (the event_handler object) so that:
			// 1. The event handler runs as itself (%! = %# = handler), matching PennMUSH semantics.
			// 2. Locate() permission checks pass: Nearby(handler, handler) is always true, allowing
			//    the handler's attribute code to find and modify objects like itself.
			// The real "who caused this event" is already carried in the event args (%0, %1, …).
			var evalParser = parser.State.IsEmpty
				? parser.Push(new ParserState(
					Registers: new([[]]),
					IterationRegisters: [],
					RegexRegisters: [],
					SwitchStack: [],
					ExecutionStack: [],
					EnvironmentRegisters: argsDict,
					CurrentEvaluation: null,
					ParserFunctionDepth: 0,
					Function: null,
					Command: null,
					CommandInvoker: _ => ValueTask.FromResult(new Option<CallState>(new None())),
					Switches: [],
					Arguments: argsDict,
					Executor: handlerRef,
					Enactor: handlerRef,
					Caller: handlerRef,
					Handle: null,
					CallDepth: new InvocationCounter(),
					FunctionRecursionDepths: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
					TotalInvocations: new InvocationCounter(),
					LimitExceeded: new LimitExceededFlag()))
				: parser.Push(new ParserState(
					Registers: new([[]]),
					IterationRegisters: [],
					RegexRegisters: [],
					SwitchStack: [],
					ExecutionStack: [],
					EnvironmentRegisters: argsDict,
					CurrentEvaluation: null,
					ParserFunctionDepth: 0,
					Function: null,
					Command: null,
					CommandInvoker: parser.CurrentState.CommandInvoker,
					Switches: [],
					Arguments: argsDict,
					Executor: handlerRef,
					Enactor: handlerRef,
					Caller: handlerRef,
					Handle: parser.CurrentState.Handle,
					CallDepth: parser.CurrentState.CallDepth ?? new InvocationCounter(),
					FunctionRecursionDepths: parser.CurrentState.FunctionRecursionDepths ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
					TotalInvocations: parser.CurrentState.TotalInvocations ?? new InvocationCounter(),
					LimitExceeded: parser.CurrentState.LimitExceeded ?? new LimitExceededFlag()));

			// Run the attribute body as a command list (same as @include, HTTP handler, @startup).
			// This allows commands such as & (attribute set), @emit, @switch, etc.
			// Convert to plain text then re-wrap (matching @include's behaviour) so that any
			// markup encoding in the stored MString does not interfere with ANTLR parsing.
			var attributeText = attributeResult.AsAttribute.Last().Value.ToPlainText();

			await evalParser.CommandListParse(MModule.single(attributeText));

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
