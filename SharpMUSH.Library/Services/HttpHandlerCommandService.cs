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
	ITaskScheduler scheduler,
	IOptionsWrapper<SharpMUSHOptions> options,
	ILogger<HttpHandlerCommandService> logger) : IHttpHandlerCommandDispatcher
{
	/// <inheritdoc />
	public ValueTask<Found<HttpHandlerResult>> DispatchAsync(
		string method,
		string path,
		string body,
		IEnumerable<(string Name, string Value)> headers,
		CancellationToken ct = default)
		=> DispatchAsync(method, path, body, headers, IHttpHandlerCommandDispatcher.UnknownAddress, null, ct);

	/// <inheritdoc />
	public async ValueTask<Found<HttpHandlerResult>> DispatchAsync(
		string method,
		string path,
		string body,
		IEnumerable<(string Name, string Value)> headers,
		string clientIp,
		DBRef? viewer,
		CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		path = WithoutCredentialParameters(path);
		var configuration = options.CurrentValue.Database;
		var handlerDbRef = configuration.HttpHandler;
		if (handlerDbRef is null or 0)
		{
			logger.LogDebug("Inbound HTTP request but no http_handler is configured.");
			return new NotFound();
		}

		// PennMUSH refuses an HTTP request outright while http_per_second is below one, in the same
		// breath as an unusable http_handler (src/bsd.c:3740-3743, reason "No HTTPHandler") — zero is
		// how an operator turns the softcode HTTP surface off, not merely how they throttle it. The
		// per-second quota itself is admission control one layer up, on the route.
		if (configuration.HttpRequestsPerSecond < 1)
		{
			logger.LogDebug("Inbound HTTP request but http_per_second is {PerSecond}; the HTTP surface is off.",
				configuration.HttpRequestsPerSecond);
			return new NotFound();
		}

		// PennMUSH runs the request as a queue entry in the main loop (run_http_command, src/cque.c:1092),
		// never beside softcode. It is a socket-type entry, so like run_user_input it is not charged to a
		// queue quota (do_entry skips it for QUEUE_SOCKET) — hence AdmitSocketWork, which counts it against
		// the global limit only. The consumer is a single reader, so the handler cannot interleave with
		// another queue entry.
		ct.ThrowIfCancellationRequested();
		var handler = handlerDbRef.Value;
		var parentBudget = ExecutionBudget.Current;
		var completion = new TaskCompletionSource<Found<HttpHandlerResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
		var abandoned = false;
		var admission = await scheduler.AdmitSocketWork(async () =>
		{
			// A request that gave up while waiting its turn (or whose token has since been disposed) has
			// nothing left to answer.
			if (Volatile.Read(ref abandoned)) return null;
			var execution = ExecuteAsync(method, path, body, headers, clientIp, viewer, handler, parentBudget, ct).AsTask();
			// The outcome, a fault included, belongs to the request waiting on it, not to the queue.
			await ((Task)execution).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
			completion.TrySetFromTask(execution);
			return null;
		}, "http-handler", "http",
			// An entry halted before it ran, or dropped at shutdown, still answers the request.
			onReleased: () => completion.TrySetResult(new HttpHandlerResult(503, "Service Unavailable", "text/plain", [], Halted)));
		if (!admission.Accepted)
		{
			logger.LogWarning("Inbound HTTP request refused by the queue: {Reason}.", admission.Reason);
			return new HttpHandlerResult(503, "Service Unavailable", "text/plain", [], admission.Error);
		}

		try { return await completion.Task.WaitAsync(ct); }
		finally { Volatile.Write(ref abandoned, true); }
	}

	/// <summary>The body of a request whose queue entry left the queue without running.</summary>
	internal const string Halted = "#-1 QUEUE ENTRY HALTED";

	private async ValueTask<Found<HttpHandlerResult>> ExecuteAsync(
		string method,
		string path,
		string body,
		IEnumerable<(string Name, string Value)> headers,
		string clientIp,
		DBRef? viewer,
		long handlerDbRefValue,
		ExecutionBudget? parentBudget,
		CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
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
		var context = new HttpResponseContext
		{
			ApplicationViewer = viewer,
			ApplicationInstance = headers.FirstOrDefault(h => h.Name.Equals("X-SharpMUSH-Form-Instance", StringComparison.OrdinalIgnoreCase)).Value
		};
		DBRef? handlerRef = null;

		using (budget.Enter())
		{
			try
			{
				budget.ThrowIfExceeded();
				if (await mediator.Send(new GetObjectNodeQuery(new DBRef((int)handlerDbRefValue, null)), budget.Token) is not AnySharpObject handler)
				{
					logger.LogWarning("Configured http_handler #{HandlerDbRef} not found.", handlerDbRefValue);
					return new NotFound();
				}
				handlerRef = handler.Object().DBRef;
				budget.ThrowIfExceeded();
				using var capture = outputCapture.BeginCapture(handlerRef.Value.Number, context);
				// The <METHOD> attribute is the handler entry point: GET, POST, etc. — run as commands,
				// the equivalent of PennMUSH's `@include #handler/<method>`. SharpMUSH deviates from
				// Penn (200 + empty body) by answering 404 when the attribute is absent; see help sharphttp.
				// Like @include (cque.c:712-717), the attribute may come from the handler's parents or the
				// type ancestor.
				var attributeName = method.ToUpperInvariant();
				var attributeResult = await attributeService.GetAttributeAsync(
					handler, handler, attributeName, IAttributeService.AttributeMode.Execute, parent: true);
				if (attributeResult is not SharpAttribute[] entryPoint)
				{
					return new NotFound();
				}

				// Build a fresh parser state — there is no ambient parser on the HTTP request path.
				// 'Invisible login': the handler is executor, enactor, and caller, as in Penn.
				var request = RequestHeaders(headers);
				var arguments = RequestArguments(path, body, request, viewer);
				var evalParser = parser.Push(ParserState.RootFor(handlerRef.Value) with
				{
					Registers = new([options.CurrentValue.Compatibility.HttpQRegisters ? RequestRegisters(request) : []]),
					EnvironmentRegisters = arguments,
					SuppliedArgumentKeys = AttributeArgumentEnvironment.SuppliedKeys(arguments),
					ExecutionBudget = budget,
					HttpResponse = context
				});

				var attributeValue = entryPoint.Last().Value;
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

		// HTTP`COMMAND sysevent, mirroring Penn (src/bsd.c:4076): address, method, path, code,
		// ctype, request body length, response body length. It is queued, so the response does not wait on it.
		if (!DeadlineExpired() && handlerRef is { } resolvedHandler)
		{
			using (budget.Enter())
			{
				try
				{
					budget.ThrowIfExceeded();
					await eventService.TriggerEventAsync(SharpEvents.HttpCommand, resolvedHandler,
						clientIp, method, path, result.Status.ToString(), result.ContentType,
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
	/// Request headers that carry a credential. The portal's account-session bearer, a client's MUSH
	/// password (Basic) and any cookie a proxy in front of the game set all arrive this way, and none
	/// of them is the handler's to read: every route's softcode sees the same arguments, and anything
	/// it can read it can log or send elsewhere. They never become <c>%&lt;hdr.*&gt;</c> arguments.
	/// Who is calling reaches softcode as <c>%&lt;viewer&gt;</c> instead, already verified.
	/// Names are compared after register normalization, so a client cannot respell one past this.
	/// </summary>
	public static readonly IReadOnlySet<string> CredentialHeaders =
		new HashSet<string>(["AUTHORIZATION", "PROXY-AUTHORIZATION", "COOKIE"], StringComparer.Ordinal);

	/// <summary>
	/// Query parameters that carry a credential: <c>access_token</c> is the account-session token's
	/// query-string form, which the server honours on every route (SignalR transports cannot send a
	/// header). It is removed from <c>%0</c> for the same reason as <see cref="CredentialHeaders"/>.
	/// </summary>
	public const string AccessTokenParameter = "access_token";

	/// <summary>
	/// <paramref name="path"/> with every <see cref="AccessTokenParameter"/> removed from its query
	/// string. The rest of the query is left exactly as the client wrote it.
	/// </summary>
	public static string WithoutCredentialParameters(string path)
	{
		var question = path.IndexOf('?');
		if (question < 0)
		{
			return path;
		}

		var kept = path[(question + 1)..].Split('&')
			.Where(pair => !Uri.UnescapeDataString(pair.Split('=', 2)[0].Replace('+', ' '))
				.Equals(AccessTokenParameter, StringComparison.OrdinalIgnoreCase))
			.ToArray();
		return kept.Length == 0 ? path[..question] : $"{path[..question]}?{string.Join('&', kept)}";
	}

	/// <summary>
	/// The request's headers as the handler is given them: each name normalized as a register name is
	/// (<c>X-Forwarded-For</c> becomes <c>X-FORWARDED-FOR</c>), duplicates joined with a newline (<c>%r</c>),
	/// in the order they first arrived. <see cref="CredentialHeaders"/> are left out.
	/// </summary>
	private static List<(string Name, MString Value)> RequestHeaders(IEnumerable<(string Name, string Value)> headers)
	{
		var request = new List<(string Name, MString Value)>();
		foreach (var (name, value) in headers)
		{
			var normalized = Utilities.RegisterNames.NormalizeSegment(name);
			if (normalized.Length == 0 || CredentialHeaders.Contains(normalized))
			{
				continue;
			}

			var index = request.FindIndex(header => header.Name == normalized);
			if (index < 0)
			{
				request.Add((normalized, MarkupText.Plain(value)));
			}
			else
			{
				request[index] = (normalized, MarkupText.Concat([request[index].Value, MarkupText.Plain("\n"), MarkupText.Plain(value)]));
			}
		}

		return request;
	}

	/// <summary>
	/// What the handler's attribute reads: the path and query as <c>%0</c> and <c>%&lt;path&gt;</c>, the body
	/// as <c>%1</c> and <c>%&lt;body&gt;</c>, each header as <c>%&lt;hdr.&lt;name&gt;&gt;</c>, their names as
	/// <c>%&lt;headers&gt;</c>, and as <c>%&lt;viewer&gt;</c> the objid of the character the request
	/// authenticated as, or nothing for an anonymous one.
	/// </summary>
	private static Dictionary<string, CallState> RequestArguments(string path, string body,
		List<(string Name, MString Value)> request, DBRef? viewer)
	{
		var arguments = new Dictionary<string, CallState>(StringComparer.OrdinalIgnoreCase)
		{
			["0"] = new CallState(path),
			["1"] = new CallState(body),
			["path"] = new CallState(path),
			["body"] = new CallState(body),
			["headers"] = new CallState(string.Join(' ', request.Select(header => header.Name))),
			["viewer"] = new CallState(viewer?.ToString() ?? string.Empty)
		};
		foreach (var (name, value) in request)
		{
			arguments[$"HDR.{name}"] = new CallState(value);
		}

		return arguments;
	}

	/// <summary>
	/// The q-registers PennMUSH gives HTTP handler code, kept for an imported game by <c>http_qregisters</c>:
	/// one <c>HDR.&lt;NAME&gt;</c> per header and <c>HEADERS</c>, the list of their names.
	/// </summary>
	private static Dictionary<string, MString> RequestRegisters(List<(string Name, MString Value)> request)
	{
		var registers = request.ToDictionary(header => $"HDR.{header.Name}", header => header.Value);
		registers["HEADERS"] = MarkupText.Plain(string.Join(' ', request.Select(header => header.Name)));
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
