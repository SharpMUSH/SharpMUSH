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
using System.Text;

namespace SharpMUSH.Library.Services;

/// <inheritdoc />
public class HttpHandlerCommandService(
	IMediator mediator,
	IAttributeService attributeService,
	IMUSHCodeParser parser,
	IHttpOutputCapture outputCapture,
	IEventService eventService,
	IOptionsWrapper<SharpMUSHOptions> options,
	ILogger<HttpHandlerCommandService> logger) : IHttpHandlerCommandDispatcher
{
	/// <inheritdoc />
	public async ValueTask<Found<HttpHandlerResult>> DispatchAsync(
		string method,
		string path,
		string body,
		IEnumerable<(string Name, string Value)> headers,
		CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		var handlerDbRef = options.CurrentValue.Database.HttpHandler;
		if (handlerDbRef is null or 0)
		{
			logger.LogDebug("Inbound HTTP request but no http_handler is configured.");
			return new NotFound();
		}

		ct.ThrowIfCancellationRequested();
		var parentBudget = ExecutionBudget.Current;
		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, parentBudget?.Token ?? default);
		var milliseconds = options.CurrentValue.Limit.QueueEntryCpuTime;
		var duration = milliseconds == 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(milliseconds);
		if (parentBudget is not null && parentBudget.Remaining != TimeSpan.MaxValue
			&& (duration == Timeout.InfiniteTimeSpan || parentBudget.Remaining < duration))
			duration = parentBudget.Remaining;
		using var budget = new ExecutionBudget(duration, cancellation.Token);
		// The parent timer can fire before the independently armed child deadline.
		bool DeadlineExpired() => budget.IsExpired || parentBudget?.IsExpired == true;

		// One response context, reachable two ways during execution (Penn's `struct http_request`):
		// on the parser state for @respond, and in the output-capture frame for emitted output.
		var context = new HttpResponseContext();
		DBRef? handlerRef = null;

		using (budget.Enter())
		{
			try
			{
				budget.ThrowIfExceeded();
				var handlerResult = await mediator.Send(new GetObjectNodeQuery(new DBRef((int)handlerDbRef.Value, null)), budget.Token);
				if (handlerResult.IsNone)
				{
					logger.LogWarning("Configured http_handler #{HandlerDbRef} not found.", handlerDbRef.Value);
					return new NotFound();
				}

				var handler = handlerResult.Known;
				handlerRef = handler.Object().DBRef;
				budget.ThrowIfExceeded();
				using var capture = outputCapture.BeginCapture(handlerRef.Value.Number, context);
				// The <METHOD> attribute is the handler entry point: GET, POST, etc. — run as commands,
				// the equivalent of PennMUSH's `@include #handler/<method>`. SharpMUSH deviates from
				// Penn (200 + empty body) by answering 404 when the attribute is absent; see help sharphttp.
				var attributeName = method.ToUpperInvariant();
				var attributeResult = await attributeService.GetAttributeAsync(
					handler, handler, attributeName, IAttributeService.AttributeMode.Execute, parent: false);
				if (!attributeResult.IsAttribute)
				{
					return new NotFound();
				}

				// Build a fresh parser state — there is no ambient parser on the HTTP request path.
				// 'Invisible login': the handler is executor, enactor, and caller, as in Penn.
				var evalParser = parser.Push(ParserState.RootFor(handlerRef.Value) with
				{
					Registers = new([BuildHeaderRegisters(headers)]),
					EnvironmentRegisters = new Dictionary<string, CallState>
					{
						["0"] = new CallState(path),
						["1"] = new CallState(body)
					},
					ExecutionBudget = budget,
					HttpResponse = context
				});

				var attributeValue = attributeResult.AsAttribute.Last().Value;
				budget.ThrowIfExceeded();
				await evalParser.CommandListParse(attributeValue);
			}
			catch (OperationCanceledException) when (DeadlineExpired())
			{
				// Parser deadlines may return an error or throw while awaiting I/O.
				// Both discard any response accumulated before the deadline.
			}
		}

		ct.ThrowIfCancellationRequested();
		if (cancellation.IsCancellationRequested && !DeadlineExpired()) cancellation.Token.ThrowIfCancellationRequested();
		var result = DeadlineExpired()
			? new HttpHandlerResult(503, "Service Unavailable", "text/plain", [], ExecutionBudget.Error)
			: AssembleResult(context);

		// HTTP`COMMAND sysevent, mirroring Penn: ip is unknown at this layer (proxied), method,
		// path, code, ctype, request body length, response body length.
		if (!DeadlineExpired() && handlerRef is { } resolvedHandler)
		{
			using (budget.Enter())
			{
				try
				{
					budget.ThrowIfExceeded();
					await eventService.TriggerEventAsync(
						parser, "HTTP`COMMAND", resolvedHandler, budget.Token,
						string.Empty, method, path, result.Status.ToString(), result.ContentType,
						body.Length.ToString(), result.Body.Length.ToString());
				}
				catch (OperationCanceledException) when (DeadlineExpired()) { }
			}
		}

		ct.ThrowIfCancellationRequested();
		if (cancellation.IsCancellationRequested && !DeadlineExpired()) cancellation.Token.ThrowIfCancellationRequested();
		if (DeadlineExpired())
			return new HttpHandlerResult(503, "Service Unavailable", "text/plain", [], ExecutionBudget.Error);

		return result;
	}

	/// <summary>
	/// Seeds the q-registers Penn provides to HTTP handler code: one <c>HDR.&lt;NAME&gt;</c> register
	/// per header (duplicate headers joined with a newline, i.e. <c>%r</c>), plus <c>HEADERS</c>
	/// holding the space-separated list of header names.
	/// </summary>
	private static Dictionary<string, MString> BuildHeaderRegisters(IEnumerable<(string Name, string Value)> headers)
	{
		var registers = new Dictionary<string, MString>();
		var names = new List<string>();

		foreach (var (name, value) in headers)
		{
			var normalized = Utilities.RegisterNames.NormalizeSegment(name);
			if (normalized.Length == 0)
			{
				continue;
			}

			var key = $"HDR.{normalized}";
			if (registers.TryGetValue(key, out var existing))
			{
				registers[key] = MarkupText.Concat([existing, MarkupText.Plain("\n"), MarkupText.Plain(value)]);
			}
			else
			{
				registers[key] = MarkupText.Plain(value);
				names.Add(normalized);
			}
		}

		registers["HEADERS"] = MarkupText.Plain(string.Join(' ', names));
		return registers;
	}

	/// <summary>
	/// Reads the response out of the shared context: status line from <c>@respond</c> (default
	/// <c>200 OK</c>), content type from <c>@respond/type</c> (default <c>text/plain</c>), headers
	/// from <c>@respond/header</c>, and the captured output as the body.
	/// </summary>
	private static HttpHandlerResult AssembleResult(HttpResponseContext context)
	{
		if (context.OutputLimitExceeded)
		{
			return new HttpHandlerResult(
				500,
				"Internal Server Error",
				"text/plain",
				[],
				ErrorMessages.Returns.OutputTooLarge);
		}

		var statusLine = string.IsNullOrWhiteSpace(context.StatusLine) ? "200 OK" : context.StatusLine!.Trim();
		var spaceIndex = statusLine.IndexOf(' ');
		var codeText = spaceIndex > 0 ? statusLine[..spaceIndex] : statusLine;
		var reason = spaceIndex > 0 ? statusLine[(spaceIndex + 1)..].Trim() : string.Empty;
		var status = int.TryParse(codeText, out var parsed) ? parsed : 200;

		return new HttpHandlerResult(
			status,
			reason.Length > 0 ? reason : "OK",
			string.IsNullOrWhiteSpace(context.ContentType) ? "text/plain" : context.ContentType!,
			context.Headers,
			context.Body.ToString());
	}
}
