using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.Commands;

public class DebugVerboseTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	/// <summary>What <paramref name="who"/> was told matching <paramref name="pattern"/>, by an object or by nobody.</summary>
	private List<string> Heard(DBRef who, string pattern, bool fromObject = true) =>
	[
		.. WebAppFactoryArg.Notifications.DeliveriesFor(who)
			.Where(delivery => delivery.Type == INotifyService.NotificationType.Announce
				&& delivery.Sender is not null == fromObject
				&& Regex.IsMatch(delivery.Message, pattern))
			.Select(delivery => delivery.Message)
	];

	/// <summary>
	/// A $-command reached through @force is its own queue entry (#1132), and @trigger queues too, so
	/// this waits until every line has arrived before asking how often each did.
	/// </summary>
	private async Task ExpectHeardOnce(DBRef who, params string[] patterns) =>
		await ExpectHeard(who, patterns, times: 1);

	private async Task ExpectHeard(DBRef who, string pattern, bool fromObject = true) =>
		await ExpectHeard(who, [pattern], times: null, fromObject);

	private async Task ExpectHeard(DBRef who, string[] patterns, int? times, bool fromObject = true)
	{
		await Assert.That(() => patterns.All(pattern => Heard(who, pattern, fromObject).Count > 0))
			.WaitsFor(all => all.IsTrue(), timeout: TimeSpan.FromSeconds(10), pollingInterval: TimeSpan.FromMilliseconds(20));
		if (times is not { } expected) return;
		foreach (var pattern in patterns)
		{
			await Assert.That(Heard(who, pattern, fromObject)).Count().IsEqualTo(expected).Because(pattern);
		}
	}

	private async Task ExpectNotHeard(DBRef who, string pattern) =>
		await Assert.That(Heard(who, pattern)).IsEmpty();

	/// <summary>Waits for a @pemit the test's own code sends once the behaviour under test has run.</summary>
	private async Task WaitForPemit(DBRef who, string text) =>
		await WebAppFactoryArg.Notifications.WaitForAsync(who, text, TimeSpan.FromSeconds(10));

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
		await ExpectHeardOnce(testPlayer.DbRef,
			@"^#\d+! +\[add\(123,456\)\] :$",
			@"^#\d+! +\[add\(123,456\)\] => 579$");

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy DebugEvalObj"));
	}

	/// <summary>
	/// The executor's DEBUG flag is kept between function calls (<c>ExecutorDebugFlags</c>); setting or
	/// clearing it has to show on the very next call.
	/// </summary>
	[Test]
	public async Task DebugFlag_SetAndCleared_TakesEffectOnTheNextCall()
	{
		// The player is the executor, so each think runs inline, in order, with no queue between them.
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgToggle");

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("think [add(9101,1)]"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set me=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("think [add(9102,1)]"));
		await ExpectHeardOnce(testPlayer.DbRef, @"^#\d+! +\[add\(9102,1\)\] :$");

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set me=!DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("think [add(9103,1)]"));
		await WebAppFactoryArg.Notifications.WaitForAsync(testPlayer.DbRef, "9104", TimeSpan.FromSeconds(10));
		await ExpectNotHeard(testPlayer.DbRef, Regex.Escape("add(9101,1)"));
		await ExpectNotHeard(testPlayer.DbRef, Regex.Escape("add(9103,1)"));
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
		// Inner function (has extra space for nesting indentation, matching PennMUSH)
		await ExpectHeardOnce(testPlayer.DbRef,
			@"^#\d+! +\[mul\(add\(11,22\),3\)\] :$",
			@"^#\d+! {2,}add\(11,22\) :$",
			@"^#\d+! +\[mul\(add\(11,22\),3\)\] => 99$");

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

		await ExpectHeardOnce(testPlayer.DbRef, @"^#\d+\] @pemit me=UniqueTestMessage789$");

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

		await ExpectHeardOnce(testPlayer.DbRef, @"^#\d+\] think UniqueNoDup777$");

		await ExpectNotHeard(testPlayer.DbRef, Regex.Escape("] think think"));

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

		await ExpectHeardOnce(testPlayer.DbRef, @"^#\d+\] @emit/noeval UniqueNoDupSwitch555$");

		await ExpectNotHeard(testPlayer.DbRef, Regex.Escape("@emit/noeval @emit/noeval"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy VerboseNoDupSwitchObj"));
	}

	[Test]
	public async Task AttributeDebugFlag_Diagnostic_FlagsLoadedAfterSet()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DiagDbg");
		// @CREATE returns the new object's dbref as its call state. Mining it out of the notification
		// instead meant reading the session-shared substitute's call list, and tied this diagnostic to
		// the wording of "Created: Object #N." — which is do_create's, and not this test's business.
		var created = await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MarkupText.Plain("@create DiagDebugThing"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("&DIAGFUNC_UNIQ2 DiagDebugThing=[add(1,2)]"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DiagDebugThing/DIAGFUNC_UNIQ2=DEBUG"));

		var dbref = DBRef.Parse(created.Message.ToPlainText());

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

		await ExpectHeardOnce(testPlayer.DbRef, @"^#\d+! +\[add\(88,77\)\] => 165$");

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
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("&TESTFUNC2_NODEBG_UNIQUE AttrNoDebugSuppressTest=@emit [add(55,44)];@pemit %#=NoDebugDone"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set AttrNoDebugSuppressTest/TESTFUNC2_NODEBG_UNIQUE=no_debug"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@trigger AttrNoDebugSuppressTest/TESTFUNC2_NODEBG_UNIQUE"));

		// NODEBUG takes precedence over object DEBUG
		await WaitForPemit(testPlayer.DbRef, "NoDebugDone");
		await ExpectNotHeard(testPlayer.DbRef, @"^#\d+! +\[?add\(55,44\)");

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
		await ExpectHeard(testPlayer.DbRef, @"^#\d+! +\[strlen\(%qa\)\] => 5$");

		await ExpectNotHeard(testPlayer.DbRef, Regex.Escape("[Q-Registers:"));
		await ExpectNotHeard(testPlayer.DbRef, Regex.Escape("[Registers:"));
		await ExpectNotHeard(testPlayer.DbRef, Regex.Escape("[Iter-Registers:"));

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
		await ExpectHeardOnce(testPlayer.DbRef, @"^#\d+! +\[add\(7,8\)\] :$");

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
		await ExpectHeardOnce(testPlayer.DbRef, @"^#\d+! +\[add\(7,8\)\] => 15$");

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
		await ExpectHeardOnce(testPlayer.DbRef,
			@"^#\d+! +\[strlen\(add\(2,3\)\)\] :$",
			@"^#\d+! {2,}add\(2,3\) :$",
			@"^#\d+! {2,}add\(2,3\) => 5$",
			@"^#\d+! +\[strlen\(add\(2,3\)\)\] => 1$");

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

		await ExpectHeardOnce(testPlayer.DbRef, @"^#\d+\] @pemit me=VerbFmtTest444$");

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy VerboseFmtObj"));
	}

	[Test]
	public async Task PuppetFlag_CannotBeSetOnPlayer()
	{
		// Pattern C: unique receiver isolates the generic "Permission denied." message in the shared session.
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PuppetNoSet");

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set me=PUPPET"));

		await ExpectHeard(testPlayer.DbRef, ["^Permission denied\\.$"], times: 1, fromObject: false);
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

		await ExpectHeard(testPlayer.DbRef, [$"^{Regex.Escape(uniqueName)} - PUPPET set\\.$"], times: 1, fromObject: false);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain($"@destroy {uniqueName}"));
	}

	[Test]
	public async Task Debug_SendsToOwner_NotToExecutor()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DbgOwner");
		var created = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@create DebugOwnerObj"));
		var executor = (await Mediator.Send(new GetObjectNodeQuery(DBRef.Parse(created.Message.ToPlainText()))))
			.Expect<AnySharpObject>().Object().DBRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DebugOwnerObj=DEBUG"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@set DebugOwnerObj=!no_command"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("&dbg_owner DebugOwnerObj=$dbgownercmd:@pemit me=[add(1,1)]"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@force DebugOwnerObj=dbgownercmd"));

		// The object's own @pemit is the last thing the command does, so by then all its debug output is out.
		await ExpectHeard(testPlayer.DbRef, Regex.Escape("! [add(1,1)]"));
		await WaitForPemit(executor, "2");
		await Assert.That(Heard(executor, Regex.Escape("! [add(1,1)]"))).IsEmpty()
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
		await ExpectHeardOnce(testPlayer.DbRef,
			@"^#\d+! +\[strlen\(%qa\)\] :$",
			@"^#\d+! +\[strlen\(%qa\)\] => \d+$");

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
		await ExpectHeardOnce(testPlayer.DbRef,
			@"^#\d+! +\[strlen\(%0\)\] :$",
			@"^#\d+! +\[strlen\(%0\)\] => 5$");

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
		await ExpectHeardOnce(testPlayer.DbRef,
			@"^#\d+! +\[iter\(Hello,strlen\(##\)\)\] :$",
			@"^#\d+! +strlen\(%iL\) :$",
			@"^#\d+! +strlen\(%iL\) => 5$");

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
		await ExpectHeardOnce(testPlayer.DbRef,
			@"^#\d+! +\[setq\(a,TestVal123\)\] :$",
			@"^#\d+! +\[setq\(a,TestVal123\)\] => $");

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

		await ExpectHeardOnce(testPlayer.DbRef, @"^#\d+\] think 30$");

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
		await ExpectHeardOnce(testPlayer.DbRef, @"^#\d+! %# and %# => #\d+ and #\d+$");

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

		// Can_Forward(thing, fwd) is checked at set time (#1218) and its subject is the OBJECT, not the
		// player setting the list; an object never controls a player (src/predicat.c:405), so the
		// forward target has to have set a forward lock the object passes.
		var fwdObj = (await Parser.CommandParse(ownerPlayer.Handle, ConnectionService,
			MarkupText.Plain("think [num(DbgFwdObj)]")))?.Message.ToPlainText();
		await Parser.CommandParse(forwardPlayer.Handle, ConnectionService,
			MarkupText.Plain($"@lock/forward me={fwdObj}"));

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService,
			MarkupText.Plain($"&DEBUGFORWARDLIST DbgFwdObj=#{forwardPlayer.DbRef.Number}"));

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MarkupText.Plain("@force DbgFwdObj=dbgfwdcmd"));
		await ExpectHeard(forwardPlayer.DbRef, Regex.Escape("! [add(10,20)]"));

		await ExpectHeard(ownerPlayer.DbRef, Regex.Escape("! [add(10,20)]"));

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

		// Both targets must allow the object through a forward lock - see the single-target test.
		var mfwdObj = (await Parser.CommandParse(ownerPlayer.Handle, ConnectionService,
			MarkupText.Plain("think [num(DbgMFwdObj)]")))?.Message.ToPlainText();
		await Parser.CommandParse(target1.Handle, ConnectionService,
			MarkupText.Plain($"@lock/forward me={mfwdObj}"));
		await Parser.CommandParse(target2.Handle, ConnectionService,
			MarkupText.Plain($"@lock/forward me={mfwdObj}"));

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService,
			MarkupText.Plain($"&DEBUGFORWARDLIST DbgMFwdObj=#{target1.DbRef.Number} #{target2.DbRef.Number}"));

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MarkupText.Plain("@force DbgMFwdObj=dbgmfwdcmd"));
		await ExpectHeard(target1.DbRef, Regex.Escape("! [mul(3,7)]"));

		await ExpectHeard(target2.DbRef, Regex.Escape("! [mul(3,7)]"));

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
		await ExpectHeard(ownerPlayer.DbRef, Regex.Escape("! [sub(9,4)]"));

		await ExpectNotHeard(otherPlayer.DbRef, Regex.Escape("! [sub(9,4)]"));

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

		// #99999 names no object, so since #1218 the list is refused at set time rather than being
		// stored and skipped at forward time. Either way the object's own debug output still reaches
		// its owner, which is what this test is about.
		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService,
			MarkupText.Plain("&DEBUGFORWARDLIST DbgBadFwdObj=#99999"));

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MarkupText.Plain("@force DbgBadFwdObj=dbgbadfwdcmd"));
		await ExpectHeard(ownerPlayer.DbRef, Regex.Escape("! [add(5,5)]"));

		await Parser.CommandParse(ownerPlayer.Handle, ConnectionService, MarkupText.Plain("@destroy DbgBadFwdObj"));
	}

	private static string PlainText(SharpMessage msg) => msg switch
	{
		MString markup => markup.ToPlainText(),
		string text => text
	};
}
