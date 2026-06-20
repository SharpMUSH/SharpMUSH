using Mediator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Runs the <c>@STARTUP</c> attribute on every object once at boot, as God (#1) — the same pass
/// <c>@restart/all</c> performs. This re-establishes global side effects on each server start;
/// most importantly it re-registers <c>@function</c> global user-defined functions, which live in
/// an in-memory registry and are intentionally not persisted (durability comes from re-running
/// their <c>@function</c> commands inside <c>@STARTUP</c>).
///
/// <para>Registered after the package-bootstrap hosted services so any package-shipped <c>@STARTUP</c>
/// attributes already exist. The pass is idempotent/safe regardless of ordering: each object's
/// STARTUP failure is swallowed and never aborts the rest.</para>
/// </summary>
public class StartupAttributeBootstrapService(
	IMediator mediator,
	IAttributeService attributeService,
	IMUSHCodeParser parser,
	ILogger<StartupAttributeBootstrapService> logger) : IHostedService
{
	/// <summary>God (#1): the identity STARTUP runs as.</summary>
	private static readonly DBRef God = new(1);

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		try
		{
			var godNode = await mediator.Send(new GetObjectNodeQuery(God), cancellationToken);
			if (godNode.IsNone)
			{
				logger.LogWarning("Boot @STARTUP pass skipped: God (#1) not found (database not ready?).");
				return;
			}

			var god = godNode.Known;

			// Fresh parser state: God is executor, enactor, and caller — there is no ambient
			// parser at boot (mirrors the package lifecycle / HTTP handler invocation pattern).
			var bootParser = parser.Push(new ParserState(
				Registers: new([[]]),
				IterationRegisters: [],
				RegexRegisters: [],
				SwitchStack: [],
				ExecutionStack: [],
				EnvironmentRegisters: new Dictionary<string, CallState>(),
				CurrentEvaluation: null,
				ParserFunctionDepth: 0,
				Function: null,
				Command: null,
				CommandInvoker: _ => ValueTask.FromResult(new Option<CallState>(new None())),
				Switches: [],
				Arguments: [],
				Executor: god.Object().DBRef,
				Enactor: god.Object().DBRef,
				Caller: god.Object().DBRef,
				Handle: null,
				ParseMode: ParseMode.Default,
				HttpResponse: null,
				CallDepth: new InvocationCounter(),
				FunctionRecursionDepths: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
				TotalInvocations: new InvocationCounter(),
				LimitExceeded: new LimitExceededFlag()));

			logger.LogInformation("Running boot @STARTUP pass on all objects as God (#1).");
			await StartupAttributeRunner.RunAllAsync(bootParser, mediator, attributeService, god);
			logger.LogInformation("Boot @STARTUP pass complete.");
		}
		catch (Exception ex)
		{
			// A failure here must never block startup; @STARTUP is best-effort.
			logger.LogWarning(ex, "Boot @STARTUP pass failed; continuing startup.");
		}
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
