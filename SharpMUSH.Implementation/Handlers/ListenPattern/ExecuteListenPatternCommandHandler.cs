using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Commands.ListenPattern;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.ListenPattern;

/// <summary>
/// Handler for executing listen pattern action attributes.
/// Captures the action and admits an independent command list without executing it inline.
/// </summary>
public class ExecuteListenPatternCommandHandler(
	IServiceProvider serviceProvider,
	IAttributeService attributeService,
	ILogger<ExecuteListenPatternCommandHandler> logger) : ICommandHandler<ExecuteListenPatternCommand>
{
	public async ValueTask<Unit> Handle(ExecuteListenPatternCommand request, CancellationToken cancellationToken)
	{
		try
		{
			using var budget = ExecutionBudget.EnterLinked(cancellationToken);
			var parser = serviceProvider.GetRequiredService<IMUSHCodeParser>();
			var restrictions = parser.State.IsEmpty ? null : parser.CurrentState.Restrictions;
			EvaluationRestrictions.DemandObjectDataAccess(restrictions);
			var listenerDbRef = request.Listener.Object().DBRef;
			var speakerDbRef = request.Speaker.Object().DBRef;
			MString action;
			if (request.Action is MString captured)
				action = captured;
			else
			{
				var attribute = await attributeService.GetAttributeAsync(request.Listener, request.Listener, request.AttributeName,
					IAttributeService.AttributeMode.Execute, parent: true);
				if (attribute is not SharpAttribute[] { Length: > 0 } chain) return Unit.Value;
				action = chain.Last().Value;
				var prefix = CommandDiscoveryService.ListenPatternRegex().Match(action.ToPlainText());
				if (!prefix.Success) prefix = CommandDiscoveryService.CommandPatternRegex().Match(action.ToPlainText());
				if (prefix.Success) action = action.Substring(prefix.Length);
			}
			if (action.Length == 0) return Unit.Value;
			var arguments = request.Registers.ToDictionary(pair => pair.Key, pair => new CallState(pair.Value.Message ?? MarkupText.Empty));
			var state = ParserState.RootFor(listenerDbRef).SnapshotForQueuedAction() with
			{
				Enactor = speakerDbRef,
				Caller = speakerDbRef,
				Arguments = arguments,
				EnvironmentRegisters = new(arguments),
				CurrentEvaluation = new DBAttribute(listenerDbRef, request.AttributeName),
				Restrictions = EvaluationRestrictions.Current ?? restrictions
			};
			cancellationToken.ThrowIfCancellationRequested();
			var admission = await serviceProvider.GetRequiredService<ITaskScheduler>().AdmitCommandList(action, state);
			if (!admission.Accepted)
				logger.LogWarning("Listener action {AttributeName} on {Listener} rejected: {Reason}", request.AttributeName, listenerDbRef, admission.Reason);
		}
		catch (Exception ex) when (ex is not OperationCanceledException and not RestrictedExpressionException)
		{
			logger.LogError(ex,
				"Error executing listen pattern {AttributeName} on {Listener} triggered by {Speaker}",
				request.AttributeName,
				request.Listener.Object().DBRef,
				request.Speaker.Object().DBRef);
		}

		return Unit.Value;
	}
}
