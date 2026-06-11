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

namespace SharpMUSH.Library.Services;

/// <inheritdoc />
public class HttpHandlerDispatcher(
	IMediator mediator,
	IAttributeService attributeService,
	IMUSHCodeParser parser,
	IOptionsWrapper<SharpMUSHOptions> options,
	ILogger<HttpHandlerDispatcher> logger) : IHttpHandlerDispatcher
{
	/// <inheritdoc />
	public async ValueTask<OneOf<string, NotFound>> DispatchAsync(
		string attribute,
		string method,
		string path,
		string query,
		string body,
		DBRef viewer,
		CancellationToken ct = default)
	{
		var handlerDbRef = options.CurrentValue.Database.HttpHandler;
		if (handlerDbRef is null or 0)
		{
			logger.LogWarning("HTTP handler dispatch requested but no http_handler is configured.");
			return new NotFound();
		}

		var handlerResult = await mediator.Send(new GetObjectNodeQuery(new DBRef((int)handlerDbRef.Value, null)), ct);
		if (handlerResult.IsNone)
		{
			logger.LogWarning("Configured http_handler #{HandlerDbRef} not found.", handlerDbRef.Value);
			return new NotFound();
		}

		var handler = handlerResult.Known;

		// No handler attribute for this route ⇒ 404 (not all routes need a handler).
		var attributeResult = await attributeService.GetAttributeAsync(
			handler, handler, attribute, IAttributeService.AttributeMode.Execute, parent: false);
		if (!attributeResult.IsAttribute)
		{
			return new NotFound();
		}

		// Bind the request to stack args: %0=method %1=path %2=query %3=body %4=viewer dbref.
		var argsDict = new Dictionary<string, CallState>
		{
			["0"] = new CallState(method),
			["1"] = new CallState(path),
			["2"] = new CallState(query),
			["3"] = new CallState(body),
			["4"] = new CallState(viewer.ToString())
		};

		// Build a fresh parser state — there is no ambient parser on the HTTP request path.
		// Executor is the handler object (it owns its softcode); enactor/caller is the viewer.
		var handlerRef = handler.Object().DBRef;
		var evalParser = parser.Push(new ParserState(
			Registers: new([[]]),
			IterationRegisters: [],
			RegexRegisters: [],
			SwitchStack: [],
			ExecutionStack: [],
			EnvironmentRegisters: [],
			CurrentEvaluation: null,
			ParserFunctionDepth: 0,
			Function: null,
			Command: null,
			CommandInvoker: _ => ValueTask.FromResult(new Option<CallState>(new None())),
			Switches: [],
			Arguments: [],
			Executor: handlerRef,
			Enactor: viewer,
			Caller: viewer,
			Handle: null,
			ParseMode: ParseMode.Default,
			CallDepth: new InvocationCounter(),
			FunctionRecursionDepths: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
			TotalInvocations: new InvocationCounter(),
			LimitExceeded: new LimitExceededFlag()));

		// ignorePermissions: the handler runs with elevated privileges; the softcode itself
		// enforces per-viewer visibility using the viewer dbref passed in %4.
		var result = await attributeService.EvaluateAttributeFunctionAsync(
			evalParser, handler, handler, attribute, argsDict, evalParent: false, ignorePermissions: true);

		return result.ToString();
	}
}
