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
		// Arrange - Create test object and set DEBUG flag on it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DebugEvalTest"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugEvalTest=DEBUG"));
		
		// Act - Execute a function with unique values as the debug object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@force DebugEvalTest=@pemit me=[add(123,456)]"));
		
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
		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugEvalTest"));
	}

	[Test]
	public async Task DebugFlag_ShowsNesting_WithIndentation()
	{
		// Arrange - Create test object and set DEBUG flag
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DebugNestingTest"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugNestingTest=DEBUG"));
		
		// Act - Execute nested function with unique values as the debug object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@force DebugNestingTest=@pemit me=[mul(add(11,22),3)]"));
		
		// Assert - Outer function
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("! mul(add(11,22),3) :"),
						str => str.Contains("! mul(add(11,22),3) :"))),
				Arg.Any<AnySharpObject>(), 
				Arg.Any<INotifyService.NotificationType>());
		
		// Assert - Inner function (has ONE space for indentation at depth 1)
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("! add(11,22) :"),
						str => str.Contains("! add(11,22) :"))),
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
		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugNestingTest"));
	}

	[Test]
	public async Task VerboseFlag_OutputsCommandExecution()
	{
		// Arrange - Create test object and set VERBOSE flag
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create VerboseCmdTest"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set VerboseCmdTest=VERBOSE"));
		
		// Act - Execute a command with unique message as the verbose object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@force VerboseCmdTest=@pemit me=UniqueTestMessage789"));
		
		// Assert - Verify VERBOSE output (format: "#dbref] command")
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("] ") && mstr.ToString().Contains("@pemit me=UniqueTestMessage789"),
						str => str.Contains("] ") && str.Contains("@pemit me=UniqueTestMessage789"))),
				Arg.Any<AnySharpObject>(), 
				Arg.Any<INotifyService.NotificationType>());
		
		// Cleanup
		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy VerboseCmdTest"));
	}

	[Test]
	public async Task AttributeDebugFlag_ForcesOutput_EvenWithoutObjectDebug()
	{
		// Arrange - Create test object WITHOUT DEBUG, set attribute with DEBUG flag
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create AttrDebugForceTest"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("&testfunc AttrDebugForceTest=[add(88,77)]"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set AttrDebugForceTest/testfunc=DEBUG"));
		
		// Act - Trigger the attribute via @force (which uses WithAttributeDebug internally)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@force AttrDebugForceTest=@trigger #self/testfunc"));
		
		// Assert - Should see debug output despite object not having DEBUG
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
		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy AttrDebugForceTest"));
	}

	[Test]
	public async Task AttributeNoDebugFlag_SuppressesOutput_EvenWithObjectDebug()
	{
		// Arrange - Create test object WITH DEBUG but set attribute WITH NODEBUG
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create AttrNoDebugSuppressTest"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set AttrNoDebugSuppressTest=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("&testfunc2 AttrNoDebugSuppressTest=[add(55,44)]"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set AttrNoDebugSuppressTest/testfunc2=NODEBUG"));
		
		// Act - Trigger the attribute via @force (which uses WithAttributeDebug internally)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@force AttrNoDebugSuppressTest=@trigger #self/testfunc2"));
		
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
		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy AttrNoDebugSuppressTest"));
	}
}
