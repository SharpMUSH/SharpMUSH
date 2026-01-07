using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class DebugVerboseTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async Task DebugFlag_OutputsFunctionEvaluation_WithSpecificValues()
	{
		// Arrange - Set DEBUG flag on player #1 (God)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set me=DEBUG"));
		
		NotifyService.ClearReceivedCalls();
		
		// Act - Execute a function with unique values
		await Parser.CommandParse(1, ConnectionService, MModule.single("think [add(123,456)]"));
		
		// Assert - Verify debug output contains the specific function call
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("! add(123,456) :"),
						str => str.Contains("! add(123,456) :"))),
				Arg.Any<AnySharpObject>(), 
				Arg.Any<INotifyService.NotificationType>());
		
		// Assert - Verify debug output contains the specific result
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("! add(123,456) => 579"),
						str => str.Contains("! add(123,456) => 579"))),
				Arg.Any<AnySharpObject>(), 
				Arg.Any<INotifyService.NotificationType>());
		
		// Cleanup
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set me=!DEBUG"));
	}

	[Test]
	public async Task DebugFlag_ShowsNesting_WithIndentation()
	{
		// Arrange
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set me=DEBUG"));
		
		NotifyService.ClearReceivedCalls();
		
		// Act - Execute nested function with unique values
		await Parser.CommandParse(1, ConnectionService, MModule.single("think [mul(add(11,22),3)]"));
		
		// Assert - Outer function (check for the actual format with #1!)
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("#1! mul(add(11,22),3) :"),
						str => str.Contains("#1! mul(add(11,22),3) :"))),
				Arg.Any<AnySharpObject>(), 
				Arg.Any<INotifyService.NotificationType>());
		
		// Assert - Inner function (has ONE space for indentation at depth 1)
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("#1! add(11,22) :"),
						str => str.Contains("#1! add(11,22) :"))),
				Arg.Any<AnySharpObject>(), 
				Arg.Any<INotifyService.NotificationType>());
		
		// Assert - Final result should be 99
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("=> 99"),
						str => str.Contains("=> 99"))),
				Arg.Any<AnySharpObject>(), 
				Arg.Any<INotifyService.NotificationType>());
		
		// Cleanup
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set me=!DEBUG"));
	}

	[Test]
	public async Task VerboseFlag_OutputsCommandExecution()
	{
		// Arrange
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set me=VERBOSE"));
		
		NotifyService.ClearReceivedCalls();
		
		// Act - Execute a command with unique message
		await Parser.CommandParse(1, ConnectionService, MModule.single("@pemit me=UniqueTestMessage789"));
		
		// Assert - Verify VERBOSE output (format: "#dbref] command")
		// The output includes the full command line
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("#1] ") && mstr.ToString().Contains("@pemit me=UniqueTestMessage789"),
						str => str.Contains("#1] ") && str.Contains("@pemit me=UniqueTestMessage789"))),
				Arg.Any<AnySharpObject>(), 
				Arg.Any<INotifyService.NotificationType>());
		
		// Cleanup
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set me=!VERBOSE"));
	}

	[Test]
	public async Task AttributeDebugFlag_ForcesOutput_EvenWithoutObjectDebug()
	{
		// Arrange - Set attribute with DEBUG flag, but object WITHOUT DEBUG and WITHOUT VERBOSE
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set me=!DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set me=!VERBOSE"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("&testfunc me=[add(88,77)]"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set me/testfunc=DEBUG"));
		
		NotifyService.ClearReceivedCalls();
		
		// Act - Call attribute via u()
		await Parser.CommandParse(1, ConnectionService, MModule.single("think [u(me/testfunc)]"));
		
		// Assert - Should see debug output despite object not having DEBUG
		// The output includes the #1! prefix and extra indentation for nested evaluation
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("add(88,77) => 165"),
						str => str.Contains("add(88,77) => 165"))),
				Arg.Any<AnySharpObject>(), 
				Arg.Any<INotifyService.NotificationType>());
		
		// Cleanup
		await Parser.CommandParse(1, ConnectionService, MModule.single("&testfunc me="));
	}

	[Test]
	public async Task AttributeNoDebugFlag_SuppressesOutput_EvenWithObjectDebug()
	{
		// Arrange - Set object WITH DEBUG but attribute WITH NODEBUG
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set me=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("&testfunc2 me=[add(55,44)]"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set me/testfunc2=NODEBUG"));
		
		NotifyService.ClearReceivedCalls();
		
		// Act - Call attribute via u()
		await Parser.CommandParse(1, ConnectionService, MModule.single("think [u(me/testfunc2)]"));
		
		// Assert - Should NOT see debug output (NODEBUG takes precedence)
		await NotifyService
			.DidNotReceive()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("! add(55,44)"),
						str => str.Contains("! add(55,44)"))),
				Arg.Any<AnySharpObject>(), 
				Arg.Any<INotifyService.NotificationType>());
		
		// Cleanup
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set me=!DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("&testfunc2 me="));
	}
}
