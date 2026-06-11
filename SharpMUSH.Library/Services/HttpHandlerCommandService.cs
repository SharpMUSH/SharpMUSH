using Mediator;
using Microsoft.Extensions.Logging;
using OneOf;
using OneOf.Types;
using SharpMUSH.Configuration.Options;
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
	public async ValueTask<OneOf<HttpHandlerResult, NotFound>> DispatchAsync(
		string method,
		string path,
		string body,
		IEnumerable<(string Name, string Value)> headers,
		CancellationToken ct = default)
	{
		var handlerDbRef = options.CurrentValue.Database.HttpHandler;
		if (handlerDbRef is null or 0)
		{
			logger.LogDebug("Inbound HTTP request but no http_handler is configured.");
			return new NotFound();
		}

		var handlerResult = await mediator.Send(new GetObjectNodeQuery(new DBRef((int)handlerDbRef.Value, null)), ct);
		if (handlerResult.IsNone)
		{
			logger.LogWarning("Configured http_handler #{HandlerDbRef} not found.", handlerDbRef.Value);
			return new NotFound();
		}

		var handler = handlerResult.Known;
		var handlerRef = handler.Object().DBRef;

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

		// One response context, reachable two ways during execution (Penn's `struct http_request`):
		// on the parser state for @respond, and in the output-capture frame for emitted output.
		var context = new HttpResponseContext();

		// Build a fresh parser state — there is no ambient parser on the HTTP request path.
		// 'Invisible login': the handler is executor, enactor, and caller, as in Penn.
		var evalParser = parser.Push(new ParserState(
			Registers: new([BuildHeaderRegisters(headers)]),
			IterationRegisters: [],
			RegexRegisters: [],
			SwitchStack: [],
			ExecutionStack: [],
			EnvironmentRegisters: new Dictionary<string, CallState>
			{
				["0"] = new CallState(path),
				["1"] = new CallState(body)
			},
			CurrentEvaluation: null,
			ParserFunctionDepth: 0,
			Function: null,
			Command: null,
			CommandInvoker: _ => ValueTask.FromResult(new Option<CallState>(new None())),
			Switches: [],
			Arguments: [],
			Executor: handlerRef,
			Enactor: handlerRef,
			Caller: handlerRef,
			Handle: null,
			ParseMode: ParseMode.Default,
			HttpResponse: context,
			CallDepth: new InvocationCounter(),
			FunctionRecursionDepths: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
			TotalInvocations: new InvocationCounter(),
			LimitExceeded: new LimitExceededFlag()));

		var attributeValue = attributeResult.AsAttribute.Last().Value;

		using (outputCapture.BeginCapture(handlerRef.Number, context))
		{
			await evalParser.CommandListParse(attributeValue);
		}

		var result = AssembleResult(context);

		// HTTP`COMMAND sysevent, mirroring Penn: ip is unknown at this layer (proxied), method,
		// path, code, ctype, request body length, response body length.
		await eventService.TriggerEventAsync(
			parser, "HTTP`COMMAND", handlerRef,
			string.Empty, method, path, result.Status.ToString(), result.ContentType,
			body.Length.ToString(), result.Body.Length.ToString());

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
			var normalized = NormalizeHeaderKey(name);
			if (normalized.Length == 0)
			{
				continue;
			}

			var key = $"HDR.{normalized}";
			if (registers.TryGetValue(key, out var existing))
			{
				registers[key] = MModule.multiple([existing, MModule.single("\n"), MModule.single(value)]);
			}
			else
			{
				registers[key] = MModule.single(value);
				names.Add(normalized);
			}
		}

		registers["HEADERS"] = MModule.single(string.Join(' ', names));
		return registers;
	}

	/// <summary>
	/// Normalizes a header name into a q-register-acceptable key (Penn's <c>pi_regs_normalize_key</c>):
	/// uppercased, with anything outside [A-Z0-9_.-] replaced by an underscore.
	/// </summary>
	private static string NormalizeHeaderKey(string name)
	{
		var builder = new StringBuilder(name.Length);
		foreach (var c in name.ToUpperInvariant())
		{
			builder.Append(c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '.' or '-' ? c : '_');
		}

		return builder.ToString();
	}

	/// <summary>
	/// Reads the response out of the shared context: status line from <c>@respond</c> (default
	/// <c>200 OK</c>), content type from <c>@respond/type</c> (default <c>text/plain</c>), headers
	/// from <c>@respond/header</c>, and the captured output as the body.
	/// </summary>
	private static HttpHandlerResult AssembleResult(HttpResponseContext context)
	{
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
