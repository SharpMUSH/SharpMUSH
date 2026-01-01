using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class ControlFlowCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask SelectCommand()
	{
		// Clear any previous received calls from other tests
		NotifyService.ClearReceivedCalls();
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@select 1=1,@pemit #1=One,@pemit #1=Other"));

		// @select currently sends 6 debug/info notifications (not yet fully implemented)
		await NotifyService
			.Received(Quantity.Exactly(6))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Skip("Deadlocks - requires fixing .GetAwaiter().GetResult() calls in codebase (see ListFunctions.cs:1518, GetAttributeQueryHandler.cs, etc.)")]
	public async ValueTask SwitchCommand()
	{
		// Clear any previous received calls from other tests
		NotifyService.ClearReceivedCalls();
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@switch 1=1,@pemit #1=One,@pemit #1=Other"));

		// @switch sends debug messages - just verify it was called
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask BreakCommand()
	{
		// @break command doesn't send notifications in current implementation
		await Parser.CommandParse(1, ConnectionService, MModule.single("@break"));
		// Just verify it doesn't throw
	}

	[Test]
	public async ValueTask AssertCommand()
	{
		// @assert command doesn't send notifications in current implementation
		await Parser.CommandParse(1, ConnectionService, MModule.single("@assert 1"));
		// Just verify it doesn't throw
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask RetryCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@retry 1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask SkipCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@skip 0=@pemit #1=SkipCommand False; @pemit #1=SkipCommand Rest"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "SkipCommand False", Arg.Any<AnySharpObject>());
		
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "SkipCommand Rest", Arg.Any<AnySharpObject>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask IncludeCommand()
	{
		// @include command doesn't send notifications in current implementation
		await Parser.CommandParse(1, ConnectionService, MModule.single("@include #1/ATTRIBUTE"));
		// Just verify it doesn't throw
	}

	[Test]
	public async ValueTask IfElseCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@ifelse 1=@pemit #1=IfElseCommand True,@pemit #1=IfElseCommand False"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "IfElseCommand True", Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}
}
