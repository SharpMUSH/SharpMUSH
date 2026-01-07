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
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	[Test]
	public async Task DebugFlag_OnObject_OutputsFunctionEvaluation()
	{
		// Arrange - Create object with DEBUG flag set
		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create Debug Test Object 1=10"));
		var setDebugResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@set Debug Test Object 1=DEBUG"));
		
		// Clear previous calls to NotifyService
		NotifyService.ClearReceivedCalls();
		
		// Act - Execute a function that should trigger debug output
		await Parser.CommandParse(1, ConnectionService, MModule.single("think [add(42,58)]"));
		
		// Assert - Verify debug output was sent
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("! add(42,58) :")) ||
				(msg.IsT1 && msg.AsT1.Contains("! add(42,58) :"))),
				Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
		
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("! add(42,58) => 100")) ||
				(msg.IsT1 && msg.AsT1.Contains("! add(42,58) => 100"))),
				Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async Task DebugFlag_WithNestedFunctions_ShowsProperIndentation()
	{
		// Arrange - Create object with DEBUG flag
		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create Debug Test Object 2=10"));
		var setDebugResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@set Debug Test Object 2=DEBUG"));
		
		NotifyService.ClearReceivedCalls();
		
		// Act - Execute nested function call
		await Parser.CommandParse(1, ConnectionService, MModule.single("think [mul(add(10,20),2)]"));
		
		// Assert - Verify outer function has no indent
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("!mul(add(10,20),2) :") && !msg.AsT0.ToString().Contains("! mul")) ||
				(msg.IsT1 && msg.AsT1.Contains("!mul(add(10,20),2) :") && !msg.AsT1.Contains("! mul"))));
		
		// Assert - Verify inner function has indentation (one space)
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("! add(10,20) :")) ||
				(msg.IsT1 && msg.AsT1.Contains("! add(10,20) :"))));
		
		// Assert - Verify result is correct
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("=> 60")) ||
				(msg.IsT1 && msg.AsT1.Contains("=> 60"))));
	}

	[Test]
	public async Task VerboseFlag_OnObject_OutputsCommandExecution()
	{
		// Arrange - Create object with VERBOSE flag
		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create Verbose Test Object 1=10"));
		var setVerboseResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@set Verbose Test Object 1=VERBOSE"));
		
		NotifyService.ClearReceivedCalls();
		
		// Act - Execute a command
		await Parser.CommandParse(1, ConnectionService, MModule.single("@pemit me=Verbose test message 123"));
		
		// Assert - Verify verbose output with specific command
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("] @pemit me=Verbose test message 123")) ||
				(msg.IsT1 && msg.AsT1.Contains("] @pemit me=Verbose test message 123"))));
	}

	[Test]
	public async Task AttributeDebugFlag_ForcesDebugOutput_EvenWithoutObjectDebug()
	{
		// Arrange - Create object WITHOUT DEBUG flag, but attribute WITH DEBUG flag
		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create Attr Debug Test 1=10"));
		var setAttrResult = await Parser.CommandParse(1, ConnectionService, MModule.single("&testfn Attr Debug Test 1=[add(15,25)]"));
		var setAttrFlagResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@set Attr Debug Test 1/testfn=DEBUG"));
		
		NotifyService.ClearReceivedCalls();
		
		// Act - Evaluate attribute via u()
		await Parser.CommandParse(1, ConnectionService, MModule.single("think [u(Attr Debug Test 1/testfn)]"));
		
		// Assert - Verify debug output appears despite object not having DEBUG
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("! add(15,25) :")) ||
				(msg.IsT1 && msg.AsT1.Contains("! add(15,25) :"))));
		
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("! add(15,25) => 40")) ||
				(msg.IsT1 && msg.AsT1.Contains("! add(15,25) => 40"))));
	}

	[Test]
	public async Task AttributeNoDebugFlag_SuppressesDebugOutput_EvenWithObjectDebug()
	{
		// Arrange - Create object WITH DEBUG flag, but attribute WITH NODEBUG flag
		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create Attr NoDebug Test 1=10"));
		var setDebugResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@set Attr NoDebug Test 1=DEBUG"));
		var setAttrResult = await Parser.CommandParse(1, ConnectionService, MModule.single("&testfn Attr NoDebug Test 1=[add(33,67)]"));
		var setAttrFlagResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@set Attr NoDebug Test 1/testfn=NODEBUG"));
		
		NotifyService.ClearReceivedCalls();
		
		// Act - Evaluate attribute via u()
		await Parser.CommandParse(1, ConnectionService, MModule.single("think [u(Attr NoDebug Test 1/testfn)]"));
		
		// Assert - Verify NO debug output appears (NODEBUG takes precedence)
		await NotifyService
			.DidNotReceive()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("! add(33,67)")) ||
				(msg.IsT1 && msg.AsT1.Contains("! add(33,67)"))));
	}

	[Test]
	public async Task DebugForwardList_SendsOutputToListedDbrefs()
	{
		// Arrange - Create two objects, set one as debug forward target
		var createObj1 = await Parser.CommandParse(1, ConnectionService, MModule.single("@create Debug Forward Test 1=10"));
		var createObj2 = await Parser.CommandParse(1, ConnectionService, MModule.single("@create Debug Forward Target 1=11"));
		
		// Set DEBUG flag and DEBUGFORWARDLIST
		var setDebugResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@set Debug Forward Test 1=DEBUG"));
		var setForwardList = await Parser.CommandParse(1, ConnectionService, MModule.single("&DEBUGFORWARDLIST Debug Forward Test 1=#11"));
		
		NotifyService.ClearReceivedCalls();
		
		// Act - Execute function
		await Parser.CommandParse(1, ConnectionService, MModule.single("think [add(7,8)]"));
		
		// Assert - Verify output sent to both owner and forward list dbref
		// Should be called at least twice - once for owner, once for forward list
		await NotifyService
			.Received(Arg.Is<int>(count => count >= 2))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("! add(7,8) => 15")) ||
				(msg.IsT1 && msg.AsT1.Contains("! add(7,8) => 15"))));
	}

	[Test]
	public async Task TriggerCommand_RespectsAttributeDebugFlag()
	{
		// Arrange - Create object and attribute with DEBUG flag
		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create Trigger Debug Test=10"));
		var setAttrResult = await Parser.CommandParse(1, ConnectionService, MModule.single("&ontrig Trigger Debug Test=[mul(5,9)]"));
		var setAttrFlagResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@set Trigger Debug Test/ontrig=DEBUG"));
		
		NotifyService.ClearReceivedCalls();
		
		// Act - Trigger the attribute
		await Parser.CommandParse(1, ConnectionService, MModule.single("@trigger Trigger Debug Test/ontrig"));
		
		// Assert - Verify debug output with expected value
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("! mul(5,9) => 45")) ||
				(msg.IsT1 && msg.AsT1.Contains("! mul(5,9) => 45"))));
	}
}
