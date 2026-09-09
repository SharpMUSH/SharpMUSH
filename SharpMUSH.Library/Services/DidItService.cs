using Mediator;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public class DidItService(
	IMediator mediator,
	IAttributeService attributeService,
	INotifyService notifyService,
	ICommunicationService communicationService) : IDidItService
{
	public async ValueTask<bool> DidIt(IMUSHCodeParser parser, DidItRequest request)
	{
		var used = false;
		var loc = request.Loc ?? (request.Player.IsContent ? await request.Player.AsContent.Location() : null);

		// PennMUSH guards only the messages on a good location; the action attribute runs regardless.
		if (loc is not null)
		{
			var args = BuildArgs(request);

			if (!string.IsNullOrEmpty(request.What))
			{
				var attr = await attributeService.GetAttributeAsync(
					request.Thing, request.Thing, request.What,
					IAttributeService.AttributeMode.Execute, parent: true);

				if (attr.IsAttribute)
				{
					used = true;
					var message = await Evaluate(parser, request, request.What, args);

					if (!string.IsNullOrEmpty(message.ToPlainText()))
					{
						await notifyService.Notify(request.Player, message, request.Thing);
					}
				}
				else if (request.Def is not null && request.Def.Length > 0)
				{
					await notifyService.Notify(request.Player, request.Def, request.Thing);
				}
			}

			// A Dark object that is legally dark produces no o-messages at all.
			if (!await request.Player.IsDarkLegal())
			{
				MString? broadcast = null;
				var oattrFound = false;

				if (!string.IsNullOrEmpty(request.OWhat))
				{
					var oattr = await attributeService.GetAttributeAsync(
						request.Thing, request.Thing, request.OWhat,
						IAttributeService.AttributeMode.Execute, parent: true);

					if (oattr.IsAttribute)
					{
						oattrFound = true;
						used = true;
						var evaluated = await Evaluate(parser, request, request.OWhat, args);

						// UFUN_NAME (src/utils.c:351): the actor's name and a space go in front of the
						// evaluated text, and an attribute that evaluated to nothing sends nothing —
						// the name alone is stripped back off.
						if (!string.IsNullOrEmpty(evaluated.ToPlainText()))
						{
							broadcast = MarkupText.Concat(
								MarkupText.Plain($"{request.Player.Object().Name} "), evaluated);
						}
					}
				}

				// The odef branch is an `else if` on the FETCH in real_did_it, not on the result: an
				// o-attribute that exists and evaluates to nothing stays silent rather than falling
				// back to the default.
				if (!oattrFound && !string.IsNullOrEmpty(request.ODef))
				{
					broadcast = MarkupText.Plain($"{request.Player.Object().Name} {request.ODef}");
				}

				if (broadcast is not null)
				{
					var message = broadcast;

					// notify_except2(player, loc, player, thing, ...): the actor is both the executor and
					// the speaker of the o-message, and the actor and the object are the two exclusions.
					await communicationService.SendToRoomAsync(
						request.Player,
						loc,
						_ => message,
						INotifyService.NotificationType.Emit,
						excludeObjects: [request.Player, request.Thing],
						interact: request.Interact);
				}
			}
		}

		if (!string.IsNullOrEmpty(request.AWhat))
		{
			used = await QueueAction(parser, request) || used;
		}

		return used;
	}

	public async ValueTask<bool> FailLock(
		IMUSHCodeParser parser,
		AnySharpObject player,
		AnySharpObject thing,
		LockType lockType,
		MString? def = null,
		AnySharpContainer? loc = null)
	{
		var (what, owhat, awhat) = LockMessages.FailureAttributes(lockType);

		return await DidIt(parser, new DidItRequest(
			Player: player,
			Thing: thing,
			What: what,
			Def: def,
			OWhat: owhat,
			AWhat: awhat,
			Loc: loc));
	}

	/// <summary>
	/// Evaluates one of the triad's message attributes the way <c>call_ufun(&amp;ufun, buff, thing,
	/// player, …)</c> does: the attribute runs as <c>Thing</c>, with <c>Thing</c> as <c>%@</c> and
	/// <c>Player</c> as <c>%#</c>.
	/// </summary>
	/// <remarks>
	/// The state is pushed onto the caller's, not built fresh. <see cref="ParserState.FunctionRecursionDepths"/>
	/// and the invocation counters are what bound an attribute that evaluates itself; a fresh state
	/// would restart both and turn a bounded recursion into stack exhaustion. PennMUSH can afford the
	/// fresh <c>pe_info</c> in <c>real_did_it</c> only because its action attribute is queued rather
	/// than called.
	/// </remarks>
	private async ValueTask<MString> Evaluate(
		IMUSHCodeParser parser,
		DidItRequest request,
		string attribute,
		Dictionary<string, CallState> args)
	{
		var thing = request.Thing.Object().DBRef;
		var player = request.Player.Object().DBRef;

		return await parser.With(
			state => state with { Executor = thing, Enactor = player, Caller = thing },
			async evaluatingParser => await attributeService.EvaluateAttributeFunctionAsync(
				evaluatingParser, request.Thing, request.Thing, attribute, args,
				evalParent: true, ignorePermissions: true));
	}

	private static Dictionary<string, CallState> BuildArgs(DidItRequest request)
	{
		var args = new Dictionary<string, CallState>();

		if (request.Env0 is not null)
		{
			args["0"] = new CallState(request.Env0.Value.ToString());
		}

		if (request.Env1 is not null)
		{
			args["1"] = new CallState(request.Env1.Value.ToString());
		}

		return args;
	}

	/// <summary>
	/// Queues the action attribute as its own queue entry, with <c>Thing</c> as executor and
	/// <c>Player</c> as enactor. PennMUSH <c>queue_attribute_base</c>.
	/// </summary>
	/// <remarks>
	/// The state captures DBRefs rather than loaded objects, so the parser resolves them when the
	/// queue drains — which may be many commands later, and a loaded object would be a snapshot taken
	/// before whatever write triggered the action. The action <i>text</i>, by contrast, is snapshotted
	/// here: <c>queue_attribute_useatr</c> copies <c>atr_value(a)</c> into its own buffer at queue
	/// time (<c>src/cque.c:840</c>), so an attribute rewritten before the entry runs still runs the
	/// value that was read when the triad fired.
	/// </remarks>
	private async ValueTask<bool> QueueAction(IMUSHCodeParser parser, DidItRequest request)
	{
		var thing = request.Thing;

		// SharpMUSH deviation from PennMUSH: queue_attribute_useatr queues the action list without
		// consulting the object's flags, so in Penn a HALTed object still runs its @a-attributes.
		// SharpMUSH treats HALT as "runs none of its softcode" everywhere — the same rule
		// AttributeService applies to u() and the $-command matcher — because @halt is what an owner
		// reaches for to stop a runaway object, and an object whose actions keep queueing is not
		// stopped. The o-message is plain text and still goes out.
		if (await thing.HasFlag("HALT"))
		{
			return false;
		}

		var attr = await attributeService.GetAttributeAsync(
			thing, thing, request.AWhat!, IAttributeService.AttributeMode.Execute, parent: true);

		if (!attr.IsAttribute || attr.AsAttribute.Length == 0)
		{
			return false;
		}

		var command = StripCommandPrefix(attr.AsAttribute.Last().Value);

		// queue_attribute_base (src/cque.c:786-795) returns 1 as soon as the attribute is found, so a
		// present-but-empty action attribute still counts as used for fail_lock's return value even
		// though there is nothing to run.
		if (string.IsNullOrEmpty(command.ToPlainText()))
		{
			return true;
		}

		var executor = thing.Object().DBRef;
		var enactor = request.Player.Object().DBRef;
		var args = BuildArgs(request);
		var attributePath = attr.AsAttribute.Last().LongName!.Split("`");
		var baseState = parser.CurrentState;

		// queue_attribute_useatr queues with PE_INFO_DEFAULT (src/cque.c:867), which gives the action
		// its own pe_info: fresh q-registers, fresh recursion depths and fresh invocation counts. The
		// collections and counters on ParserState are reference types, and the queue entry runs on the
		// scheduler's own thread while the command that queued it is still running, so sharing the
		// caller's would be both a data race and a semantic leak — setq() in the action would write the
		// caller's register frame, and an already-tripped LimitExceeded would silence the action.
		//
		// CommandHistory, BreakPropagation and HttpResponse go with them. Each is a channel back into
		// the frame that queued the action, and a queue entry is a new top-level command list with no
		// such frame: @retry in the action would replay the triggering command, an @break would be
		// re-raised by an @include the action never ran under, and @respond would edit an HTTP
		// response that was assembled and sent long before the queue drained.
		await mediator.Send(new QueueCommandListRequest(
			command,
			baseState with
			{
				Executor = executor,
				Enactor = enactor,
				Caller = enactor,
				Arguments = args,
				EnvironmentRegisters = args,
				Registers = new([[]]),
				IterationRegisters = [],
				RegexRegisters = [],
				SwitchStack = [],
				ExecutionStack = [],
				CallDepth = new InvocationCounter(),
				FunctionRecursionDepths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
				TotalInvocations = new InvocationCounter(),
				LimitExceeded = new LimitExceededFlag(),
				MoveDepth = new InvocationCounter(),
				CommandHistory = null,
				BreakPropagation = null,
				HttpResponse = null
			},
			new DbRefAttribute(executor, attributePath),
			-1));

		return true;
	}

	/// <summary>
	/// Strips a <c>$command</c> / <c>^listen</c> prefix off an action list before it is queued, the
	/// way <c>queue_attribute_useatr</c> does (<c>src/cque.c:842-857</c>): from a leading <c>$</c> or
	/// <c>^</c> up to and including the first colon not preceded by a backslash. A value that opens
	/// with one of those characters but carries no unescaped colon is not a prefixed command, and is
	/// queued whole.
	/// </summary>
	private static MString StripCommandPrefix(MString value)
	{
		var plain = value.ToPlainText();

		if (plain.Length == 0 || (plain[0] != '$' && plain[0] != '^'))
		{
			return value;
		}

		for (var index = 0; index < plain.Length; index++)
		{
			if (plain[index] == '\\')
			{
				// The backslash escapes whatever follows it, colon included, and stays in the queued
				// text — Penn advances past both characters without copying either out.
				index++;
				continue;
			}

			if (plain[index] == ':')
			{
				return value.Substring(index + 1);
			}
		}

		return value;
	}
}
