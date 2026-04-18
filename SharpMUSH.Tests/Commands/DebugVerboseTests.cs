using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.Definitions;
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
		var executor = WebAppFactoryArg.ExecutorDBRef;
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
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("! add(123,456) :"),
						str => str.Contains("! add(123,456) :"))), null, INotifyService.NotificationType.Announce);

		// Assert - Verify debug output contains the specific result
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("! add(123,456) => 579"),
						str => str.Contains("! add(123,456) => 579"))), null, INotifyService.NotificationType.Announce);

		// Cleanup
		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugEvalObj"));
	}

	[Test]
	public async Task DebugFlag_ShowsNesting_WithIndentation()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
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
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("! mul(add(11,22),3) :"),
						str => str.Contains("! mul(add(11,22),3) :"))), null, INotifyService.NotificationType.Announce);

		// Assert - Inner function (has extra space for nesting indentation, matching PennMUSH)
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("!  add(11,22) :"),
						str => str.Contains("!  add(11,22) :"))), null, INotifyService.NotificationType.Announce);

		// Assert - Final result should be 99
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, "=> 99")), null, INotifyService.NotificationType.Announce);

		// Cleanup
		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugNestObj"));
	}

	[Test]
	public async Task VerboseFlag_OutputsCommandExecution()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Arrange - Create test object and set VERBOSE flag on it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create VerboseObj"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set VerboseObj=VERBOSE"));

		// Act - Execute a command as the test object with unique message
		await Parser.CommandParse(1, ConnectionService, MModule.single("@force VerboseObj=@pemit me=UniqueTestMessage789"));

		// Assert - Verify VERBOSE output (format: "#dbref] command")
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("] ") && mstr.ToString().Contains("@pemit me=UniqueTestMessage789"),
						str => str.Contains("] ") && str.Contains("@pemit me=UniqueTestMessage789"))), null, INotifyService.NotificationType.Announce);

		// Cleanup
		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy VerboseObj"));
	}

	[Test]
	public async Task VerboseFlag_DoesNotDuplicateCommandName()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Arrange - Create test object and set VERBOSE flag on it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create VerboseNoDupObj"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set VerboseNoDupObj=VERBOSE"));

		// Act - Execute a think command as the test object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@force VerboseNoDupObj=think UniqueNoDup777"));

		// Assert - Verbose output should show "think UniqueNoDup777" NOT "think think UniqueNoDup777"
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("] think UniqueNoDup777"),
						str => str.Contains("] think UniqueNoDup777"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("] think think"),
						str => str.Contains("] think think"))), null, INotifyService.NotificationType.Announce);

		// Cleanup
		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy VerboseNoDupObj"));
	}

	[Test]
	public async Task VerboseFlag_DoesNotDuplicateCommandNameWithSwitches()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Arrange - Create test object and set VERBOSE flag on it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create VerboseNoDupSwitchObj"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set VerboseNoDupSwitchObj=VERBOSE"));

		// Act - Execute a command with a switch as the test object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@force VerboseNoDupSwitchObj=@emit/noeval UniqueNoDupSwitch555"));

		// Assert - Verbose output should show "@emit/noeval UniqueNoDupSwitch555" NOT "@emit/noeval @emit/noeval UniqueNoDupSwitch555"
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("] @emit/noeval UniqueNoDupSwitch555"),
						str => str.Contains("] @emit/noeval UniqueNoDupSwitch555"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("@emit/noeval @emit/noeval"),
						str => str.Contains("@emit/noeval @emit/noeval"))), null, INotifyService.NotificationType.Announce);

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
				// Legacy Notify path
				if (args[1] is OneOf<MString, string> msg)
					return msg.Match(m => m.ToString().Contains("DiagDebugThing"), s => s.Contains("DiagDebugThing"));
				// NotifyLocalized path with sender overload: (who, key, sender, params object[] formatArgs)
				// args[3] is the params array: [name, dbref]
				if (args[1] is string && args.Length > 3 && args[3] is object[] formatArgs)
					return formatArgs.Any(a => a?.ToString()?.Contains("DiagDebugThing") == true);
				return false;
			});

		await Assert.That(createCall).IsNotNull().Because("@create should produce a notification");

		string createMsg;
		var createArgs = createCall!.GetArguments();
		if (createArgs[1] is OneOf<MString, string> omsg)
		{
			createMsg = omsg.Match(m => m.ToString(), s => s);
		}
		else if (createArgs[1] is string && createArgs.Length > 3 && createArgs[3] is object[] fmtArgs && fmtArgs.Length > 1)
		{
			// NotifyLocalized with sender: (who, key, sender, params object[] {name, dbref})
			// fmtArgs[1] is the DBRef object → ToString() = "#N"
			createMsg = fmtArgs[1]?.ToString() ?? string.Empty;
		}
		else
		{
			createMsg = string.Empty;
		}

		// Extract DBRef number from "Created DiagDebugThing (#N)." or "#N"
		var match = Regex.Match(createMsg, @"#(\d+)");
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
		var executor = WebAppFactoryArg.ExecutorDBRef;
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
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("add(88,77) => 165"),
						str => str.Contains("add(88,77) => 165"))), null, INotifyService.NotificationType.Announce);

		// Cleanup
		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy AttrDebugForceTest"));
	}

	[Test]
	public async Task AttributeNoDebugFlag_SuppressesOutput_EvenWithObjectDebug()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
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
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("! add(55,44)"),
						str => str.Contains("! add(55,44)"))), null, INotifyService.NotificationType.Announce);

		// Cleanup
		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy AttrNoDebugSuppressTest"));
	}

	[Test]
	public async Task DebugFlag_DoesNotOutputRegisterDumps()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Arrange - Create test object and set DEBUG flag
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DebugNoRegObj"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugNoRegObj=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugNoRegObj=!no_command"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("&test_cmd_noreg DebugNoRegObj=$test3command:@pemit me=[setq(a,Hello)][setq(b,World)][strlen(%qa)]"));

		// Act
		await Parser.CommandParse(1, ConnectionService, MModule.single("@force DebugNoRegObj=test3command"));

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("[Q-Registers:"),
						str => str.Contains("[Q-Registers:"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("[Registers:"),
						str => str.Contains("[Registers:"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("[Iter-Registers:"),
						str => str.Contains("[Iter-Registers:"))), null, INotifyService.NotificationType.Announce);

		// Cleanup
		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugNoRegObj"));
	}

	[Test]
	public async Task Debug_ExactPennMUSHFormat_PreEvalColon()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DebugFmtPre"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugFmtPre=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugFmtPre=!no_command"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("&dbg_fmt_pre DebugFmtPre=$dbgfmtprecmd:@pemit me=[add(7,8)]"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@force DebugFmtPre=dbgfmtprecmd"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! +add\(7,8\) :$"),
						str => Regex.IsMatch(str, @"^#\d+! +add\(7,8\) :$"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugFmtPre"));
	}

	[Test]
	public async Task Debug_ExactPennMUSHFormat_PostEvalArrow()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DebugFmtPost"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugFmtPost=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugFmtPost=!no_command"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("&dbg_fmt_post DebugFmtPost=$dbgfmtpostcmd:@pemit me=[add(7,8)]"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@force DebugFmtPost=dbgfmtpostcmd"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! +add\(7,8\) => 15$"),
						str => Regex.IsMatch(str, @"^#\d+! +add\(7,8\) => 15$"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugFmtPost"));
	}

	[Test]
	public async Task Debug_NestingUsesSpaceIndentation_MatchesPennMUSH()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DebugNestFmt"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugNestFmt=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugNestFmt=!no_command"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("&dbg_nest_fmt DebugNestFmt=$dbgnestfmtcmd:@pemit me=[strlen(add(2,3))]"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@force DebugNestFmt=dbgnestfmtcmd"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! +strlen\(add\(2,3\)\) :$"),
						str => Regex.IsMatch(str, @"^#\d+! +strlen\(add\(2,3\)\) :$"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! {2,}add\(2,3\) :$"),
						str => Regex.IsMatch(str, @"^#\d+! {2,}add\(2,3\) :$"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! {2,}add\(2,3\) => 5$"),
						str => Regex.IsMatch(str, @"^#\d+! {2,}add\(2,3\) => 5$"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! +strlen\(add\(2,3\)\) => 1$"),
						str => Regex.IsMatch(str, @"^#\d+! +strlen\(add\(2,3\)\) => 1$"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugNestFmt"));
	}

	[Test]
	public async Task Verbose_ExactPennMUSHFormat()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create VerboseFmtObj"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set VerboseFmtObj=VERBOSE"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@force VerboseFmtObj=@pemit me=VerbFmtTest444"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+\] @pemit me=VerbFmtTest444$"),
						str => Regex.IsMatch(str, @"^#\d+\] @pemit me=VerbFmtTest444$"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy VerboseFmtObj"));
	}

	[Test]
	public async Task PuppetFlag_CannotBeSetOnPlayer()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set #1=PUPPET"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("Permission denied"),
						str => str.Contains("Permission denied"))), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async Task PuppetFlag_CanBeSetOnThing()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create PuppetThingObj"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set PuppetThingObj=PUPPET"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("PUPPET") && mstr.ToString().Contains("set."),
						str => str.Contains("PUPPET") && str.Contains("set."))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy PuppetThingObj"));
	}

	[Test]
	public async Task Debug_SendsToOwner_NotToExecutor()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DebugOwnerObj"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugOwnerObj=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugOwnerObj=!no_command"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("&dbg_owner DebugOwnerObj=$dbgownercmd:@pemit me=[add(1,1)]"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@force DebugOwnerObj=dbgownercmd"));

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

		var firstArg = debugCalls.First().GetArguments()[0] as AnySharpObject;
		await Assert.That(firstArg).IsNotNull().Because("Debug should be sent to an object");
		await Assert.That(firstArg!.Object().DBRef.Number).IsEqualTo(1)
			.Because("Debug output should go to owner (#1), not executor object");

		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugOwnerObj"));
	}

	[Test]
	public async Task Debug_ShowsPercentQRegister_InExpressionText()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DebugPctQ"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugPctQ=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugPctQ=!no_command"));

		await Parser.CommandParse(1, ConnectionService,
			MModule.single("&pctq_cmd DebugPctQ=$pctqcmd:@pemit me=[setq(a,Hello)][strlen(%qa)]"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@force DebugPctQ=pctqcmd"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, "strlen(%qa)")), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"strlen\(%qa\) => \d+"),
						str => Regex.IsMatch(str, @"strlen\(%qa\) => \d+"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugPctQ"));
	}

	[Test]
	public async Task Debug_ShowsPercentZeroArg_InExpressionText()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DebugPct0"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugPct0=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugPct0=!no_command"));

		await Parser.CommandParse(1, ConnectionService,
			MModule.single("&pct0_cmd DebugPct0=$pct0testcmd *:@pemit me=[strlen(%0)]"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@force DebugPct0=pct0testcmd World"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, "strlen(%0)")), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, "strlen(%0) => 5")), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugPct0"));
	}

	[Test]
	public async Task Debug_ShowsIterTokens_InExpressionText()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DebugPctIter"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugPctIter=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugPctIter=!no_command"));

		await Parser.CommandParse(1, ConnectionService,
			MModule.single("&pctiter_cmd DebugPctIter=$pctitercmd:@pemit me=[iter(Hello,strlen(##))]"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@force DebugPctIter=pctitercmd"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, "iter(")), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, "strlen(##)")), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"strlen\(.+\) => 5"),
						str => Regex.IsMatch(str, @"strlen\(.+\) => 5"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugPctIter"));
	}

	[Test]
	public async Task Debug_SetqShowsRegisterName_InExpressionText()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DebugSetq"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugSetq=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set DebugSetq=!no_command"));

		await Parser.CommandParse(1, ConnectionService,
			MModule.single("&setq_cmd DebugSetq=$setqcmd:@pemit me=[setq(a,TestVal123)]"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@force DebugSetq=setqcmd"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, "setq(a,TestVal123) :")), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"setq\(a,TestVal123\) => $"),
						str => Regex.IsMatch(str, @"setq\(a,TestVal123\) => $"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugSetq"));
	}

	[Test]
	public async Task Verbose_ShowsEvaluatedCommand_InOutput()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create VerbosePctObj"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set VerbosePctObj=VERBOSE"));

		await Parser.CommandParse(1, ConnectionService,
			MModule.single("@force VerbosePctObj=think [add(10,20)]"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("] think 30"),
						str => str.Contains("] think 30"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy VerbosePctObj"));
	}
}
