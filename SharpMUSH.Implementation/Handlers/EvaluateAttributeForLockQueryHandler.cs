using Microsoft.Extensions.Logging;
using Mediator;
using OneOf.Types;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers;

/// <summary>
/// Handler for evaluating an attribute as MUSHcode during lock evaluation.
/// PennMUSH eval locks (ATTR/pattern) evaluate the attribute on the gated object
/// with the unlocker as enactor, then compare the result to the pattern.
/// </summary>
public class EvaluateAttributeForLockQueryHandler(
	IAttributeService attributeService,
	IMUSHCodeParser parser,
	ILogger<EvaluateAttributeForLockQueryHandler> logger) : IQueryHandler<EvaluateAttributeForLockQuery, string?>
{
	public async ValueTask<string?> Handle(EvaluateAttributeForLockQuery query, CancellationToken cancellationToken)
	{
		// Evaluate the attribute on the gated object with the unlocker as executor/enactor
		// PennMUSH: call_ufun(&ufun, buff, player, player, pe_info, NULL)
		// where player = unlocker, and the attribute is on target (gated object)
		try
		{
			var unlockerRef = query.Unlocker.Object().DBRef;

			// Push a parser state so CurrentState is available during attribute evaluation
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
				Executor: unlockerRef,
				Enactor: unlockerRef,
				Caller: unlockerRef,
				Handle: null,
				ParseMode: ParseMode.Default,
				CallDepth: new InvocationCounter(),
				FunctionRecursionDepths: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
				TotalInvocations: new InvocationCounter(),
				LimitExceeded: new LimitExceededFlag()));

			var result = await attributeService.EvaluateAttributeFunctionAsync(
				evalParser,
				query.Unlocker,       // executor = unlocker (PennMUSH: player)
				query.GatedObject,    // obj = gated object (where the attribute lives)
				query.AttributeName,
				new Dictionary<string, CallState>(),
				evalParent: false,
				ignorePermissions: true);

			return result?.ToPlainText();
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Failed to evaluate attribute {Attribute} on {Object} for lock evaluation",
				query.AttributeName, query.GatedObject);
			return null;
		}
	}
}
