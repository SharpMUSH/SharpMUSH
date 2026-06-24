using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Commands.ListenPattern;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.ListenPattern;

/// <summary>
/// Handler for executing listen pattern action attributes.
/// Creates a parser with appropriate state and executes the attribute.
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
			var parser = serviceProvider.GetRequiredService<IMUSHCodeParser>();

			var listenerDbRef = request.Listener.Object().DBRef;
			var speakerDbRef = request.Speaker.Object().DBRef;

			var registerDict = new Dictionary<string, MString>();
			foreach (var kvp in request.Registers)
			{
				registerDict[kvp.Key] = kvp.Value.Message ?? MModule.empty();
			}

			await parser.With(state => state with
			{
				Executor = listenerDbRef,
				Enactor = speakerDbRef,
				Registers = new([registerDict])
			}, async newParser =>
			{
				// ignorePermissions: true because listen patterns run with listener's permissions
				await attributeService.EvaluateAttributeFunctionAsync(
					newParser,
					request.Speaker,  // enactor
					request.Listener, // executor (object with the attribute)
					request.AttributeName,
					request.Registers,
					evalParent: false,
					ignorePermissions: true);
			});
		}
		catch (Exception ex)
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
