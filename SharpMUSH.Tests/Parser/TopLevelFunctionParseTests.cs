using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// <see cref="IMUSHCodeParser.FunctionParse(MString)"/> entered at the top level from a state that
/// carries actors but no invocation counters — the shape you get by filling in only the required
/// <see cref="ParserState"/> arguments, which is what the benchmark harness and every embedder
/// naturally writes.
///
/// <para>
/// That state used to lose its actors on the way in: FunctionParse pushed a fresh tracking frame
/// and left Executor/Enactor/Caller null, so the permission gate at the top of every function call
/// threw out of KnownExecutorObject, got caught, and returned an empty result. The caller saw
/// silence, and the log saw a full stack trace per function call — two million of them in one
/// nightly benchmark run.
/// </para>
/// </summary>
public class TopLevelFunctionParseTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IServiceProvider Services => WebAppFactoryArg.Services;

	/// <summary>
	/// A parser whose root state names an executor but omits CallDepth/TotalInvocations/
	/// FunctionRecursionDepths/LimitExceeded, so FunctionParse has to push a tracking frame.
	/// </summary>
	private IMUSHCodeParser UntrackedRootParser(DBRef? executor) =>
		new MUSHCodeParser(
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
				Executor: executor,
				Enactor: executor,
				Caller: executor,
				Handle: 1));

	[Test]
	public async Task FunctionParse_FromAnUntrackedRootState_EvaluatesTheFunction()
	{
		var parser = UntrackedRootParser(WebAppFactoryArg.ExecutorDBRef);

		var result = await parser.FunctionParse(MarkupText.Plain("add(1,2)"));

		await Assert.That(result?.Message?.ToPlainText()).IsEqualTo("3")
			.Because("dropping the executor made every function call fail its permission gate and return empty");
	}

	[Test]
	public async Task FunctionParse_FromAnUntrackedRootState_KeepsTheExecutorsPermissions()
	{
		var parser = UntrackedRootParser(WebAppFactoryArg.ExecutorDBRef);

		var result = await parser.FunctionParse(MarkupText.Plain("beep()"));

		await Assert.That(result?.Message?.ToPlainText()).DoesNotContain("PERMISSION DENIED")
			.Because("God evaluating an admin-only function must still be God after the tracking frame is pushed");
	}

	[Test]
	public async Task FunctionParse_WithNoExecutorAtAll_ReturnsEmptyRatherThanThrowing()
	{
		var parser = UntrackedRootParser(executor: null);

		var result = await parser.FunctionParse(MarkupText.Plain("add(1,2)"));

		await Assert.That(result?.Message?.ToPlainText()).IsNullOrEmpty()
			.Because("the connect screen has no executor; a function call there is answered, not logged as an internal error");
	}
}
