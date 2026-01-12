using Mediator;
using Microsoft.Extensions.DependencyInjection;
using OneOf;
using OneOf.Types;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;

namespace SharpMUSH.Tests.Functions;

public class FunctionPermissionTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IServiceProvider Services => WebAppFactoryArg.Services;

	[Test]
	public async Task WizardOnlyFunction_AllowsWizard()
	{
		// Test that a wizard can call a WizardOnly function (e.g., pcreate)
		var parser = WebAppFactoryArg.FunctionParser;
		var result = await parser.FunctionParse(MModule.single("pcreate(TestWiz,password)"));
		
		// Should not return a permission error
		await Assert.That(result?.Message?.ToPlainText()).DoesNotContain("PERMISSION DENIED");
	}

	[Test]
	public async Task WizardOnlyFunction_DeniesNonWizard()
	{
		// Create a non-wizard player
		var player = await Mediator.Send(new CreatePlayerCommand(
			"NonWizardPlayer",
			"password",
			new DBRef(0),
			new DBRef(0),
			100));

		// Create a parser with the non-wizard player as executor
		var parser = new MUSHCodeParser(
			Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MUSHCodeParser>>(),
			Services.GetRequiredService<LibraryService<string, FunctionDefinition>>(),
			Services.GetRequiredService<LibraryService<string, CommandDefinition>>(),
			Services.GetRequiredService<IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>>(),
			Services,
			state: new ParserState(
				Registers: new ConcurrentStack<Dictionary<string, MString>>([[]]),
				IterationRegisters: [],
				RegexRegisters: [],
				ExecutionStack: [],
				EnvironmentRegisters: [],
				CurrentEvaluation: null,
				ParserFunctionDepth: 0,
				Function: null,
				Command: "think",
				CommandInvoker: _ => ValueTask.FromResult(new Option<CallState>(new OneOf.Types.None())),
				Switches: [],
				Arguments: [],
				Executor: player,
				Enactor: player,
				Caller: player,
				Handle: 1,
				CallDepth: new InvocationCounter(),
				FunctionRecursionDepths: new Dictionary<string, int>(),
				TotalInvocations: new InvocationCounter(),
				LimitExceeded: new LimitExceededFlag()
			));

		// Try to call a WizardOnly function (pcreate)
		var result = await parser.FunctionParse(MModule.single("pcreate(AnotherPlayer,password)"));
		
		// Should return a permission error
		await Assert.That(result?.Message?.ToPlainText()).Contains("PERMISSION DENIED");
	}

	[Test]
	public async Task AdminOnlyFunction_AllowsWizard()
	{
		// Test that a wizard can call an AdminOnly function (e.g., beep)
		var parser = WebAppFactoryArg.FunctionParser;
		var result = await parser.FunctionParse(MModule.single("beep()"));
		
		// Should not return a permission error
		await Assert.That(result?.Message?.ToPlainText()).DoesNotContain("PERMISSION DENIED");
	}

	[Test]
	public async Task AdminOnlyFunction_DeniesNonWizard()
	{
		// Create a non-wizard player
		var player = await Mediator.Send(new CreatePlayerCommand(
			"NonAdminPlayer",
			"password",
			new DBRef(0),
			new DBRef(0),
			100));

		// Create a parser with the non-wizard player as executor
		var parser = new MUSHCodeParser(
			Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MUSHCodeParser>>(),
			Services.GetRequiredService<LibraryService<string, FunctionDefinition>>(),
			Services.GetRequiredService<LibraryService<string, CommandDefinition>>(),
			Services.GetRequiredService<IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>>(),
			Services,
			state: new ParserState(
				Registers: new ConcurrentStack<Dictionary<string, MString>>([[]]),
				IterationRegisters: [],
				RegexRegisters: [],
				ExecutionStack: [],
				EnvironmentRegisters: [],
				CurrentEvaluation: null,
				ParserFunctionDepth: 0,
				Function: null,
				Command: "think",
				CommandInvoker: _ => ValueTask.FromResult(new Option<CallState>(new OneOf.Types.None())),
				Switches: [],
				Arguments: [],
				Executor: player,
				Enactor: player,
				Caller: player,
				Handle: 1,
				CallDepth: new InvocationCounter(),
				FunctionRecursionDepths: new Dictionary<string, int>(),
				TotalInvocations: new InvocationCounter(),
				LimitExceeded: new LimitExceededFlag()
			));

		// Try to call an AdminOnly function (beep)
		var result = await parser.FunctionParse(MModule.single("beep()"));
		
		// Should return a permission error
		await Assert.That(result?.Message?.ToPlainText()).Contains("PERMISSION DENIED");
	}
}
