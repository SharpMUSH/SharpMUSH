using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class DebugVerboseTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async Task DebugFlag_OutputsFunctionEvaluation_WithSpecificValues()
	{
		// Arrange - Create test object and set DEBUG flag on it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DebugEvalObj"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugEvalObj=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugEvalObj=!no_command"));

		// Create a custom command on the object
		await Parser.CommandParse(1, ConnectionService, MModule.single("&test_cmd_eval DebugEvalObj=$test1command:@pemit me=[add(123,456)]"));

		// Act - Execute the custom command as the test object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@force DebugEvalObj=test1command"));

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
		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugEvalObj"));
	}

	[Test]
	public async Task DebugFlag_ShowsNesting_WithIndentation()
	{
		// Arrange - Create test object and set DEBUG flag on it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DebugNestObj"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugNestObj=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugNestObj=!no_command"));

		// Create a custom command on the object
		await Parser.CommandParse(1, ConnectionService, MModule.single("&test_cmd_nest DebugNestObj=$test2command:@pemit me=[mul(add(11,22),3)]"));

		// Act - Execute the custom command as the test object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@force DebugNestObj=test2command"));

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
					TestHelpers.MessageContains(msg, "=> 99")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());

		// Cleanup
		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugNestObj"));
	}

	[Test]
	public async Task VerboseFlag_OutputsCommandExecution()
	{
		// Arrange - Create test object and set VERBOSE flag on it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create VerboseObj"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set VerboseObj=VERBOSE"));

		// Act - Execute a command as the test object with unique message
		await Parser.CommandParse(1, ConnectionService, MModule.single("@force VerboseObj=@pemit me=UniqueTestMessage789"));

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
		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy VerboseObj"));
	}

	[Test]
	public async Task VerboseFlag_DoesNotDuplicateCommandName()
	{
		// Arrange - Create test object and set VERBOSE flag on it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create VerboseNoDupObj"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set VerboseNoDupObj=VERBOSE"));

		// Act - Execute a think command as the test object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@force VerboseNoDupObj=think UniqueNoDup777"));

		// Assert - Verbose output should show "think UniqueNoDup777" NOT "think think UniqueNoDup777"
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("] think UniqueNoDup777"),
						str => str.Contains("] think UniqueNoDup777"))),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());

		await NotifyService
			.DidNotReceive()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("] think think"),
						str => str.Contains("] think think"))),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());

		// Cleanup
		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy VerboseNoDupObj"));
	}

	[Test]
	public async Task VerboseFlag_DoesNotDuplicateCommandNameWithSwitches()
	{
		// Arrange - Create test object and set VERBOSE flag on it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create VerboseNoDupSwitchObj"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set VerboseNoDupSwitchObj=VERBOSE"));

		// Act - Execute a command with a switch as the test object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@force VerboseNoDupSwitchObj=@emit/noeval UniqueNoDupSwitch555"));

		// Assert - Verbose output should show "@emit/noeval UniqueNoDupSwitch555" NOT "@emit/noeval @emit/noeval UniqueNoDupSwitch555"
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("] @emit/noeval UniqueNoDupSwitch555"),
						str => str.Contains("] @emit/noeval UniqueNoDupSwitch555"))),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());

		await NotifyService
			.DidNotReceive()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("@emit/noeval @emit/noeval"),
						str => str.Contains("@emit/noeval @emit/noeval"))),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());

		// Cleanup
		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy VerboseNoDupSwitchObj"));
	}

	[Test]
	public async Task AttributeDebugFlag_Diagnostic_FlagsLoadedAfterSet()
	{
		// Diagnostic: Create a thing, add attribute, set DEBUG flag, verify flag is readable via inheritance query
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DiagDebugThing"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("&DIAGFUNC_UNIQ2 DiagDebugThing=[add(1,2)]"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DiagDebugThing/DIAGFUNC_UNIQ2=DEBUG"));

		// Get the DBRef of DiagDebugThing from the notification (created as "#N:...")
		var createCall = NotifyService.ReceivedCalls()
			.FirstOrDefault(c =>
			{
				var args = c.GetArguments();
				if (args.Length < 2) return false;
				return args[1] is OneOf<MString, string> msg &&
					msg.Match(m => m.ToString().Contains("DiagDebugThing"), s => s.Contains("DiagDebugThing"));
			});

		await Assert.That(createCall).IsNotNull().Because("@create should produce a notification");
		var createMsg = ((OneOf<MString, string>)createCall!.GetArguments()[1]!)
			.Match(m => m.ToString(), s => s);

		// Extract DBRef number from "Created DiagDebugThing (#N:...)."
		var match = Regex.Match(createMsg, @"#(\d+):");
		await Assert.That(match.Success).IsTrue().Because("Create notification should contain DBRef");
		var dbrefNum = int.Parse(match.Groups[1].Value);
		var dbref = new DBRef(dbrefNum);

		// Read via GetAttributeQuery (old path) - should pass
		var attrsOld = await Mediator.CreateStream(new GetAttributeQuery(
			dbref, ["DIAGFUNC_UNIQ2"])).ToArrayAsync();
		var flagsOld = attrsOld.LastOrDefault()?.Flags.ToList() ?? [];
		var hasDebugOld = flagsOld.Any(f => f.Name.Equals("debug", StringComparison.OrdinalIgnoreCase));
		await Assert.That(hasDebugOld).IsTrue().Because("GetAttributeQuery should return DEBUG flag");

		// Read via GetAttributeWithInheritanceQuery (new path used by @trigger) - must also pass
		var attrInheritance = await Mediator.CreateStream(new GetAttributeWithInheritanceQuery(
			dbref, ["DIAGFUNC_UNIQ2"], false)).ToArrayAsync();
		var flagsNew = attrInheritance.FirstOrDefault()?.Attributes.Last().Flags.ToList() ?? [];
		var hasDebugNew = flagsNew.Any(f => f.Name.Equals("debug", StringComparison.OrdinalIgnoreCase));
		await Assert.That(hasDebugNew).IsTrue()
			.Because($"GetAttributeWithInheritanceQuery must also return DEBUG flag (old flags: {string.Join(",", flagsOld.Select(f => f.Name))}, new flags: {string.Join(",", flagsNew.Select(f => f.Name))})");

		// Cleanup
		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DiagDebugThing"));
	}

	[Test]
	public async Task AttributeDebugFlag_ForcesOutput_EvenWithoutObjectDebug()
	{
		// Arrange - Create test object WITHOUT DEBUG, set attribute with DEBUG flag
		// The attribute must contain a command with a function argument so that VisitFunction is called.
		// Using @emit [add(88,77)] ensures the bracket pattern is evaluated as a command argument.
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create AttrDebugForceTest"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("&TESTFUNC_ATTRDBG_UNIQUE AttrDebugForceTest=@emit [add(88,77)]"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set AttrDebugForceTest/TESTFUNC_ATTRDBG_UNIQUE=DEBUG"));

		// Act - Trigger the attribute (which uses WithAttributeDebug internally)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@trigger AttrDebugForceTest/TESTFUNC_ATTRDBG_UNIQUE"));

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
		// The attribute must contain a command with a function argument so that VisitFunction is called.
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create AttrNoDebugSuppressTest"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set AttrNoDebugSuppressTest=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("&TESTFUNC2_NODEBG_UNIQUE AttrNoDebugSuppressTest=@emit [add(55,44)]"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set AttrNoDebugSuppressTest/TESTFUNC2_NODEBG_UNIQUE=no_debug"));

		// Act - Trigger the attribute (which uses WithAttributeDebug internally)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@trigger AttrNoDebugSuppressTest/TESTFUNC2_NODEBG_UNIQUE"));

		// Assert - Should NOT see debug output (NODEBUG takes precedence over object DEBUG)
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

	[Test]
	public async Task DebugFlag_DoesNotOutputRegisterDumps()
	{
		// PennMUSH does NOT emit [Registers:], [Q-Registers:], or [Iter-Registers:] blocks
		// in debug output. It only shows function calls with ':' and results with '=>'.

		// Arrange - Create test object and set DEBUG flag
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DebugNoRegObj"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugNoRegObj=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugNoRegObj=!no_command"));

		// Create command that sets q-registers and uses them
		await Parser.CommandParse(1, ConnectionService, MModule.single("&test_cmd_noreg DebugNoRegObj=$test3command:@pemit me=[setq(a,Hello)][setq(b,World)][strlen(%qa)]"));

		// Act
		await Parser.CommandParse(1, ConnectionService, MModule.single("@force DebugNoRegObj=test3command"));

		// Assert - Verify NO register dump blocks appear (PennMUSH compatibility)
		await NotifyService
			.DidNotReceive()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("[Q-Registers:"),
						str => str.Contains("[Q-Registers:"))),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());

		await NotifyService
			.DidNotReceive()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("[Registers:"),
						str => str.Contains("[Registers:"))),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());

		await NotifyService
			.DidNotReceive()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("[Iter-Registers:"),
						str => str.Contains("[Iter-Registers:"))),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());

		// Cleanup
		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugNoRegObj"));
	}
}
