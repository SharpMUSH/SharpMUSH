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
		var context = new HttpResponseContext();
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
				var evalParser = parser.Push(ParserState.RootFor(handlerRef.Value) with
				{
					Registers = new([BuildRequestRegisters(headers, viewer)]),
					EnvironmentRegisters = new Dictionary<string, CallState>
					{
						["0"] = new CallState(path),
						["1"] = new CallState(body)
					},
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
					await eventService.TriggerEventAsync("HTTP`COMMAND", resolvedHandler,
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
	/// of them is the handler's to read: every route's softcode sees the same registers, and anything
	/// it can read it can log or send elsewhere. They never become <c>%q&lt;hdr.*&gt;</c> registers.
	/// Who is calling reaches softcode as <c>%q&lt;viewer&gt;</c> instead, already verified.
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
	/// Seeds the q-registers Penn provides to HTTP handler code: one <c>HDR.&lt;NAME&gt;</c> register
	/// per header (duplicate headers joined with a newline, i.e. <c>%r</c>), plus <c>HEADERS</c>
	/// holding the space-separated list of header names. <see cref="CredentialHeaders"/> are left
	/// out of both. <c>VIEWER</c> holds the objid of the character the request authenticated as, or
	/// nothing for an anonymous one.
	/// </summary>
	private static Dictionary<string, MString> BuildRequestRegisters(IEnumerable<(string Name, string Value)> headers, DBRef? viewer)
	{
		var registers = new Dictionary<string, MString>();
		var names = new List<string>();

		foreach (var (name, value) in headers)
		{
			var normalized = Utilities.RegisterNames.NormalizeSegment(name);
			if (normalized.Length == 0 || CredentialHeaders.Contains(normalized))
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
		registers["VIEWER"] = MarkupText.Plain(viewer?.ToString() ?? string.Empty);
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
