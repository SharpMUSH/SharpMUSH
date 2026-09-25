using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Requests;

namespace SharpMUSH.Implementation.Visitors;

/// <summary>
/// Queues a matched <c>$</c>-command body as a new entry, as PennMUSH's <c>parse_que_attr</c> does
/// (<c>src/attrib.c:2056-2093</c>) for every match that is not <c>QUEUE_INPLACE</c>.
/// </summary>
internal static class QueuedCommandMatch
{
	/// <param name="current">The state of the command that matched.</param>
	/// <param name="matcher">Who typed or ran the matching command; the queued body's enactor and caller.</param>
	public static async ValueTask Admit(IMediator mediator, ParserState current, AnySharpObject obj, SharpAttribute attr,
		Dictionary<string, CallState> arguments, DBRef? matcher, CancellationToken cancellationToken)
	{
		var body = attr.Value.Substring(attr.CommandListIndex!.Value, attr.Value.Length - attr.CommandListIndex!.Value);

		// parse_que_attr queues with PE_INFO_DEFAULT: fresh q-registers and no iteration, regex or switch context.
		await mediator.Send(new AdmitCommandListRequest(
			body,
			current.SnapshotForQueuedAction() with
			{
				CurrentEvaluation = new DBAttribute(obj.Object().DBRef, attr.Name),
				Registers = new([[]]),
				IterationRegisters = [],
				RegexRegisters = [],
				SwitchStack = [],
				EnvironmentRegisters = arguments,
				Arguments = arguments,
				Function = null,
				Executor = obj.Object().DBRef,
				Enactor = matcher,
				Caller = matcher,
				HttpResponse = null
			},
			new DbRefAttribute(obj.Object().DBRef, attr.LongName?.Split('`') ?? [attr.Name]),
			-1), cancellationToken);
	}
}
