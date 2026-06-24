using Mediator;
using Microsoft.Extensions.DependencyInjection;
using OneOf.Types;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;

namespace SharpMUSH.Tests.Functions;

public class FunctionPermissionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IServiceProvider Services => WebAppFactoryArg.Services;

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
				SwitchStack: [],
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
		var parser = WebAppFactoryArg.FunctionParser;
		var result = await parser.FunctionParse(MModule.single("pcreate(TestWiz,password)"));

		await Assert.That(result?.Message?.ToPlainText()).DoesNotContain("PERMISSION DENIED");
	}

	[Test]
	public async Task WizardOnlyFunction_DeniesNonWizard()
	{
		var player = await Mediator.Send(new CreatePlayerCommand(
			"NonWizardPlayer",
			"password",
			new DBRef(0),
			new DBRef(0),
			100));

		var parser = CreateParserWithExecutor(player);

		var result = await parser.FunctionParse(MModule.single("pcreate(AnotherPlayer,password)"));

		await Assert.That(result?.Message?.ToPlainText()).Contains("PERMISSION DENIED");
	}

	[Test]
	public async Task AdminOnlyFunction_AllowsWizard()
	{
		var parser = WebAppFactoryArg.FunctionParser;
		var result = await parser.FunctionParse(MModule.single("beep()"));

		await Assert.That(result?.Message?.ToPlainText()).DoesNotContain("PERMISSION DENIED");
	}

	[Test]
	public async Task AdminOnlyFunction_DeniesNonWizard()
	{
		var player = await Mediator.Send(new CreatePlayerCommand(
			"NonAdminPlayer",
			"password",
			new DBRef(0),
			new DBRef(0),
			100));

		var parser = CreateParserWithExecutor(player);

		var result = await parser.FunctionParse(MModule.single("beep()"));

		await Assert.That(result?.Message?.ToPlainText()).Contains("PERMISSION DENIED");
	}
}
