using Mediator;
using NSubstitute;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using TUnit.AspNetCore;
using TUnit.Core;
using TUnit.Core.Services;

namespace SharpMUSH.Tests;

public abstract class TestsBase : WebApplicationTest<TestWebApplicationFactory,SharpMUSH.Server.Program>
{
	/// <summary>
	/// Per-test NotifyService mock. Each test gets a fresh mock with no accumulated state.
	/// </summary>
	protected INotifyService NotifyService { get; private set; } = null!;
	
	protected override async Task SetupAsync()
	{
		// Create a fresh NotifyService mock for this test
		NotifyService = Substitute.For<INotifyService>();
		
		// Register it with the wrapper so all DI-injected code uses this test's mock
		TestNotifyServiceWrapper.SetCurrentNotifyService(NotifyService);
		
		await Task.CompletedTask;
	}
	
	protected IMUSHCodeParser CommandParser => CreateComamndParser();
	
	protected IMUSHCodeParser FunctionParser =>  CreateFunctionParser();

	private IMUSHCodeParser CreateComamndParser()
	{
		var mediator = Services.GetRequiredService<IMediator>();
		var one = mediator.Send(new GetObjectNodeQuery(new DBRef(1)))
			.GetAwaiter().GetResult()
			.AsPlayer.Object.DBRef;
		
		var mp = Services.GetRequiredService<IMUSHCodeParser>()
			.FromState(new ParserState(
				Registers: new([[]]),
				IterationRegisters: [],
				RegexRegisters: [],
				ExecutionStack: [],
				EnvironmentRegisters: [],
				CurrentEvaluation: null,
				ParserFunctionDepth: 0,
				Function: null,
				Command: null,
				CommandInvoker: _ => ValueTask.FromResult(new Option<CallState>(new None())),
				Switches: [],
				Arguments: [],
				Executor: one,
				Enactor: one,
				Caller: one,
				Handle: 1,
				CallDepth: new InvocationCounter(),
				FunctionRecursionDepths: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
				TotalInvocations: new InvocationCounter(),
				LimitExceeded: new LimitExceededFlag()
			));
		
		return mp;
	}
	
	private IMUSHCodeParser CreateFunctionParser()
	{
		var mediator = Services.GetRequiredService<IMediator>();
		var one = mediator.Send(new GetObjectNodeQuery(new DBRef(1)))
			.GetAwaiter().GetResult()
			.AsPlayer.Object.DBRef;
		
		var mp = Services.GetRequiredService<IMUSHCodeParser>()
			.FromState(new ParserState(
				Registers: new([[]]),
				IterationRegisters: [],
				RegexRegisters: [],
				ExecutionStack: [],
				EnvironmentRegisters: [],
				CurrentEvaluation: null,
				ParserFunctionDepth: 0,
				Function: null,
				Command: null,
				CommandInvoker: _ => ValueTask.FromResult(new Option<CallState>(new None())),
				Switches: [],
				Arguments: [],
				Executor: one,
				Enactor: one,
				Caller: one,
				Handle: 1,
				CallDepth: new InvocationCounter(),
				FunctionRecursionDepths: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
				TotalInvocations: new InvocationCounter(),
				LimitExceeded: new LimitExceededFlag()
			));
		
		return mp;
	}
}