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
		// Create a unique player as executor: the owned object's debug output goes to its owner.
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgEval");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create DebugEvalObj"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set DebugEvalObj=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set DebugEvalObj=!no_command"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("&test_cmd_eval DebugEvalObj=$test1command:@pemit me=[add(123,456)]"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@force DebugEvalObj=test1command"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! +\[add\(123,456\)\] :$"),
						str => Regex.IsMatch(str, @"^#\d+! +\[add\(123,456\)\] :$"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! +\[add\(123,456\)\] => 579$"),
						str => Regex.IsMatch(str, @"^#\d+! +\[add\(123,456\)\] => 579$"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@destroy DebugEvalObj"));
	}

	[Test]
	public async Task DebugFlag_ShowsNesting_WithIndentation()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgNest");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create DebugNestObj"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set DebugNestObj=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set DebugNestObj=!no_command"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("&test_cmd_nest DebugNestObj=$test2command:@pemit me=[mul(add(11,22),3)]"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@force DebugNestObj=test2command"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! +\[mul\(add\(11,22\),3\)\] :$"),
						str => Regex.IsMatch(str, @"^#\d+! +\[mul\(add\(11,22\),3\)\] :$"))), null, INotifyService.NotificationType.Announce);

		// Inner function (has extra space for nesting indentation, matching PennMUSH)
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! {2,}add\(11,22\) :$"),
						str => Regex.IsMatch(str, @"^#\d+! {2,}add\(11,22\) :$"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! +\[mul\(add\(11,22\),3\)\] => 99$"),
						str => Regex.IsMatch(str, @"^#\d+! +\[mul\(add\(11,22\),3\)\] => 99$"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@destroy DebugNestObj"));
	}

	[Test]
	public async Task VerboseFlag_OutputsCommandExecution()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "VerbExec");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create VerboseObj"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set VerboseObj=VERBOSE"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@force VerboseObj=@pemit me=UniqueTestMessage789"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+\] @pemit me=UniqueTestMessage789$"),
						str => Regex.IsMatch(str, @"^#\d+\] @pemit me=UniqueTestMessage789$"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@destroy VerboseObj"));
	}

	[Test]
	public async Task VerboseFlag_DoesNotDuplicateCommandName()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "VerbNoDup");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create VerboseNoDupObj"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set VerboseNoDupObj=VERBOSE"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@force VerboseNoDupObj=think UniqueNoDup777"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+\] think UniqueNoDup777$"),
						str => Regex.IsMatch(str, @"^#\d+\] think UniqueNoDup777$"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("] think think"),
						str => str.Contains("] think think"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@destroy VerboseNoDupObj"));
	}

	[Test]
	public async Task VerboseFlag_DoesNotDuplicateCommandNameWithSwitches()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "VerbNoDupSw");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create VerboseNoDupSwitchObj"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set VerboseNoDupSwitchObj=VERBOSE"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@force VerboseNoDupSwitchObj=@emit/noeval UniqueNoDupSwitch555"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+\] @emit/noeval UniqueNoDupSwitch555$"),
						str => Regex.IsMatch(str, @"^#\d+\] @emit/noeval UniqueNoDupSwitch555$"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("@emit/noeval @emit/noeval"),
						str => str.Contains("@emit/noeval @emit/noeval"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@destroy VerboseNoDupSwitchObj"));
	}

	[Test]
	public async Task AttributeDebugFlag_Diagnostic_FlagsLoadedAfterSet()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DiagDbg");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create DiagDebugThing"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("&DIAGFUNC_UNIQ2 DiagDebugThing=[add(1,2)]"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set DiagDebugThing/DIAGFUNC_UNIQ2=DEBUG"));

		var createCall = NotifyService.ReceivedCalls()
			.FirstOrDefault(c =>
			{
				var args = c.GetArguments();
				if (args.Length < 2) return false;
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

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@destroy DiagDebugThing"));
	}

	[Test]
	public async Task AttributeDebugFlag_ForcesOutput_EvenWithoutObjectDebug()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AttrDbgForce");
		// The attribute must contain a command with a function argument so that VisitFunction is called.
		// Using @emit [add(88,77)] ensures the bracket pattern is evaluated as a command argument.
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create AttrDebugForceTest"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("&TESTFUNC_ATTRDBG_UNIQUE AttrDebugForceTest=@emit [add(88,77)]"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set AttrDebugForceTest/TESTFUNC_ATTRDBG_UNIQUE=DEBUG"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@trigger AttrDebugForceTest/TESTFUNC_ATTRDBG_UNIQUE"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! +\[add\(88,77\)\] => 165$"),
						str => Regex.IsMatch(str, @"^#\d+! +\[add\(88,77\)\] => 165$"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@destroy AttrDebugForceTest"));
	}

	[Test]
	public async Task AttributeNoDebugFlag_SuppressesOutput_EvenWithObjectDebug()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AttrNoDbg");
		// The attribute must contain a command with a function argument so that VisitFunction is called.
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create AttrNoDebugSuppressTest"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set AttrNoDebugSuppressTest=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("&TESTFUNC2_NODEBG_UNIQUE AttrNoDebugSuppressTest=@emit [add(55,44)]"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set AttrNoDebugSuppressTest/TESTFUNC2_NODEBG_UNIQUE=no_debug"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@trigger AttrNoDebugSuppressTest/TESTFUNC2_NODEBG_UNIQUE"));

		// NODEBUG takes precedence over object DEBUG
		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("! add(55,44)"),
						str => str.Contains("! add(55,44)"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@destroy AttrNoDebugSuppressTest"));
	}

	[Test]
	public async Task DebugFlag_DoesNotOutputRegisterDumps()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgNoReg");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create DebugNoRegObj"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set DebugNoRegObj=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set DebugNoRegObj=!no_command"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("&test_cmd_noreg DebugNoRegObj=$test3command:@pemit me=[setq(a,Hello)][setq(b,World)][strlen(%qa)]"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@force DebugNoRegObj=test3command"));

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("[Q-Registers:"),
						str => str.Contains("[Q-Registers:"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("[Registers:"),
						str => str.Contains("[Registers:"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("[Iter-Registers:"),
						str => str.Contains("[Iter-Registers:"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@destroy DebugNoRegObj"));
	}

	[Test]
	public async Task Debug_ExactPennMUSHFormat_PreEvalColon()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgFmtPre");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create DebugFmtPre"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set DebugFmtPre=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set DebugFmtPre=!no_command"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("&dbg_fmt_pre DebugFmtPre=$dbgfmtprecmd:@pemit me=[add(7,8)]"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@force DebugFmtPre=dbgfmtprecmd"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! +\[add\(7,8\)\] :$"),
						str => Regex.IsMatch(str, @"^#\d+! +\[add\(7,8\)\] :$"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@destroy DebugFmtPre"));
	}

	[Test]
	public async Task Debug_ExactPennMUSHFormat_PostEvalArrow()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgFmtPost");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create DebugFmtPost"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set DebugFmtPost=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set DebugFmtPost=!no_command"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("&dbg_fmt_post DebugFmtPost=$dbgfmtpostcmd:@pemit me=[add(7,8)]"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@force DebugFmtPost=dbgfmtpostcmd"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! +\[add\(7,8\)\] => 15$"),
						str => Regex.IsMatch(str, @"^#\d+! +\[add\(7,8\)\] => 15$"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@destroy DebugFmtPost"));
	}

	[Test]
	public async Task Debug_NestingUsesSpaceIndentation_MatchesPennMUSH()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgNestFmt");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create DebugNestFmt"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set DebugNestFmt=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set DebugNestFmt=!no_command"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("&dbg_nest_fmt DebugNestFmt=$dbgnestfmtcmd:@pemit me=[strlen(add(2,3))]"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@force DebugNestFmt=dbgnestfmtcmd"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! +\[strlen\(add\(2,3\)\)\] :$"),
							str => Regex.IsMatch(str, @"^#\d+! +\[strlen\(add\(2,3\)\)\] :$"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! {2,}add\(2,3\) :$"),
						str => Regex.IsMatch(str, @"^#\d+! {2,}add\(2,3\) :$"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! {2,}add\(2,3\) => 5$"),
						str => Regex.IsMatch(str, @"^#\d+! {2,}add\(2,3\) => 5$"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! +\[strlen\(add\(2,3\)\)\] => 1$"),
							str => Regex.IsMatch(str, @"^#\d+! +\[strlen\(add\(2,3\)\)\] => 1$"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@destroy DebugNestFmt"));
	}

	[Test]
	public async Task Verbose_ExactPennMUSHFormat()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "VerbFmt");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create VerboseFmtObj"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set VerboseFmtObj=VERBOSE"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@force VerboseFmtObj=@pemit me=VerbFmtTest444"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+\] @pemit me=VerbFmtTest444$"),
						str => Regex.IsMatch(str, @"^#\d+\] @pemit me=VerbFmtTest444$"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@destroy VerboseFmtObj"));
	}

	[Test]
	public async Task PuppetFlag_CannotBeSetOnPlayer()
	{
		// Pattern C: unique receiver isolates the generic "Permission denied." message in the shared session.
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PuppetNoSet");

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set me=PUPPET"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), "Permission denied.", (AnySharpObject?)null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async Task PuppetFlag_CanBeSetOnThing()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PupSet");
		// Pattern B: unique object name is embedded in the flag-set message, making it globally unique.
		var uniqueName = TestIsolationHelpers.GenerateUniqueName("PuppetThing");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@create {uniqueName}"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {uniqueName}=PUPPET"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessagePlainTextEquals(msg, $"{uniqueName} - PUPPET set.")),
				(AnySharpObject?)null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@destroy {uniqueName}"));
	}

	[Test]
	public async Task Debug_SendsToOwner_NotToExecutor()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgOwner");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create DebugOwnerObj"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set DebugOwnerObj=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set DebugOwnerObj=!no_command"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("&dbg_owner DebugOwnerObj=$dbgownercmd:@pemit me=[add(1,1)]"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@force DebugOwnerObj=dbgownercmd"));

		var debugCalls = NotifyService.ReceivedCalls()
			.Where(c =>
			{
				var args = c.GetArguments();
				if (args.Length < 2) return false;
				return args[1] is OneOf<MString, string> msg &&
					msg.Match(m => m.ToString().Contains("! [add(1,1)]"), s => s.Contains("! [add(1,1)]"));
			})
			.ToList();

		await Assert.That(debugCalls.Count).IsGreaterThan(0)
			.Because("Debug output for add(1,1) should be sent");

		var firstArg = debugCalls.First().GetArguments()[0] as AnySharpObject;
		await Assert.That(firstArg).IsNotNull().Because("Debug should be sent to an object");
		await Assert.That(firstArg!.Object().DBRef.Number).IsEqualTo(testPlayer.DbRef.Number)
			.Because("Debug output should go to owner (testPlayer), not executor object");

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@destroy DebugOwnerObj"));
	}

	[Test]
	public async Task Debug_ShowsPercentQRegister_InExpressionText()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgPctQ");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create DebugPctQ"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set DebugPctQ=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set DebugPctQ=!no_command"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single("&pctq_cmd DebugPctQ=$pctqcmd:@pemit me=[setq(a,Hello)][strlen(%qa)]"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@force DebugPctQ=pctqcmd"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! +\[strlen\(%qa\)\] :$"),
						str => Regex.IsMatch(str, @"^#\d+! +\[strlen\(%qa\)\] :$"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! +\[strlen\(%qa\)\] => \d+$"),
						str => Regex.IsMatch(str, @"^#\d+! +\[strlen\(%qa\)\] => \d+$"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@destroy DebugPctQ"));
	}

	[Test]
	public async Task Debug_ShowsPercentZeroArg_InExpressionText()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgPct0");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create DebugPct0"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set DebugPct0=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set DebugPct0=!no_command"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single("&pct0_cmd DebugPct0=$pct0testcmd *:@pemit me=[strlen(%0)]"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@force DebugPct0=pct0testcmd World"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! +\[strlen\(%0\)\] :$"),
						str => Regex.IsMatch(str, @"^#\d+! +\[strlen\(%0\)\] :$"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! +\[strlen\(%0\)\] => 5$"),
						str => Regex.IsMatch(str, @"^#\d+! +\[strlen\(%0\)\] => 5$"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@destroy DebugPct0"));
	}

	[Test]
	public async Task Debug_ShowsIterTokens_InExpressionText()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgIter");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create DebugPctIter"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set DebugPctIter=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set DebugPctIter=!no_command"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single("&pctiter_cmd DebugPctIter=$pctitercmd:@pemit me=[iter(Hello,strlen(##))]"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@force DebugPctIter=pctitercmd"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! +\[iter\(Hello,strlen\(##\)\)\] :$"),
						str => Regex.IsMatch(str, @"^#\d+! +\[iter\(Hello,strlen\(##\)\)\] :$"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! +strlen\(%iL\) :$"),
						str => Regex.IsMatch(str, @"^#\d+! +strlen\(%iL\) :$"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! +strlen\(%iL\) => 5$"),
						str => Regex.IsMatch(str, @"^#\d+! +strlen\(%iL\) => 5$"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@destroy DebugPctIter"));
	}

	[Test]
	public async Task Debug_SetqShowsRegisterName_InExpressionText()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgSetq");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create DebugSetq"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set DebugSetq=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set DebugSetq=!no_command"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single("&setq_cmd DebugSetq=$setqcmd:@pemit me=[setq(a,TestVal123)]"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@force DebugSetq=setqcmd"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! +\[setq\(a,TestVal123\)\] :$"),
						str => Regex.IsMatch(str, @"^#\d+! +\[setq\(a,TestVal123\)\] :$"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! +\[setq\(a,TestVal123\)\] => $"),
						str => Regex.IsMatch(str, @"^#\d+! +\[setq\(a,TestVal123\)\] => $"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@destroy DebugSetq"));
	}

	[Test]
	public async Task Verbose_ShowsEvaluatedCommand_InOutput()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "VerbPct");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create VerbosePctObj"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set VerbosePctObj=VERBOSE"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single("@force VerbosePctObj=think [add(10,20)]"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+\] think 30$"),
						str => Regex.IsMatch(str, @"^#\d+\] think 30$"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@destroy VerbosePctObj"));
	}

	[Test]
	public async Task Debug_SubstitutionOnly_ShowsSingleLineFormat()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgSubst");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create DebugSubst"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set DebugSubst=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set DebugSubst=!no_command"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single("&subst_cmd DebugSubst=$substcmd:@pemit me=%# and %#"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@force DebugSubst=substcmd"));

		// PennMUSH substitution-only debug format: "#dbref! %# and %# => #<dbref> and #<dbref>"
		// Single line, no colon — fires when argument has substitutions but no function calls.
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => Regex.IsMatch(mstr.ToString(), @"^#\d+! %# and %# => #\d+ and #\d+$"),
						str => Regex.IsMatch(str, @"^#\d+! %# and %# => #\d+ and #\d+$"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@destroy DebugSubst"));
	}

	[Test]
	public async Task DebugForwardList_SendsDebugToSpecifiedPlayer()
	{
		var ownerPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgFwdOwner");
		var forwardPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgFwdTarget");

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MModule.single("@create DbgFwdObj"));
		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MModule.single("@set DbgFwdObj=DEBUG"));
		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MModule.single("@set DbgFwdObj=!no_command"));
		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService,
			MModule.single("&test_fwd DbgFwdObj=$dbgfwdcmd:@pemit me=[add(10,20)]"));

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService,
			MModule.single($"&DEBUGFORWARDLIST DbgFwdObj=#{forwardPlayer.DbRef.Number}"));

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MModule.single("@force DbgFwdObj=dbgfwdcmd"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(forwardPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("! [add(10,20)]"),
						str => str.Contains("! [add(10,20)]"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(ownerPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("! [add(10,20)]"),
						str => str.Contains("! [add(10,20)]"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MModule.single("@destroy DbgFwdObj"));
	}

	[Test]
	public async Task DebugForwardList_MultipleTargets_SendsToAll()
	{
		var ownerPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgMFwdOwn");
		var target1 = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgMFwdT1");
		var target2 = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgMFwdT2");

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MModule.single("@create DbgMFwdObj"));
		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MModule.single("@set DbgMFwdObj=DEBUG"));
		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MModule.single("@set DbgMFwdObj=!no_command"));
		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService,
			MModule.single("&test_mfwd DbgMFwdObj=$dbgmfwdcmd:@pemit me=[mul(3,7)]"));

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService,
			MModule.single($"&DEBUGFORWARDLIST DbgMFwdObj=#{target1.DbRef.Number} #{target2.DbRef.Number}"));

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MModule.single("@force DbgMFwdObj=dbgmfwdcmd"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(target1.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("! [mul(3,7)]"),
						str => str.Contains("! [mul(3,7)]"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(target2.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("! [mul(3,7)]"),
						str => str.Contains("! [mul(3,7)]"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MModule.single("@destroy DbgMFwdObj"));
	}

	[Test]
	public async Task DebugForwardList_NoAttribute_OnlySendsToOwner()
	{
		var ownerPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgNoFwdOwn");
		var otherPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgNoFwdOth");

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MModule.single("@create DbgNoFwdObj"));
		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MModule.single("@set DbgNoFwdObj=DEBUG"));
		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MModule.single("@set DbgNoFwdObj=!no_command"));
		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService,
			MModule.single("&test_nofwd DbgNoFwdObj=$dbgnofwdcmd:@pemit me=[sub(9,4)]"));

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MModule.single("@force DbgNoFwdObj=dbgnofwdcmd"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(ownerPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("! [sub(9,4)]"),
						str => str.Contains("! [sub(9,4)]"))), null, INotifyService.NotificationType.Announce);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(otherPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("! [sub(9,4)]"),
						str => str.Contains("! [sub(9,4)]"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MModule.single("@destroy DbgNoFwdObj"));
	}

	[Test]
	public async Task DebugForwardList_InvalidTarget_DoesNotCrash()
	{
		var ownerPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgBadFwdOwn");

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MModule.single("@create DbgBadFwdObj"));
		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MModule.single("@set DbgBadFwdObj=DEBUG"));
		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MModule.single("@set DbgBadFwdObj=!no_command"));
		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService,
			MModule.single("&test_badfwd DbgBadFwdObj=$dbgbadfwdcmd:@pemit me=[add(5,5)]"));

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService,
			MModule.single("&DEBUGFORWARDLIST DbgBadFwdObj=#99999"));

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MModule.single("@force DbgBadFwdObj=dbgbadfwdcmd"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(ownerPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						mstr => mstr.ToString().Contains("! [add(5,5)]"),
						str => str.Contains("! [add(5,5)]"))), null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MModule.single("@destroy DbgBadFwdObj"));
	}
}
