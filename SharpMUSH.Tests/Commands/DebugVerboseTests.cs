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
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DebugTest1"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugTest1=DEBUG"));
		
		NotifyService.ClearReceivedCalls();
		
		// Act - Execute a function with unique values as the debug object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@force DebugTest1=think [add(123,456)]"));
		
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
		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugTest1"));
	}

	[Test]
	public async Task DebugFlag_ShowsNesting_WithIndentation()
	{
		// Arrange - Create test object and set DEBUG flag
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DebugTest2"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugTest2=DEBUG"));
		
		NotifyService.ClearReceivedCalls();
		
		// Act - Execute nested function with unique values as the debug object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@force DebugTest2=think [mul(add(11,22),3)]"));
		
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
		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugTest2"));
	}

	[Test]
	public async Task VerboseFlag_OutputsCommandExecution()
	{
		// Arrange - Create test object and set VERBOSE flag
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create VerboseTest1"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set VerboseTest1=VERBOSE"));
		
		NotifyService.ClearReceivedCalls();
		
		// Act - Execute a command with unique message as the verbose object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@force VerboseTest1=@pemit me=UniqueTestMessage789"));
		
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
		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy VerboseTest1"));
	}

	[Test]
	public async Task AttributeDebugFlag_ForcesOutput_EvenWithoutObjectDebug()
	{
		// Arrange - Create test object WITHOUT DEBUG, set attribute with DEBUG flag
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create AttrDebugTest1"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("&testfunc AttrDebugTest1=[add(88,77)]"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set AttrDebugTest1/testfunc=DEBUG"));
		
		NotifyService.ClearReceivedCalls();
		
		// Act - Trigger the attribute (which uses WithAttributeDebug)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@trigger AttrDebugTest1/testfunc"));
		
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
		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy AttrDebugTest1"));
	}

	[Test]
	public async Task AttributeNoDebugFlag_SuppressesOutput_EvenWithObjectDebug()
	{
		// Arrange - Create test object WITH DEBUG but set attribute WITH NODEBUG
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create AttrNoDebugTest1"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set AttrNoDebugTest1=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("&testfunc2 AttrNoDebugTest1=[add(55,44)]"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set AttrNoDebugTest1/testfunc2=NODEBUG"));
		
		NotifyService.ClearReceivedCalls();
		
		// Act - Trigger the attribute (which uses WithAttributeDebug)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@trigger AttrNoDebugTest1/testfunc2"));
		
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
		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy AttrNoDebugTest1"));
	}
}
