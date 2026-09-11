using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
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
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@create DebugEvalObj"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DebugEvalObj=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DebugEvalObj=!no_command"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("&test_cmd_eval DebugEvalObj=$test1command:@pemit me=[add(123,456)]"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@force DebugEvalObj=test1command"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+! +\[add\(123,456\)\] :$")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+! +\[add\(123,456\)\] => 579$")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy DebugEvalObj"));
	}

	[Test]
	public async Task DebugFlag_ShowsNesting_WithIndentation()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgNest");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@create DebugNestObj"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DebugNestObj=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DebugNestObj=!no_command"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("&test_cmd_nest DebugNestObj=$test2command:@pemit me=[mul(add(11,22),3)]"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@force DebugNestObj=test2command"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+! +\[mul\(add\(11,22\),3\)\] :$")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		// Inner function (has extra space for nesting indentation, matching PennMUSH)
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+! {2,}add\(11,22\) :$")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+! +\[mul\(add\(11,22\),3\)\] => 99$")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy DebugNestObj"));
	}

	[Test]
	public async Task VerboseFlag_OutputsCommandExecution()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "VerbExec");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@create VerboseObj"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set VerboseObj=VERBOSE"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@force VerboseObj=@pemit me=UniqueTestMessage789"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+\] @pemit me=UniqueTestMessage789$")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy VerboseObj"));
	}

	[Test]
	public async Task VerboseFlag_DoesNotDuplicateCommandName()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "VerbNoDup");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@create VerboseNoDupObj"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set VerboseNoDupObj=VERBOSE"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@force VerboseNoDupObj=think UniqueNoDup777"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+\] think UniqueNoDup777$")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					TestHelpers.MessagePlainTextContains(msg, "] think think")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy VerboseNoDupObj"));
	}

	[Test]
	public async Task VerboseFlag_DoesNotDuplicateCommandNameWithSwitches()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "VerbNoDupSw");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@create VerboseNoDupSwitchObj"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set VerboseNoDupSwitchObj=VERBOSE"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@force VerboseNoDupSwitchObj=@emit/noeval UniqueNoDupSwitch555"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+\] @emit/noeval UniqueNoDupSwitch555$")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					TestHelpers.MessagePlainTextContains(msg, "@emit/noeval @emit/noeval")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy VerboseNoDupSwitchObj"));
	}

	[Test]
	public async Task AttributeDebugFlag_Diagnostic_FlagsLoadedAfterSet()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DiagDbg");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@create DiagDebugThing"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("&DIAGFUNC_UNIQ2 DiagDebugThing=[add(1,2)]"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DiagDebugThing/DIAGFUNC_UNIQ2=DEBUG"));

		var createCall = NotifyService.ReceivedCalls()
			.FirstOrDefault(c =>
			{
				var args = c.GetArguments();
				if (args.Length < 2) return false;
				if (args[1] is SharpMessage msg)
					return TestHelpers.MessagePlainTextContains(msg, "DiagDebugThing");
				// NotifyLocalized path with sender overload: (who, key, sender, params object[] formatArgs)
				// args[3] is the params array: [name, dbref]
				if (args[1] is string && args.Length > 3 && args[3] is object[] formatArgs)
					return formatArgs.Any(a => a?.ToString()?.Contains("DiagDebugThing") == true);
				return false;
			});

		await Assert.That(createCall).IsNotNull().Because("@create should produce a notification");

		string createMsg;
		var createArgs = createCall!.GetArguments();
		if (createArgs[1] is SharpMessage omsg)
		{
			createMsg = PlainText(omsg);
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

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy DiagDebugThing"));
	}

	[Test]
	public async Task AttributeDebugFlag_ForcesOutput_EvenWithoutObjectDebug()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AttrDbgForce");
		// The attribute must contain a command with a function argument so that VisitFunction is called.
		// Using @emit [add(88,77)] ensures the bracket pattern is evaluated as a command argument.
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@create AttrDebugForceTest"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("&TESTFUNC_ATTRDBG_UNIQUE AttrDebugForceTest=@emit [add(88,77)]"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set AttrDebugForceTest/TESTFUNC_ATTRDBG_UNIQUE=DEBUG"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@trigger AttrDebugForceTest/TESTFUNC_ATTRDBG_UNIQUE"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+! +\[add\(88,77\)\] => 165$")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy AttrDebugForceTest"));
	}

	[Test]
	public async Task AttributeNoDebugFlag_SuppressesOutput_EvenWithObjectDebug()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AttrNoDbg");
		// The attribute must contain a command with a function argument so that VisitFunction is called.
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@create AttrNoDebugSuppressTest"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set AttrNoDebugSuppressTest=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("&TESTFUNC2_NODEBG_UNIQUE AttrNoDebugSuppressTest=@emit [add(55,44)]"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set AttrNoDebugSuppressTest/TESTFUNC2_NODEBG_UNIQUE=no_debug"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@trigger AttrNoDebugSuppressTest/TESTFUNC2_NODEBG_UNIQUE"));

		// NODEBUG takes precedence over object DEBUG
		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					TestHelpers.MessagePlainTextContains(msg, "! add(55,44)")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy AttrNoDebugSuppressTest"));
	}

	[Test]
	public async Task DebugFlag_DoesNotOutputRegisterDumps()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgNoReg");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@create DebugNoRegObj"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DebugNoRegObj=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DebugNoRegObj=!no_command"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("&test_cmd_noreg DebugNoRegObj=$test3command:@pemit me=[setq(a,Hello)][setq(b,World)][strlen(%qa)]"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@force DebugNoRegObj=test3command"));

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					TestHelpers.MessagePlainTextContains(msg, "[Q-Registers:")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					TestHelpers.MessagePlainTextContains(msg, "[Registers:")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					TestHelpers.MessagePlainTextContains(msg, "[Iter-Registers:")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy DebugNoRegObj"));
	}

	[Test]
	public async Task Debug_ExactPennMUSHFormat_PreEvalColon()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgFmtPre");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@create DebugFmtPre"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DebugFmtPre=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DebugFmtPre=!no_command"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("&dbg_fmt_pre DebugFmtPre=$dbgfmtprecmd:@pemit me=[add(7,8)]"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@force DebugFmtPre=dbgfmtprecmd"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+! +\[add\(7,8\)\] :$")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy DebugFmtPre"));
	}

	[Test]
	public async Task Debug_ExactPennMUSHFormat_PostEvalArrow()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgFmtPost");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@create DebugFmtPost"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DebugFmtPost=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DebugFmtPost=!no_command"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("&dbg_fmt_post DebugFmtPost=$dbgfmtpostcmd:@pemit me=[add(7,8)]"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@force DebugFmtPost=dbgfmtpostcmd"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+! +\[add\(7,8\)\] => 15$")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy DebugFmtPost"));
	}

	[Test]
	public async Task Debug_NestingUsesSpaceIndentation_MatchesPennMUSH()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgNestFmt");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@create DebugNestFmt"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DebugNestFmt=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DebugNestFmt=!no_command"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("&dbg_nest_fmt DebugNestFmt=$dbgnestfmtcmd:@pemit me=[strlen(add(2,3))]"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@force DebugNestFmt=dbgnestfmtcmd"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+! +\[strlen\(add\(2,3\)\)\] :$")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+! {2,}add\(2,3\) :$")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+! {2,}add\(2,3\) => 5$")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+! +\[strlen\(add\(2,3\)\)\] => 1$")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy DebugNestFmt"));
	}

	[Test]
	public async Task Verbose_ExactPennMUSHFormat()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "VerbFmt");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@create VerboseFmtObj"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set VerboseFmtObj=VERBOSE"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@force VerboseFmtObj=@pemit me=VerbFmtTest444"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+\] @pemit me=VerbFmtTest444$")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy VerboseFmtObj"));
	}

	[Test]
	public async Task PuppetFlag_CannotBeSetOnPlayer()
	{
		// Pattern C: unique receiver isolates the generic "Permission denied." message in the shared session.
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PuppetNoSet");

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set me=PUPPET"));

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
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain($"@create {uniqueName}"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain($"@set {uniqueName}=PUPPET"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					TestHelpers.MessagePlainTextEquals(msg, $"{uniqueName} - PUPPET set.")),
				(AnySharpObject?)null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain($"@destroy {uniqueName}"));
	}

	[Test]
	public async Task Debug_SendsToOwner_NotToExecutor()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgOwner");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@create DebugOwnerObj"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DebugOwnerObj=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DebugOwnerObj=!no_command"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("&dbg_owner DebugOwnerObj=$dbgownercmd:@pemit me=[add(1,1)]"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@force DebugOwnerObj=dbgownercmd"));

		var debugCalls = NotifyService.ReceivedCalls()
			.Where(c =>
			{
				var args = c.GetArguments();
				if (args.Length < 2) return false;
				return args[1] is SharpMessage msg &&
					TestHelpers.MessagePlainTextContains(msg, "! [add(1,1)]");
			})
			.ToList();

		await Assert.That(debugCalls.Count).IsGreaterThan(0)
			.Because("Debug output for add(1,1) should be sent");

		var firstArg = debugCalls.First().GetArguments()[0] as AnySharpObject;
		await Assert.That(firstArg).IsNotNull().Because("Debug should be sent to an object");
		await Assert.That(firstArg!.Object().DBRef.Number).IsEqualTo(testPlayer.DbRef.Number)
			.Because("Debug output should go to owner (testPlayer), not executor object");

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy DebugOwnerObj"));
	}

	[Test]
	public async Task Debug_ShowsPercentQRegister_InExpressionText()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgPctQ");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@create DebugPctQ"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DebugPctQ=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DebugPctQ=!no_command"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MarkupText.Plain("&pctq_cmd DebugPctQ=$pctqcmd:@pemit me=[setq(a,Hello)][strlen(%qa)]"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@force DebugPctQ=pctqcmd"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+! +\[strlen\(%qa\)\] :$")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+! +\[strlen\(%qa\)\] => \d+$")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy DebugPctQ"));
	}

	[Test]
	public async Task Debug_ShowsPercentZeroArg_InExpressionText()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgPct0");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@create DebugPct0"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DebugPct0=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DebugPct0=!no_command"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MarkupText.Plain("&pct0_cmd DebugPct0=$pct0testcmd *:@pemit me=[strlen(%0)]"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@force DebugPct0=pct0testcmd World"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+! +\[strlen\(%0\)\] :$")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+! +\[strlen\(%0\)\] => 5$")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy DebugPct0"));
	}

	[Test]
	public async Task Debug_ShowsIterTokens_InExpressionText()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgIter");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@create DebugPctIter"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DebugPctIter=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DebugPctIter=!no_command"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MarkupText.Plain("&pctiter_cmd DebugPctIter=$pctitercmd:@pemit me=[iter(Hello,strlen(##))]"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@force DebugPctIter=pctitercmd"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+! +\[iter\(Hello,strlen\(##\)\)\] :$")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+! +strlen\(%iL\) :$")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+! +strlen\(%iL\) => 5$")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy DebugPctIter"));
	}

	[Test]
	public async Task Debug_SetqShowsRegisterName_InExpressionText()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgSetq");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@create DebugSetq"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DebugSetq=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DebugSetq=!no_command"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MarkupText.Plain("&setq_cmd DebugSetq=$setqcmd:@pemit me=[setq(a,TestVal123)]"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@force DebugSetq=setqcmd"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+! +\[setq\(a,TestVal123\)\] :$")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+! +\[setq\(a,TestVal123\)\] => $")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy DebugSetq"));
	}

	[Test]
	public async Task Verbose_ShowsEvaluatedCommand_InOutput()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "VerbPct");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@create VerbosePctObj"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set VerbosePctObj=VERBOSE"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MarkupText.Plain("@force VerbosePctObj=think [add(10,20)]"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+\] think 30$")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy VerbosePctObj"));
	}

	[Test]
	public async Task Debug_SubstitutionOnly_ShowsSingleLineFormat()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgSubst");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@create DebugSubst"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DebugSubst=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DebugSubst=!no_command"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MarkupText.Plain("&subst_cmd DebugSubst=$substcmd:@pemit me=%# and %#"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@force DebugSubst=substcmd"));

		// PennMUSH substitution-only debug format: "#dbref! %# and %# => #<dbref> and #<dbref>"
		// Single line, no colon — fires when argument has substitutions but no function calls.
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					Regex.IsMatch(PlainText(msg), @"^#\d+! %# and %# => #\d+ and #\d+$")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy DebugSubst"));
	}

	[Test]
	public async Task DebugForwardList_SendsDebugToSpecifiedPlayer()
	{
		var ownerPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgFwdOwner");
		var forwardPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgFwdTarget");

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MarkupText.Plain("@create DbgFwdObj"));
		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MarkupText.Plain("@set DbgFwdObj=DEBUG"));
		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MarkupText.Plain("@set DbgFwdObj=!no_command"));
		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService,
			MarkupText.Plain("&test_fwd DbgFwdObj=$dbgfwdcmd:@pemit me=[add(10,20)]"));

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService,
			MarkupText.Plain($"&DEBUGFORWARDLIST DbgFwdObj=#{forwardPlayer.DbRef.Number}"));

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MarkupText.Plain("@force DbgFwdObj=dbgfwdcmd"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(forwardPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					TestHelpers.MessagePlainTextContains(msg, "! [add(10,20)]")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(ownerPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					TestHelpers.MessagePlainTextContains(msg, "! [add(10,20)]")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy DbgFwdObj"));
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

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MarkupText.Plain("@create DbgMFwdObj"));
		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MarkupText.Plain("@set DbgMFwdObj=DEBUG"));
		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MarkupText.Plain("@set DbgMFwdObj=!no_command"));
		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService,
			MarkupText.Plain("&test_mfwd DbgMFwdObj=$dbgmfwdcmd:@pemit me=[mul(3,7)]"));

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService,
			MarkupText.Plain($"&DEBUGFORWARDLIST DbgMFwdObj=#{target1.DbRef.Number} #{target2.DbRef.Number}"));

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MarkupText.Plain("@force DbgMFwdObj=dbgmfwdcmd"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(target1.DbRef),
				Arg.Is<SharpMessage>(msg =>
					TestHelpers.MessagePlainTextContains(msg, "! [mul(3,7)]")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(target2.DbRef),
				Arg.Is<SharpMessage>(msg =>
					TestHelpers.MessagePlainTextContains(msg, "! [mul(3,7)]")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy DbgMFwdObj"));
	}

	[Test]
	public async Task DebugForwardList_NoAttribute_OnlySendsToOwner()
	{
		var ownerPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgNoFwdOwn");
		var otherPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgNoFwdOth");

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MarkupText.Plain("@create DbgNoFwdObj"));
		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MarkupText.Plain("@set DbgNoFwdObj=DEBUG"));
		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MarkupText.Plain("@set DbgNoFwdObj=!no_command"));
		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService,
			MarkupText.Plain("&test_nofwd DbgNoFwdObj=$dbgnofwdcmd:@pemit me=[sub(9,4)]"));

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MarkupText.Plain("@force DbgNoFwdObj=dbgnofwdcmd"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(ownerPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					TestHelpers.MessagePlainTextContains(msg, "! [sub(9,4)]")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(otherPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					TestHelpers.MessagePlainTextContains(msg, "! [sub(9,4)]")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy DbgNoFwdObj"));
	}

	[Test]
	public async Task DebugForwardList_InvalidTarget_DoesNotCrash()
	{
		var ownerPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgBadFwdOwn");

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MarkupText.Plain("@create DbgBadFwdObj"));
		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MarkupText.Plain("@set DbgBadFwdObj=DEBUG"));
		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MarkupText.Plain("@set DbgBadFwdObj=!no_command"));
		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService,
			MarkupText.Plain("&test_badfwd DbgBadFwdObj=$dbgbadfwdcmd:@pemit me=[add(5,5)]"));

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService,
			MarkupText.Plain("&DEBUGFORWARDLIST DbgBadFwdObj=#99999"));

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MarkupText.Plain("@force DbgBadFwdObj=dbgbadfwdcmd"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(ownerPlayer.DbRef),
				Arg.Is<SharpMessage>(msg =>
					TestHelpers.MessagePlainTextContains(msg, "! [add(5,5)]")), Arg.Is<AnySharpObject?>(sender => sender != null), INotifyService.NotificationType.Announce);

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy DbgBadFwdObj"));
	}

	private static string PlainText(SharpMessage msg) => msg switch
	{
		MString markup => markup.ToPlainText(),
		string text => text
	};
}
