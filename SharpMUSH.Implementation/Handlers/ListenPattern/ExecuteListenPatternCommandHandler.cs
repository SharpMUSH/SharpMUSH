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
			// Get a parser from the service provider
			var parser = serviceProvider.GetRequiredService<IMUSHCodeParser>();

			// Get the listener's DBRef
			var listenerDbRef = request.Listener.Object().DBRef;
			var speakerDbRef = request.Speaker.Object().DBRef;

			// Convert CallState registers to MString for parser state
			var registerDict = new Dictionary<string, MString>();
			foreach (var kvp in request.Registers)
			{
				// Extract MString from CallState
				registerDict[kvp.Key] = kvp.Value.Message ?? MModule.empty();
			}

			// Execute the attribute with modified parser state
			// Set executor to listener and enactor to speaker
			await parser.With(state => state with
			{
				Executor = listenerDbRef,
				Enactor = speakerDbRef,
				Registers = new([registerDict])
			}, async newParser =>
			{
				// Execute the listen action attribute
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
			// Log the error but don't throw - we don't want listener errors to break notifications
			logger.LogError(ex,
				"Error executing listen pattern {AttributeName} on {Listener} triggered by {Speaker}",
				request.AttributeName,
				request.Listener.Object().DBRef,
				request.Speaker.Object().DBRef);
		}

		return Unit.Value;
	}
}
