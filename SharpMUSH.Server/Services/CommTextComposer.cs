using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// A recalled channel line's <c>text</c>, composed the way the <c>comm-feed</c> package composes the pushed
/// <c>comm.message</c> for the same line: through <c>FN`COMM`TEXT</c> on the configured event handler.
/// </summary>
/// <remarks>
/// <para>The push runs <c>CHANNEL`MESSAGE</c> on the handler with the speaker as enactor
/// (<c>EventService</c>), which calls <c>u(me/FN`COMM`TEXT,style,name,message)</c>. So here too the
/// executor and caller are the handler, the enactor is the speaker (God when the speaker is gone, as the
/// event falls back to), and the arguments are the style, the name as the line names it (empty for an
/// <c>@cemit</c>) and the message. A game that redefined the attribute gets the same text pulled as pushed.</para>
/// <para>Only when there is no handler or no such attribute on it is the bundled default used. Each line
/// runs under the queue-entry time limit a push runs under; a line that exceeds it, or whose softcode
/// throws, falls back to the default rather than failing the request.</para>
/// </remarks>
public sealed class CommTextComposer(
	IMediator mediator,
	IAttributeService attributeService,
	IMUSHCodeParser parser,
	IOptionsWrapper<SharpMUSHOptions> options,
	ILogger<CommTextComposer> logger)
{
	public const string Attribute = "FN`COMM`TEXT";

	/// <summary>The handler object, if the game has one carrying <see cref="Attribute"/>.</summary>
	public async ValueTask<AnySharpObject?> HandlerAsync(CancellationToken ct)
	{
		if (options.CurrentValue.Database.EventHandler is not { } number || number == 0
			|| await mediator.Send(new GetObjectNodeQuery(new DBRef((int)number)), ct) is not AnySharpObject handler)
			return null;

		return await attributeService.GetAttributeAsync(handler, handler, Attribute, IAttributeService.AttributeMode.Read)
			is SharpAttribute[]? handler
			: null;
	}

	/// <summary>The text for <paramref name="line"/>: through <paramref name="handler"/>'s attribute when given.</summary>
	public async ValueTask<string> ComposeAsync(AnySharpObject? handler, SharpChannelMessage line, CancellationToken ct)
	{
		if (handler is null) return Default(line.Style, line.SpeakerName, line.MessageText);

		var handlerRef = handler.Object().DBRef;
		var enactor = await mediator.Send(new GetObjectNodeQuery(line.Sender), ct) is AnySharpObject
			? line.Sender
			: new DBRef(1);
		var args = new Dictionary<string, CallState>
		{
			["0"] = new(line.Style),
			["1"] = new(line.SpeakerName),
			["2"] = new(line.MessageText)
		};

		using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
		using var budget = ExecutionBudget.FromMilliseconds(
			options.CurrentValue.Limit.QueueEntryCpuTime, linked.Token);
		using var scope = budget.Enter();
		try
		{
			var context = parser.FromState(ParserState.RootFor(handlerRef) with
			{
				Enactor = enactor,
				Caller = handlerRef,
				ExecutionBudget = budget
			});
			var text = await attributeService.EvaluateAttributeFunctionAsync(context, handler, handler, Attribute, args);
			budget.ThrowIfExceeded();
			return text.ToPlainText();
		}
		catch (OperationCanceledException) when (!ct.IsCancellationRequested)
		{
			logger.LogWarning("{Attribute} on {Handler} ran out of time composing a recalled line", Attribute, handlerRef);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogWarning(ex, "{Attribute} on {Handler} failed composing a recalled line", Attribute, handlerRef);
		}

		return Default(line.Style, line.SpeakerName, line.MessageText);
	}

	/// <summary>The bundled <c>FN`COMM`TEXT</c>: <c>switch(%0,pose,%1 %2,semipose,%1%2,%2)</c>.</summary>
	public static string Default(string style, string name, string message) => style switch
	{
		"pose" => $"{name} {message}",
		"semipose" => name + message,
		_ => message
	};
}
