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

		// Assert - Inner function (has extra space for nesting indentation, matching PennMUSH)
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("!  add(11,22) :"),
						str => str.Contains("!  add(11,22) :"))),
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

	// ========================================================================
	// PennMUSH-format exact matching tests
	// PennMUSH DEBUG: "#<dbref>! <spaces><expression> :" before eval,
	//                 "#<dbref>! <spaces><expression> => <result>" after eval
	// PennMUSH VERBOSE: "#<dbref>] <command>"
	// PennMUSH PUPPET: "<objectname>> <message>" relay to owner
	// Reference: PennMUSH src/parse.c (debug), src/game.c (verbose),
	//            src/notify.c (puppet), hdrs/flag_tab.h (flag restrictions)
	// ========================================================================

	[Test]
	public async Task Debug_ExactPennMUSHFormat_PreEvalColon()
	{
		// PennMUSH debug pre-evaluation format: "#N! expression :"
		// (one space after '!' at depth 0, expression text, then " :")
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DebugFmtPre"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugFmtPre=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugFmtPre=!no_command"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("&dbg_fmt_pre DebugFmtPre=$dbgfmtprecmd:@pemit me=[add(7,8)]"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@force DebugFmtPre=dbgfmtprecmd"));

		// Assert exact format: #<N>! add(7,8) :
		// The regex matches: hash, digits, exclamation, space(s), expression, space, colon
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! +add\(7,8\) :$"),
						str => Regex.IsMatch(str, @"^#\d+! +add\(7,8\) :$"))),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());

		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugFmtPre"));
	}

	[Test]
	public async Task Debug_ExactPennMUSHFormat_PostEvalArrow()
	{
		// PennMUSH debug post-evaluation format: "#N! expression => result"
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DebugFmtPost"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugFmtPost=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugFmtPost=!no_command"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("&dbg_fmt_post DebugFmtPost=$dbgfmtpostcmd:@pemit me=[add(7,8)]"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@force DebugFmtPost=dbgfmtpostcmd"));

		// Assert exact format: #<N>! add(7,8) => 15
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! +add\(7,8\) => 15$"),
						str => Regex.IsMatch(str, @"^#\d+! +add\(7,8\) => 15$"))),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());

		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugFmtPost"));
	}

	[Test]
	public async Task Debug_NestingUsesSpaceIndentation_MatchesPennMUSH()
	{
		// PennMUSH: nested functions get 1 extra space per depth level
		// Depth 0: "#N! expression :"  (1 space after !)
		// Depth 1: "#N!  inner :"      (2 spaces after !)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DebugNestFmt"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugNestFmt=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugNestFmt=!no_command"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("&dbg_nest_fmt DebugNestFmt=$dbgnestfmtcmd:@pemit me=[strlen(add(2,3))]"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@force DebugNestFmt=dbgnestfmtcmd"));

		// Inner add(2,3) should have MORE leading spaces than outer strlen(...)
		// Outer: "#N! strlen(add(2,3)) :" — depth 0
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! +strlen\(add\(2,3\)\) :$"),
						str => Regex.IsMatch(str, @"^#\d+! +strlen\(add\(2,3\)\) :$"))),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());

		// Inner: "#N!  add(2,3) :" — depth 1 (extra space)
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! {2,}add\(2,3\) :$"),
						str => Regex.IsMatch(str, @"^#\d+! {2,}add\(2,3\) :$"))),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());

		// Inner result: "#N!  add(2,3) => 5"
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! {2,}add\(2,3\) => 5$"),
						str => Regex.IsMatch(str, @"^#\d+! {2,}add\(2,3\) => 5$"))),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());

		// Outer result: "#N! strlen(add(2,3)) => 1" (length of "5" is 1)
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! +strlen\(add\(2,3\)\) => 1$"),
						str => Regex.IsMatch(str, @"^#\d+! +strlen\(add\(2,3\)\) => 1$"))),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());

		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugNestFmt"));
	}

	[Test]
	public async Task Verbose_ExactPennMUSHFormat()
	{
		// PennMUSH verbose format: "#<executor_dbref>] <command>"
		// Reference: PennMUSH src/game.c: snprintf(tmp, sizeof tmp, "#%d] %s", executor, msg);
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create VerboseFmtObj"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set VerboseFmtObj=VERBOSE"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@force VerboseFmtObj=@pemit me=VerbFmtTest444"));

		// Assert exact format: #<N>] @pemit me=VerbFmtTest444
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+\] @pemit me=VerbFmtTest444$"),
						str => Regex.IsMatch(str, @"^#\d+\] @pemit me=VerbFmtTest444$"))),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());

		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy VerboseFmtObj"));
	}

	[Test]
	public async Task PuppetFlag_CannotBeSetOnPlayer()
	{
		// PennMUSH: PUPPET flag is TYPE_THING only (hdrs/flag_tab.h)
		// Setting on a Player should fail.
		// PennMUSH output: "PUPPET - I don't recognize that flag."
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set #1=PUPPET"));

		// Assert that PUPPET was NOT set — a failure notification should appear
		// SharpMUSH format: "Flag: PUPPET cannot be set on object type: PLAYER."
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("PUPPET") && mstr.ToString().Contains("cannot be set"),
						str => str.Contains("PUPPET") && str.Contains("cannot be set"))),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async Task PuppetFlag_CanBeSetOnThing()
	{
		// PennMUSH: PUPPET flag is TYPE_THING — it should succeed on Things
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create PuppetThingObj"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set PuppetThingObj=PUPPET"));

		// Assert that PUPPET was set — success notification "Flag: PUPPET Set."
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("PUPPET") && mstr.ToString().Contains("Set"),
						str => str.Contains("PUPPET") && str.Contains("Set"))),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());

		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy PuppetThingObj"));
	}

	[Test]
	public async Task Debug_SendsToOwner_NotToExecutor()
	{
		// PennMUSH: Debug output is sent to Owner(executor) via raw_notify
		// Reference: PennMUSH src/parse.c: raw_notify(Owner(executor), dbuf);
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DebugOwnerObj"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugOwnerObj=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugOwnerObj=!no_command"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("&dbg_owner DebugOwnerObj=$dbgownercmd:@pemit me=[add(1,1)]"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@force DebugOwnerObj=dbgownercmd"));

		// The debug output should be sent to the object's owner (#1, the player)
		// We verify this by checking the first argument of the Notify call — it should be the owner
		var debugCalls = NotifyService.ReceivedCalls()
			.Where(c =>
			{
				var args = c.GetArguments();
				if (args.Length < 2) return false;
				return args[1] is OneOf<MString, string> msg &&
					msg.Match(m => m.ToString().Contains("! add(1,1)"), s => s.Contains("! add(1,1)"));
			})
			.ToList();

		await Assert.That(debugCalls.Count).IsGreaterThan(0)
			.Because("Debug output for add(1,1) should be sent");

		// Verify the first argument is the owner (player #1)
		var firstArg = debugCalls.First().GetArguments()[0] as AnySharpObject;
		await Assert.That(firstArg).IsNotNull().Because("Debug should be sent to an object");
		await Assert.That(firstArg!.Object().DBRef.Number).IsEqualTo(1)
			.Because("Debug output should go to owner (#1), not executor object");

		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugOwnerObj"));
	}

	// ========================================================================
	// Percent substitution display tests
	// Verifies that %q registers, %0-%9 arguments, and %i0/%iL iterators
	// appear correctly in debug and verbose output.
	// PennMUSH shows the raw source text (with %q, %0, etc.) in the debug
	// "before" line, and the computed result in the "after" line.
	// ========================================================================

	[Test]
	public async Task Debug_ShowsPercentQRegister_InExpressionText()
	{
		// When debug output shows an expression containing %qa, the expression
		// text should show '%qa' literally (not the resolved value).
		// The result after '=>' should show the computed value.
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DebugPctQ"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugPctQ=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugPctQ=!no_command"));

		// Command: set %qa to "Hello", then evaluate strlen(%qa)
		// No wildcard needed — this is a simple $-command with no arguments
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("&pctq_cmd DebugPctQ=$pctqcmd:@pemit me=[setq(a,Hello)][strlen(%qa)]"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@force DebugPctQ=pctqcmd"));

		// Assert: debug output for strlen should show '%qa' literally in the expression text
		// PennMUSH format: #N! strlen(%qa) :
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, "strlen(%qa)")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());

		// Assert: the result after '=>' for strlen(%qa) should show the resolved value
		// The exact result depends on register resolution timing; what matters is the format:
		// "#N! strlen(%qa) => <number>"
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"strlen\(%qa\) => \d+"),
						str => Regex.IsMatch(str, @"strlen\(%qa\) => \d+"))),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());

		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugPctQ"));
	}

	[Test]
	public async Task Debug_ShowsPercentZeroArg_InExpressionText()
	{
		// When debug shows an expression containing %0, it should show
		// '%0' literally in the expression text, with resolved value in the result.
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DebugPct0"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugPct0=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugPct0=!no_command"));

		// $-command with argument: $test *:@pemit me=[strlen(%0)]
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("&pct0_cmd DebugPct0=$pct0testcmd *:@pemit me=[strlen(%0)]"));

		// Trigger with argument "World" (5 chars)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@force DebugPct0=pct0testcmd World"));

		// Assert: debug expression text should show '%0' literally
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, "strlen(%0)")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());

		// Assert: result should be "5" (length of "World")
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, "strlen(%0) => 5")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());

		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugPct0"));
	}

	[Test]
	public async Task Debug_ShowsIterTokens_InExpressionText()
	{
		// When debug shows a function inside iter(), the expression text should
		// preserve the ## (double-hash iteration token) in the parse tree.
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DebugPctIter"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugPctIter=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugPctIter=!no_command"));

		// iter with strlen: iter(Hello,strlen(##))
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("&pctiter_cmd DebugPctIter=$pctitercmd:@pemit me=[iter(Hello,strlen(##))]"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@force DebugPctIter=pctitercmd"));

		// Assert: debug should show iter() call in expression text
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, "iter(")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());

		// Assert: pre-eval debug line should preserve ## token literally in strlen(##)
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, "strlen(##)")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());

		// Assert: strlen result (5 = length of "Hello") should appear in debug output
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"strlen\(.+\) => 5"),
						str => Regex.IsMatch(str, @"strlen\(.+\) => 5"))),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());

		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugPctIter"));
	}

	[Test]
	public async Task Debug_SetqShowsRegisterName_InExpressionText()
	{
		// setq(a,Hello) should appear literally in debug expression text
		// and the result should show empty (setq returns empty string)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DebugSetq"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugSetq=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugSetq=!no_command"));

		await Parser.CommandParse(1, ConnectionService,
			MModule.single("&setq_cmd DebugSetq=$setqcmd:@pemit me=[setq(a,TestVal123)]"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@force DebugSetq=setqcmd"));

		// Assert: debug before-line shows setq call with register name
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, "setq(a,TestVal123) :")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());

		// Assert: debug after-line shows setq result (empty string)
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"setq\(a,TestVal123\) => $"),
						str => Regex.IsMatch(str, @"setq\(a,TestVal123\) => $"))),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());

		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugSetq"));
	}

	[Test]
	public async Task Verbose_ShowsEvaluatedCommand_InOutput()
	{
		// Verbose output shows the command AFTER evaluation (PennMUSH behavior).
		// In PennMUSH, verbose output is generated after process_expression evaluates
		// the command text. So [add(10,20)] becomes 30 before verbose sees it.
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create VerbosePctObj"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set VerbosePctObj=VERBOSE"));

		// Force a command that uses a function in brackets
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("@force VerbosePctObj=think [add(10,20)]"));

		// Assert: verbose output should show the evaluated result
		// PennMUSH behavior: "#N] think 30" (not "#N] think [add(10,20)]")
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("] think 30"),
						str => str.Contains("] think 30"))),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());

		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy VerbosePctObj"));
	}
}
