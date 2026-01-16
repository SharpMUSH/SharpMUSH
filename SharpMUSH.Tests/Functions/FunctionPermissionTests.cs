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
	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }

	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();
	private IServiceProvider Services => Factory.Services;

	/// <summary>
	/// Helper method to create a parser with a specific executor
	/// </summary>
	private IMUSHCodeParser CreateParserWithExecutor(DBRef executorDbRef)
	{
		return new MUSHCodeParser(
			Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MUSHCodeParser>>(),
			Services.GetRequiredService<LibraryService<string, FunctionDefinition>>(),
			Services.GetRequiredService<LibraryService<string, CommandDefinition>>(),
			Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>(),
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
				CommandInvoker: _ => ValueTask.FromResult(new Option<CallState>(new None())),
				Switches: [],
				Arguments: [],
				Executor: executorDbRef,
				Enactor: executorDbRef,
				Caller: executorDbRef,
				Handle: 1,
				CallDepth: new InvocationCounter(),
				FunctionRecursionDepths: new Dictionary<string, int>(),
				TotalInvocations: new InvocationCounter(),
				LimitExceeded: new LimitExceededFlag()
			));
	}

	[Test]
	public async Task WizardOnlyFunction_AllowsWizard()
	{
		// Test that a wizard can call a WizardOnly function (e.g., pcreate)
		var parser = Factory.FunctionParser;
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
		var parser = CreateParserWithExecutor(player);

		// Try to call a WizardOnly function (pcreate)
		var result = await parser.FunctionParse(MModule.single("pcreate(AnotherPlayer,password)"));
		
		// Should return a permission error
		await Assert.That(result?.Message?.ToPlainText()).Contains("PERMISSION DENIED");
	}

	[Test]
	public async Task AdminOnlyFunction_AllowsWizard()
	{
		// Test that a wizard can call an AdminOnly function (e.g., beep)
		var parser = Factory.FunctionParser;
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
		var parser = CreateParserWithExecutor(player);

		// Try to call an AdminOnly function (beep)
		var result = await parser.FunctionParse(MModule.single("beep()"));
		
		// Should return a permission error
		await Assert.That(result?.Message?.ToPlainText()).Contains("PERMISSION DENIED");
	}
}
