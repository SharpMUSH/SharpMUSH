using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Commands;

public class UserDefinedCommandsTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.Services.GetRequiredService<IMUSHCodeParser>();

	/// <summary>A $-command matched from an action list is its own queue entry (#1132); wait for it to run.</summary>
	private ValueTask DrainQueue() => WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>().DrainImmediateQueueForTests();

	/// <summary>
	/// How many times God was told exactly <paramref name="message"/>, as <paramref name="type"/>, by
	/// <paramref name="sender"/> (by anyone when null).
	/// </summary>
	private int Heard(string message, DBRef? sender, INotifyService.NotificationType type) =>
		WebAppFactoryArg.Notifications.DeliveriesFor(WebAppFactoryArg.ExecutorDBRef).Count(delivery =>
			(sender is null || delivery.Sender == sender)
			&& delivery.Type == type
			&& delivery.Message == message);

	private async Task ExpectHeardOnce(string message, DBRef? sender, INotifyService.NotificationType type)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
		while (Heard(message, sender, type) == 0 && DateTime.UtcNow < deadline)
		{
			await Task.Delay(20);
		}

		await Assert.That(Heard(message, sender, type)).IsEqualTo(1);
	}

	private async Task ExpectNotHeard(string message, DBRef? sender, INotifyService.NotificationType type) =>
		await Assert.That(Heard(message, sender, type)).IsEqualTo(0);

	[Test]
	public async ValueTask WildcardEqSplitCommandPassesArgsToEmit()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcWildEq");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&UTEST_WILDEQ {obj}=${token} *=*:@emit {token} Boo! %0 - %1"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token} a=b"));

		await ExpectHeardOnce($"{token} Boo! a - b", obj, INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Single wildcard: $cmd * — %0 captures everything after the command name.
	/// </summary>
	[Test]
	public async ValueTask Wildcard_Single_SubstitutesArg()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcSingle");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&UTEST_SINGLE {obj}=${token} *:@emit {token} Hello, %0!"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token} World"));

		await ExpectHeardOnce($"{token} Hello, World!", obj, INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Two wildcards with a literal word between them: $cmd * to * — %0 and %1 are the two captures.
	/// </summary>
	[Test]
	public async ValueTask Wildcard_TwoCaptures_WithLiteralBetween_SubstitutesBothArgs()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcTwo");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&UTEST_TWO {obj}=${token} * to *:@emit {token}: Message from %0 to %1"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token} Alice to Bob"));

		await ExpectHeardOnce($"{token}: Message from Alice to Bob", obj, INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Exact match (no wildcards): $cmd — fires only on the precise command string; no substitution vars.
	/// </summary>
	[Test]
	public async ValueTask Wildcard_ExactMatch_NoWildcards_FiresCommand()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcExact");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&UTEST_EXACT {obj}=${token}:@emit {token} Pong!"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token}"));

		await ExpectHeardOnce($"{token} Pong!", obj, INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Three wildcards: $cmd * * * — %0, %1, %2 each capture one word.
	/// </summary>
	[Test]
	public async ValueTask Wildcard_ThreeCaptures_SubstitutesAllThreeArgs()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcThree");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&UTEST_THREE {obj}=${token} * * *:@emit {token}: A=%0 B=%1 C=%2"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token} foo bar baz"));

		await ExpectHeardOnce($"{token}: A=foo B=bar C=baz", obj, INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Regex single capture group: %0 is the full match, %1 is the first capture group.
	/// </summary>
	[Test]
	public async ValueTask Regex_SingleCaptureGroup_SubstitutesArg()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcRx1");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($@"&UTEST_RX1 {obj}=${token} (.+):@emit {token}: You said: %1"));
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set {obj}/UTEST_RX1=regexp"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token} hello world"));

		await ExpectHeardOnce($"{token}: You said: hello world", obj, INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Regex %0 is the full match string: $cmd prefix_([0-9]+) — %0 includes the command token, %1 is the number.
	/// Uses [0-9]+ instead of \d+ to avoid MUSH backslash escaping on attribute set.
	/// </summary>
	[Test]
	public async ValueTask Regex_PercentZeroIsFullMatch_PercentOneIsCaptureGroup()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcRx2");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&UTEST_RX2 {obj}=${token} prefix_([0-9]+):@emit Full: %0, Part: %1"));
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set {obj}/UTEST_RX2=regexp"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token} prefix_42"));

		// %0 is the full match which includes the command token: "{token} prefix_42"
		await ExpectHeardOnce($"Full: {token} prefix_42, Part: 42", obj, INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// %+ is the argument count, and a regexp $-command's named capture groups are not arguments.
	/// PennMUSH's pi_regs_get_envc (src/parse.c:1783-1813) takes the highest numeric index plus one
	/// and says so in the loop: "only check numeric args, ignore named ones". Named groups share the
	/// register namespace with %0-%N here (PatternArguments.cs:19-20), so %+ counted them too.
	/// </summary>
	[Test]
	public async ValueTask Regex_NamedCaptureGroups_AreNotCountedByPercentPlus()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcRxPlus");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&UTEST_RXPLUS {obj}=${token} (?<who>[A-Za-z]+) (?<what>[A-Za-z]+):@emit {token}: %+ %1 %2 [r(who,args)]"));
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set {obj}/UTEST_RXPLUS=regexp"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token} Alice Bob"));

		await ExpectHeardOnce($"{token}: 3 Alice Bob Alice", obj, INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Regex two capture groups: $cmd ([A-Za-z]+) ([A-Za-z]+) — %1 and %2 capture the two words.
	/// Uses [A-Za-z]+ instead of \w+ to avoid MUSH backslash escaping on attribute set.
	/// </summary>
	[Test]
	public async ValueTask Regex_TwoCaptureGroups_SubstitutesBothArgs()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcRx3");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&UTEST_RX3 {obj}=${token} ([A-Za-z]+) ([A-Za-z]+):@emit {token}: %1 messaged %2"));
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set {obj}/UTEST_RX3=regexp"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token} Alice Bob"));

		await ExpectHeardOnce($"{token}: Alice messaged Bob", obj, INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Regex named capture groups are accessible by their numeric index (%1, %2).
	/// Uses [0-9]+ instead of \d+ to avoid MUSH backslash escaping on attribute set.
	/// </summary>
	[Test]
	public async ValueTask Regex_NamedCaptureGroups_AccessibleByIndex()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcRx4");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&UTEST_RX4 {obj}=${token} (?<num>[0-9]+)d(?<sides>[0-9]+):@emit {token}: Rolling %1d%2"));
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set {obj}/UTEST_RX4=regexp"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token} 3d6"));

		await ExpectHeardOnce($"{token}: Rolling 3d6", obj, INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Per `help r` / `help regexp syntax6`, a regexp $-command's named captures are "named stack
	/// registers", read via r(&lt;name&gt;, args) — the `args` TYPE selector (r/&lt;name&gt; alone reads
	/// q-registers). This proves whether the args-type read of named captures works.
	/// </summary>
	[Test]
	public async ValueTask Regex_NamedCaptureGroups_AccessibleByName_ViaRArgs()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcRxName");
		var token = TestIsolationHelpers.GenerateUniqueName("ucn");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&UTEST_RXNAME {obj}=${token} (?<num>[0-9]+)d(?<sides>[0-9]+):@emit {token}: Rolling [r(num,args)]d[r(sides,args)]"));
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set {obj}/UTEST_RXNAME=regexp"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token} 3d6"));

		await ExpectHeardOnce($"{token}: Rolling 3d6", obj, INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// A regexp $-command carrying a non-capturing group. The <c>:</c> in <c>(?:…)</c> would end the
	/// pattern, so PennMUSH has it written <c>(?\:…)</c> and turns it back into <c>:</c> before
	/// compiling (<c>atr_single_match_r</c>, <c>src/attrib.c:1786-1798</c>). Without that step .NET is
	/// handed <c>(?\:</c>, <c>CommandAttributeScanner</c>'s catch swallows the ArgumentException, and
	/// the command silently ceases to exist — the symptom is "Huh?", never an error message.
	/// <para>
	/// A client-typed <c>&amp;</c> stores its value as written, so <c>\:</c> typed is <c>\:</c> stored.
	/// </para>
	/// </summary>
	[Test]
	public async ValueTask Regex_NonCapturingGroup_IsWrittenWithAnEscapedColon()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcRxNoCap");
		var token = TestIsolationHelpers.GenerateUniqueName("ucnc");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($@"&UTEST_RXNOCAP {obj}=${token} (?\:at|toward) ([A-Za-z]+):@emit {token}: %1"));
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set {obj}/UTEST_RXNOCAP=regexp"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token} toward door"));

		// %1 is "door", not "toward": the alternation is a group that does not capture.
		await ExpectHeardOnce($"{token}: door", obj, INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// The same escape in a wildcard pattern, where the failure is quieter still: an un-collapsed
	/// <c>\:</c> survives <c>Regex.Escape</c> and becomes a backslash the typed line has to contain.
	/// </summary>
	[Test]
	public async ValueTask Wildcard_EscapedColonIsALiteralColonInThePattern()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcColon");
		var token = TestIsolationHelpers.GenerateUniqueName("ucc");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($@"&UTEST_COLON {obj}=${token}\:go *:@emit {token}: %0"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token}:go north"));

		await ExpectHeardOnce($"{token}: north", obj, INotifyService.NotificationType.Emit);
	}

	[Test]
	[Category("TestInfrastructure")]
	[Skip("Test needs investigation - unrelated to communication commands")]
	public async Task SetAndResetCacheTest()
	{
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain("&cmd`setandresetcache #1=$test:@pemit #1=Value 1 received"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("test"));

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain("&cmd`setandresetcache #1=$test2:@pemit #1=Value 2 received"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("test2"));

		await ExpectHeardOnce("Value 1 received", WebAppFactoryArg.ExecutorDBRef, INotifyService.NotificationType.Announce);
		await ExpectHeardOnce("Value 2 received", WebAppFactoryArg.ExecutorDBRef, INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// Child inherits $commands from parent object.
	/// PennMUSH testatree.t: atree.command.1-4 / atree.sortorder.10
	/// </summary>
	[Test]
	public async ValueTask ParentInheritedCommand_Fires()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("pic");

		var parentObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"CmdParent_{token}");
		var childObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"CmdChild_{token}");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@parent {childObj}={parentObj}"));

		// Child does NOT have NO_COMMAND but parent DOES, so only the child fires the inherited command
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {childObj}=!no_command"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {parentObj}=no_command"));

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&CMD_{token} {parentObj}=${token}:@pemit %#=Inherited {token}"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(token));

		await ExpectHeardOnce($"Inherited {token}", childObj, INotifyService.NotificationType.PrivateEmit);
	}

	/// <summary>
	/// no_command on attribute blocks inherited command AND tree descendants.
	/// PennMUSH testatree.t: atree.command.16-17 / atree.sortorder.17-18
	/// </summary>
	[Test]
	public async ValueTask NoCommandOnAttribute_BlocksTreeDescendants()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("ncb");

		var parentObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"NcParent_{token}");
		var childObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"NcChild_{token}");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@parent {childObj}={parentObj}"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {childObj}=!no_command"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {parentObj}=no_command"));

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&CMD_{token}`LEAF {parentObj}=${token}leaf:@pemit %#=Leaf fired {token}"));

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&CMD_{token} {childObj}=$dummy:say dummy"));
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set {childObj}/CMD_{token}=no_command"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token}leaf"));

		await ExpectNotHeard($"Leaf fired {token}", null, INotifyService.NotificationType.PrivateEmit);
	}

	/// <summary>
	/// Child's local $cmd masks parent's $cmd`leaf (child override blocks parent tree branch).
	/// PennMUSH testatree.t: atree.command.13-14
	/// </summary>
	[Test]
	public async ValueTask ChildCommand_MasksParentTreeBranch()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("cmk");

		var parentObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"MskPar_{token}");
		var childObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"MskChi_{token}");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@parent {childObj}={parentObj}"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {childObj}=!no_command"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {parentObj}=no_command"));

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&CMD_{token} {parentObj}=${token}:@pemit %#=Parent {token}"));
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&CMD_{token}`LEAF {parentObj}=${token}leaf:@pemit %#=Parent leaf"));

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&CMD_{token} {childObj}=${token}:@pemit %#=Child {token}"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(token));
		await ExpectHeardOnce($"Child {token}", childObj, INotifyService.NotificationType.PrivateEmit);

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token}leaf"));
		await ExpectHeardOnce("Parent leaf", childObj, INotifyService.NotificationType.PrivateEmit);
	}

	/// <summary>
	/// no_inherit on parent attr causes fallthrough to grandparent command.
	/// PennMUSH testatree.t: atree.command.27-29
	/// </summary>
	[Test]
	public async ValueTask NoInherit_FallsThrough_ToGrandparent()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("nif");

		var grandObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"NiGrand_{token}");
		var parentObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"NiPar_{token}");
		var childObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"NiChi_{token}");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@parent {childObj}={parentObj}"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@parent {parentObj}={grandObj}"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {childObj}=!no_command"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {parentObj}=no_command"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {grandObj}=no_command"));

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&CMD_{token}`LEAF {grandObj}=${token}leaf:@pemit %#=Grand leaf"));

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&CMD_{token} {parentObj}=${token}:@pemit %#=Parent root"));
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&CMD_{token}`LEAF {parentObj}=${token}leaf:@pemit %#=Parent leaf"));
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set {parentObj}/CMD_{token}=no_inherit"));

		// parent's CMD_token has no_inherit so entire branch skipped, falls to grand
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token}leaf"));

		await ExpectHeardOnce("Grand leaf", childObj, INotifyService.NotificationType.PrivateEmit);
	}

	/// <summary>
	/// no_command on parent's tree root blocks leaf inheritance even when child has no local override.
	/// PennMUSH testatree.t: atree.command.19-21
	/// </summary>
	[Test]
	public async ValueTask ParentNoCommand_BlocksLeafInheritance()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("pnc");

		var parentObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"PncPar_{token}");
		var childObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"PncChi_{token}");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@parent {childObj}={parentObj}"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {childObj}=!no_command"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {parentObj}=no_command"));

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&CMD_{token} {parentObj}=${token}:@pemit %#=Root {token}"));
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&CMD_{token}`LEAF {parentObj}=${token}leaf:@pemit %#=Leaf {token}"));

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set {parentObj}/CMD_{token}=no_command"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token}leaf"));

		await ExpectNotHeard($"Leaf {token}", null, INotifyService.NotificationType.PrivateEmit);
	}

	// Reported bug: a $command like `$test:@emit ...` does NOT match when the
	// player types " test" (a leading space before the command). PennMUSH strips
	// leading whitespace from a command before matching, so this SHOULD fire.

	/// <summary>
	/// Control: an exact-match $command with NO leading space fires (baseline for the leading-space tests).
	/// </summary>
	[Test]
	public async ValueTask NoLeadingSpace_TerminalEntry_Matches()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcNoLeadSpace");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&UTEST_NOLEAD {obj}=${token}:@emit {token} Matched"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token}"));

		await ExpectHeardOnce($"{token} Matched", obj, INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Terminal entry (direct player input): typing " {token}" (leading space) should still match the $command.
	/// This is the exact scenario from the bug report.
	/// </summary>
	[Test]
	public async ValueTask LeadingSpace_TerminalEntry_StillMatches()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcLeadSpaceTerm");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&UTEST_LEAD_TERM {obj}=${token}:@emit {token} Matched"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($" {token}"));

		await ExpectHeardOnce($"{token} Matched", obj, INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Command-list path (queued/callback, e.g. softcode action lists): a single command with a
	/// leading space run via CommandListParse should still match the $command.
	/// </summary>
	[Test]
	public async ValueTask LeadingSpace_CommandList_StillMatches()
	{
		var listParser = WebAppFactoryArg.CommandParser;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcLeadSpaceList");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&UTEST_LEAD_LIST {obj}=${token}:@emit {token} Matched"));

		await listParser.CommandListParse(MarkupText.Plain($" {token}"));
		await DrainQueue();

		await ExpectHeardOnce($"{token} Matched", obj, INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Command-list path with a space after a ';' separator ("@@ comment;  {token}"). The $command is the
	/// second command in the list; it must match its own per-command slice, not the whole list source.
	/// </summary>
	[Test]
	public async ValueTask LeadingSpace_AfterSemicolonInCommandList_StillMatches()
	{
		var listParser = WebAppFactoryArg.CommandParser;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcLeadSpaceSemi");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&UTEST_LEAD_SEMI {obj}=${token}:@emit {token} Matched"));

		await listParser.CommandListParse(MarkupText.Plain($"@@ ignore;  {token}"));
		await DrainQueue();

		await ExpectHeardOnce($"{token} Matched", obj, INotifyService.NotificationType.Emit);
	}

	// A $command that is part of a multi-command ';' list must match against its own per-command
	// slice (EvaluateCommands computes `commandText` via the command's evaluationString span), NOT
	// the whole list source. Otherwise its ^...$ pattern would be tested against "alpha;beta" and
	// never match. Built-in commands already slice this way (ArgumentSplit's realSubtext).

	/// <summary>
	/// A $command that is the SECOND command in a list, with NO leading space ("@@ ignore;{token}").
	/// </summary>
	[Test]
	public async ValueTask SemicolonList_SecondCommand_NoLeadingSpace_Match()
	{
		var listParser = WebAppFactoryArg.CommandParser;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcSemiNoSpace");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&UTEST_SEMI_NOSPACE {obj}=${token}:@emit {token} Matched"));

		await listParser.CommandListParse(MarkupText.Plain($"@@ ignore;{token}"));
		await DrainQueue();

		await ExpectHeardOnce($"{token} Matched", obj, INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Terminal entry: a trailing space after the command (" {token} ") should still match an exact $command.
	/// The wildcard patterns are anchored at the end (^...$), so a trailing space breaks an exact match.
	/// </summary>
	[Test]
	public async ValueTask TrailingSpace_TerminalEntry_StillMatches()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcTrailSpaceTerm");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&UTEST_TRAIL_TERM {obj}=${token}:@emit {token} Matched"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token} "));

		await ExpectHeardOnce($"{token} Matched", obj, INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Command-list path: a trailing space after the command run via CommandListParse should still match.
	/// </summary>
	[Test]
	public async ValueTask TrailingSpace_CommandList_StillMatches()
	{
		var listParser = WebAppFactoryArg.CommandParser;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcTrailSpaceList");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&UTEST_TRAIL_LIST {obj}=${token}:@emit {token} Matched"));

		await listParser.CommandListParse(MarkupText.Plain($"{token} "));
		await DrainQueue();

		await ExpectHeardOnce($"{token} Matched", obj, INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// A $command that is the FIRST command in a list with a trailing built-in ("{token};@emit X").
	/// It must match its own slice, not "token;@emit X".
	/// </summary>
	[Test]
	public async ValueTask SemicolonList_FirstCommand_WithTail_Match()
	{
		var listParser = WebAppFactoryArg.CommandParser;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcSemiFirst");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&UTEST_SEMI_FIRST {obj}=${token}:@emit {token} Matched"));

		await listParser.CommandListParse(MarkupText.Plain($"{token};@emit TAIL"));
		await DrainQueue();

		await ExpectHeardOnce($"{token} Matched", obj, INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Three-command list: a $command in the MIDDLE and at the END both match. Guards the
	/// Stop.StopIndex arithmetic for the final list element.
	/// </summary>
	[Test]
	public async ValueTask SemicolonList_MiddleAndLastCommands_Match()
	{
		var listParser = WebAppFactoryArg.CommandParser;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcSemiThree");
		var mid = TestIsolationHelpers.GenerateUniqueName("ucmid");
		var last = TestIsolationHelpers.GenerateUniqueName("uclast");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&UTEST_SEMI_MID {obj}=${mid}:@emit {mid} MidMatched"));
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&UTEST_SEMI_LAST {obj}=${last}:@emit {last} LastMatched"));

		await listParser.CommandListParse(MarkupText.Plain($"@emit HEAD;{mid};{last}"));
		await DrainQueue();

		await ExpectHeardOnce($"{mid} MidMatched", obj, INotifyService.NotificationType.Emit);
		await ExpectHeardOnce($"{last} LastMatched", obj, INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Per-command argument capture in a list (the "funky indexes" guard). A wildcard $command as the
	/// SECOND command must capture %0 from its OWN slice — "Bob" — not leak the first command's text or a
	/// wrong offset. The first command is deliberately a different length than the second.
	/// </summary>
	[Test]
	public async ValueTask SemicolonList_WildcardArg_CapturesPerCommandSlice()
	{
		var listParser = WebAppFactoryArg.CommandParser;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcSemiArg");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&UTEST_SEMI_ARG {obj}=${token} *:@emit GREET=<%0>"));

		await listParser.CommandListParse(MarkupText.Plain($"@emit AAAAAAAAAA;{token} Bob"));
		await DrainQueue();

		await ExpectHeardOnce("GREET=<Bob>", obj, INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Two-wildcard $command as the second command in a list: %0 and %1 each capture from the
	/// per-command slice. Further guards capture-group index alignment after slicing.
	/// </summary>
	[Test]
	public async ValueTask SemicolonList_TwoWildcardArgs_CapturePerCommandSlice()
	{
		var listParser = WebAppFactoryArg.CommandParser;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcSemiArg2");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&UTEST_SEMI_ARG2 {obj}=${token} * to *:@emit MSG=<%0>-<%1>"));

		await listParser.CommandListParse(MarkupText.Plain($"@emit IGNORE;{token} Alice to Bob"));
		await DrainQueue();

		await ExpectHeardOnce("MSG=<Alice>-<Bob>", obj, INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// A HALTED object runs none of its softcode, so its $-commands do not fire — the same
	/// PennMUSH PE_NOTHING rule enforced for u()/ufun. This is what makes @chown's loop-break
	/// (which halts the object) actually stop a runaway $-command. The command fires once before
	/// the flag is set; after halting, triggering it again must leave the count at one.
	/// </summary>
	[Test]
	public async ValueTask HaltedObjectDollarCommandDoesNotFire()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcHalt");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&UTEST_HALT {obj}=${token}:@emit {token} fired"));

		// Not halted: the $-command fires.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token}"));
		await ExpectHeardOnce($"{token} fired", obj, INotifyService.NotificationType.Emit);

		// Halt the object, then trigger again: the emit count must stay at exactly one.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {obj}=HALT"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token}"));
		await ExpectHeardOnce($"{token} fired", obj, INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// PennMUSH matches $-commands against the command line after it is evaluated (game.c tests the
	/// evaluated cptr), so a command whose name only appears once substitutions and functions run
	/// still triggers. Here the typed line [strcat(&lt;token&gt;)] is not the command name literally,
	/// but evaluates to it. Matched raw (the previous behavior) it would fall through to "Huh?";
	/// matched on the evaluated line it fires.
	/// </summary>
	[Test]
	public async ValueTask DollarCommandMatchesAgainstTheEvaluatedLine()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "DollarEval");
		var token = TestIsolationHelpers.GenerateUniqueName("de");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&UTEST_EVAL {obj}=${token}:@emit {token} evaluated"));

		// The command name only exists after the strcat evaluates.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"[strcat({token})]"));

		await ExpectHeardOnce($"{token} evaluated", obj, INotifyService.NotificationType.Emit);
	}
}
