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
			var eventHandlerDbRef = options.CurrentValue.Database.EventHandler;

			if (eventHandlerDbRef is null or 0)
			{
				return;
			}

			var eventHandlerRef = new DBRef((int)eventHandlerDbRef.Value, null);
			var eventHandlerResult = await mediator.Send(new GetObjectNodeQuery(eventHandlerRef));

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

			// Resolve the enactor (%#) for this event.
			// PennMUSH contract: %# is the object that caused the event (e.g. the player who
			// connected, the wizard who ran @tel). For system events with no real actor, enactor
			// is null → we fall back to #-1, and then to the handler object itself so that
			// %# is never a non-existent dbref inside handler code.
			var eventEnactorRef = enactor ?? new DBRef(-1, null);
			DBRef resolvedEnactorRef;
			if (eventEnactorRef.Number < 0)
			{
				// System event: no real actor — handler runs as God (elevated).
				resolvedEnactorRef = new DBRef(1, null);
			}
			else
			{
				var enactorResult = await mediator.Send(new GetObjectNodeQuery(eventEnactorRef));
				if (enactorResult.IsNone)
				{
					// Enactor no longer exists — fall back to God.
					logger.LogWarning(
						"Event enactor {Enactor} not found for event {EventName}, using God as enactor",
						eventEnactorRef,
						eventName);
					resolvedEnactorRef = new DBRef(1, null);
				}
				else
				{
					resolvedEnactorRef = eventEnactorRef;
				}
			}

			// Build a fresh parser state with the event arguments bound as %0, %1, ...
			// This mirrors the HTTP handler pattern (HttpHandlerCommandService) and the startup
			// bootstrap pattern (StartupAttributeBootstrapService): when there is no ambient parse
			// context, push a minimal state rather than calling parser.CurrentState (which throws
			// on an empty ImmutableStack). Using CommandListParse (not FunctionParse) so that the
			// attribute body can run commands such as &attr obj=val, @emit, @switch, etc.
			//
			// Executor = God (#1) — the "ignorePermissions: true" mechanism from the original
			//   EvaluateAttributeFunctionAsync path: God is IsSee_All and IsGod, so the Nearby
			//   check in Locate() is bypassed and CanSet() always succeeds. This matches the
			//   elevated-permissions semantics events require.
			// Enactor = resolvedEnactorRef (%# — the object that caused the event)
			// Caller  = handlerRef (%@ — who triggered this evaluation; the handler itself)
			//
			// Note: %! inside the handler will be #1 (God). If softcode needs the handler object,
			// it can use num(handler_name) or a named q-register. This is the same trade-off as
			// the original EvaluateAttributeFunctionAsync(ignorePermissions:true) path.
			var godRef = new DBRef(1, null);
			var isEmpty = parser.State.IsEmpty;
			var evalParser = parser.Push(new ParserState(
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
				CommandInvoker: isEmpty
					? _ => ValueTask.FromResult(new Option<CallState>(new None()))
					: parser.CurrentState.CommandInvoker,
				Switches: [],
				Arguments: argsDict,
				Executor: godRef,
				Enactor: resolvedEnactorRef,
				Caller: handlerRef,
				Handle: isEmpty ? null : parser.CurrentState.Handle,
				CallDepth: isEmpty ? new InvocationCounter() : parser.CurrentState.CallDepth ?? new InvocationCounter(),
				FunctionRecursionDepths: isEmpty
					? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
					: parser.CurrentState.FunctionRecursionDepths ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
				TotalInvocations: isEmpty ? new InvocationCounter() : parser.CurrentState.TotalInvocations ?? new InvocationCounter(),
				LimitExceeded: isEmpty ? new LimitExceededFlag() : parser.CurrentState.LimitExceeded ?? new LimitExceededFlag()));

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
