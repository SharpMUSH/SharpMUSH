using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Softcode;
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
	private sealed record Hearer(string Attribute, MString Body, string? Dialect);

	/// <inheritdoc />
	public async ValueTask<bool> HasHandlerAsync(string eventName)
	{
		var eventHandlerDbRef = options.CurrentValue.Database.EventHandler;
		return eventHandlerDbRef is not (null or 0)
			&& await mediator.Send(new GetObjectNodeQuery(new DBRef((int)eventHandlerDbRef.Value, null))) is AnySharpObject eventHandler
			&& (await HearersAsync(eventHandler, eventName.ToUpperInvariant())).Count > 0;
	}

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
			var resolvedEnactorRef = await ResolveEnactorAsync(enactor);

			var handlerRef = eventHandler.Object().DBRef;
			var arguments = EventArguments.Of(name, args);
			// Another event keeps the depth of the code that raised it, so a chain that passes through one (a watch
			// handler that moves something, whose OBJECT`MOVE handler sets HP) is still counted.
			var run = new EventRun(handlerRef, resolvedEnactorRef, AttributeChangeChain.Depth);
			if (EventCatalogue.Find(name) is { Stoppable: true })
			{
				await QueueInTurnAsync(name, run, arguments, hearers);
				return;
			}

			// queue_event (src/cque.c:392-517) queues the handler's attribute as an entry of its own, run
			// by the handler and charged to it, so the code that raised the event never waits on it or
			// shares its time limit. Each attribute below it is queued the same way, as @trigger would.
			// The handler runs with its own permissions: the seeded Event Handler (#9) is a WIZARD object;
			// a custom, non-wizard handler runs unprivileged.
			foreach (var hearer in hearers)
			{
				var admission = await QueueHold.AdmitAsync(scheduler.Value, Body(hearer), State(run, arguments, hearer));
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

	/// <inheritdoc />
	public async ValueTask QueueAttributeWatchHandlersAsync(string pattern, DBRef? enactor, IReadOnlyList<MString> args)
	{
		try
		{
			if (await EventHandlerAsync() is not AnySharpObject eventHandler)
			{
				return;
			}

			var hearers = await WatchHandlersAsync(eventHandler, pattern);
			if (hearers.Count == 0)
			{
				return;
			}

			// Each handler runs one change deeper than the code that made the change (see AttributeChangeChain).
			var run = new EventRun(eventHandler.Object().DBRef, await ResolveEnactorAsync(enactor), AttributeChangeChain.Depth + 1);
			var arguments = NamedArguments(AttributeWatch.ArgumentNames, args);

			// As a non-stoppable event's attributes are: each its own queue entry, run by the handler with its own
			// permissions, so the write that caused it never waits on it.
			foreach (var hearer in hearers)
			{
				var admission = await QueueHold.AdmitAsync(scheduler.Value, Body(hearer), State(run, arguments, hearer));
				if (!admission.Accepted)
				{
					logger.LogWarning("Attribute watch handler {Attribute} was not queued: {Reason}", hearer.Attribute, admission.Reason);
				}
			}
		}
		catch (OperationCanceledException) when (ExecutionBudget.Current?.IsExceeded == true)
		{
			throw;
		}
		catch (Exception ex)
		{
			// A watch handler's failure must not break the write that caused it.
			logger.LogError(ex, "Error queueing attribute watch handlers {Pattern} with enactor {Enactor}", pattern, enactor);
		}
	}

	/// <summary>The configured event handler object, or <see cref="None"/> when none is set or it is gone.</summary>
	private async ValueTask<AnyOptionalSharpObject> EventHandlerAsync()
	{
		var eventHandlerDbRef = options.CurrentValue.Database.EventHandler;
		if (eventHandlerDbRef is null or 0)
		{
			return new None();
		}

		return await mediator.Send(new GetObjectNodeQuery(new DBRef((int)eventHandlerDbRef.Value, null)), ExecutionBudget.CurrentToken);
	}

	/// <summary>
	/// %# for a handler: the object that caused the event, or God (#1) for a system event or one whose enactor is gone,
	/// so that %# is always a real dbref inside handler code (PennMUSH uses #-1, which many functions reject).
	/// </summary>
	private async ValueTask<DBRef> ResolveEnactorAsync(DBRef? enactor)
		=> enactor is { Number: >= 0 } eventEnactor
			&& await mediator.Send(new GetObjectNodeQuery(eventEnactor), ExecutionBudget.CurrentToken) is AnySharpObject
				? eventEnactor
				: new DBRef(1, null);

	/// <summary>
	/// The handler's own attributes matching any wildcard in <paramref name="pattern"/>, each once, in name order. An
	/// empty attribute (a branch that only holds the ones below it) runs nothing.
	/// </summary>
	private async ValueTask<IReadOnlyList<Hearer>> WatchHandlersAsync(AnySharpObject eventHandler, string pattern)
	{
		var found = new Dictionary<string, Hearer>(StringComparer.Ordinal);
		foreach (var wildcard in AttributeWatch.Wildcards(pattern))
		{
			if (await attributeService.GetAttributePatternAsync(eventHandler, eventHandler, wildcard, checkParents: false,
						IAttributeService.AttributePatternMode.Wildcard) is not SharpAttribute[] matched)
			{
				continue;
			}

			foreach (var attribute in matched.Where(attribute => attribute.Value.Length > 0))
			{
				found.TryAdd(attribute.LongName, new Hearer(attribute.LongName, attribute.Value, SoftcodeDialects.Of(attribute)));
			}
		}

		return [.. found.Values.OrderBy(hearer => hearer.Attribute, StringComparer.Ordinal)];
	}

	/// <summary>%0-%9 by position, and each also by its name in <paramref name="names"/>.</summary>
	private static Dictionary<string, CallState> NamedArguments(IReadOnlyList<string> names, IReadOnlyList<MString> args)
	{
		var named = args.Index().ToDictionary(x => x.Index.ToString(), x => new CallState(x.Item), StringComparer.OrdinalIgnoreCase);
		foreach (var (name, value) in names.Zip(args))
		{
			named[name] = new CallState(value);
		}

		return named;
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
			hearers.Add(new Hearer(eventName, own.Last().Value, SoftcodeDialects.Of(own.Last())));
		}

		if (await attributeService.GetAttributePatternAsync(eventHandler, eventHandler, $"{eventName}`*", checkParents: false,
					IAttributeService.AttributePatternMode.Wildcard) is SharpAttribute[] below)
		{
			hearers.AddRange(below
				.Where(attribute => attribute.Value.Length > 0)
				.OrderBy(attribute => attribute.LongName, StringComparer.Ordinal)
				.Select(attribute => new Hearer(attribute.LongName, attribute.Value, SoftcodeDialects.Of(attribute))));
		}

		return hearers;
	}

	/// <summary>
	/// A stoppable event's attributes run in turn in one queue entry, as @include would run them, so one may call
	/// <c>stopevent()</c> to keep the ones after it from running.
	/// </summary>
	private async ValueTask QueueInTurnAsync(string eventName, EventRun run,
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
						.FromState(State(run, arguments, hearer) with { ExecutionBudget = ExecutionBudget.Current })
						.CommandListParse(Body(hearer));
				}

				if (stop.Requested)
				{
					break;
				}
			}

			return result;
		}, $"event:{eventName}", "event", run.Handler, run.Enactor, notifyOnRejection: false);

		if (!admission.Accepted)
		{
			logger.LogWarning("Event {EventName} was not queued: {Reason}", eventName, admission.Reason);
		}
	}

	/// <summary>
	/// The body is re-wrapped as plain text (as @include does) so the stored markup cannot interfere with parsing.
	/// </summary>
	private static MString Body(Hearer hearer) => MarkupText.Plain(hearer.Body.ToPlainText());

	/// <summary>Who runs an event's attributes, as whom, and how many attribute changes stand behind it.</summary>
	private readonly record struct EventRun(DBRef Handler, DBRef Enactor, int AttributeChangeDepth);

	/// <summary>
	/// The arguments are %0-%9, and each also by its name in EventArguments. Every attribute gets its own copy.
	/// </summary>
	private static ParserState State(EventRun run, Dictionary<string, CallState> arguments, Hearer hearer)
		=> ParserState.RootFor(run.Handler) with
		{
			Enactor = run.Enactor,
			Arguments = new Dictionary<string, CallState>(arguments, StringComparer.OrdinalIgnoreCase),
			EnvironmentRegisters = new(arguments, StringComparer.OrdinalIgnoreCase),
			SuppliedArgumentKeys = AttributeArgumentEnvironment.SuppliedKeys(arguments),
			CurrentEvaluation = new DBAttribute(run.Handler, hearer.Attribute) { Dialect = hearer.Dialect },
			AttributeChangeDepth = run.AttributeChangeDepth
		};
}
