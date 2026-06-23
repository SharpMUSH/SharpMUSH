using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class UserDefinedCommandsTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.Services.GetRequiredService<IMUSHCodeParser>();

	[Test]
	public async ValueTask WildcardEqSplitCommandPassesArgsToEmit()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcWildEq");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		// Set a $ command attribute with an EqSplit wildcard pattern on a unique object in room #0
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&UTEST_WILDEQ {obj}=${token} *=*:@emit {token} Boo! %0 - %1"));

		// Fire the command — %0 should be "a", %1 should be "b"
		await Parser.CommandParse(1, ConnectionService, MModule.single($"{token} a=b"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"{token} Boo! a - b")),
				TestHelpers.MatchingObject(obj), INotifyService.NotificationType.Emit);
	}

	// ── Wildcard pattern tests ──────────────────────────────────────────────

	/// <summary>
	/// Single wildcard: $cmd * — %0 captures everything after the command name.
	/// </summary>
	[Test]
	public async ValueTask Wildcard_Single_SubstitutesArg()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcSingle");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&UTEST_SINGLE {obj}=${token} *:@emit {token} Hello, %0!"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"{token} World"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"{token} Hello, World!")),
				TestHelpers.MatchingObject(obj), INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Two wildcards with a literal word between them: $cmd * to * — %0 and %1 are the two captures.
	/// </summary>
	[Test]
	public async ValueTask Wildcard_TwoCaptures_WithLiteralBetween_SubstitutesBothArgs()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcTwo");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&UTEST_TWO {obj}=${token} * to *:@emit {token}: Message from %0 to %1"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"{token} Alice to Bob"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"{token}: Message from Alice to Bob")),
				TestHelpers.MatchingObject(obj), INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Exact match (no wildcards): $cmd — fires only on the precise command string; no substitution vars.
	/// </summary>
	[Test]
	public async ValueTask Wildcard_ExactMatch_NoWildcards_FiresCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcExact");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&UTEST_EXACT {obj}=${token}:@emit {token} Pong!"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"{token}"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"{token} Pong!")),
				TestHelpers.MatchingObject(obj), INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Three wildcards: $cmd * * * — %0, %1, %2 each capture one word.
	/// </summary>
	[Test]
	public async ValueTask Wildcard_ThreeCaptures_SubstitutesAllThreeArgs()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcThree");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&UTEST_THREE {obj}=${token} * * *:@emit {token}: A=%0 B=%1 C=%2"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"{token} foo bar baz"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"{token}: A=foo B=bar C=baz")),
				TestHelpers.MatchingObject(obj), INotifyService.NotificationType.Emit);
	}

	// ── Regex pattern tests ──────────────────────────────────────────────────

	/// <summary>
	/// Regex single capture group: %0 is the full match, %1 is the first capture group.
	/// </summary>
	[Test]
	public async ValueTask Regex_SingleCaptureGroup_SubstitutesArg()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcRx1");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($@"&UTEST_RX1 {obj}=${token} (.+):@emit {token}: You said: %1"));
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@set {obj}/UTEST_RX1=regexp"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"{token} hello world"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"{token}: You said: hello world")),
				TestHelpers.MatchingObject(obj), INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Regex %0 is the full match string: $cmd prefix_([0-9]+) — %0 includes the command token, %1 is the number.
	/// Uses [0-9]+ instead of \d+ to avoid MUSH backslash escaping on attribute set.
	/// </summary>
	[Test]
	public async ValueTask Regex_PercentZeroIsFullMatch_PercentOneIsCaptureGroup()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcRx2");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&UTEST_RX2 {obj}=${token} prefix_([0-9]+):@emit Full: %0, Part: %1"));
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@set {obj}/UTEST_RX2=regexp"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"{token} prefix_42"));

		// %0 is the full match which includes the command token: "{token} prefix_42"
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"Full: {token} prefix_42, Part: 42")),
				TestHelpers.MatchingObject(obj), INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Regex two capture groups: $cmd ([A-Za-z]+) ([A-Za-z]+) — %1 and %2 capture the two words.
	/// Uses [A-Za-z]+ instead of \w+ to avoid MUSH backslash escaping on attribute set.
	/// </summary>
	[Test]
	public async ValueTask Regex_TwoCaptureGroups_SubstitutesBothArgs()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcRx3");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&UTEST_RX3 {obj}=${token} ([A-Za-z]+) ([A-Za-z]+):@emit {token}: %1 messaged %2"));
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@set {obj}/UTEST_RX3=regexp"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"{token} Alice Bob"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"{token}: Alice messaged Bob")),
				TestHelpers.MatchingObject(obj), INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Regex named capture groups are accessible by their numeric index (%1, %2).
	/// Uses [0-9]+ instead of \d+ to avoid MUSH backslash escaping on attribute set.
	/// </summary>
	[Test]
	public async ValueTask Regex_NamedCaptureGroups_AccessibleByIndex()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcRx4");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&UTEST_RX4 {obj}=${token} (?<num>[0-9]+)d(?<sides>[0-9]+):@emit {token}: Rolling %1d%2"));
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@set {obj}/UTEST_RX4=regexp"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"{token} 3d6"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"{token}: Rolling 3d6")),
				TestHelpers.MatchingObject(obj), INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Per `help r` / `help regexp syntax6`, a regexp $-command's named captures are "named stack
	/// registers", read via r(&lt;name&gt;, args) — the `args` TYPE selector (r/&lt;name&gt; alone reads
	/// q-registers). This proves whether the args-type read of named captures works.
	/// </summary>
	[Test]
	public async ValueTask Regex_NamedCaptureGroups_AccessibleByName_ViaRArgs()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcRxName");
		var token = TestIsolationHelpers.GenerateUniqueName("ucn");
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&UTEST_RXNAME {obj}=${token} (?<num>[0-9]+)d(?<sides>[0-9]+):@emit {token}: Rolling [r(num,args)]d[r(sides,args)]"));
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@set {obj}/UTEST_RXNAME=regexp"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"{token} 3d6"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"{token}: Rolling 3d6")),
				TestHelpers.MatchingObject(obj), INotifyService.NotificationType.Emit);
	}

	[Test]
	[Category("TestInfrastructure")]
	[Skip("Test needs investigation - unrelated to communication commands")]
	public async Task SetAndResetCacheTest()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("&cmd`setandresetcache #1=$test:@pemit #1=Value 1 received"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("test"));

		await Parser.CommandParse(1, ConnectionService,
			MModule.single("&cmd`setandresetcache #1=$test2:@pemit #1=Value 2 received"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("test2"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, "Value 1 received")),
				TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, "Value 2 received")),
				TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// Child inherits $commands from parent object.
	/// PennMUSH testatree.t: atree.command.1-4 / atree.sortorder.10
	/// </summary>
	[Test]
	public async ValueTask ParentInheritedCommand_Fires()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var token = TestIsolationHelpers.GenerateUniqueName("pic");

		// Create parent and child objects
		var parentObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"CmdParent_{token}");
		var childObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"CmdChild_{token}");

		// Set parent relationship
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {childObj}={parentObj}"));

		// Ensure child does NOT have NO_COMMAND object flag, but parent DOES
		// (so only the child fires the inherited command, not the parent directly)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {childObj}=!no_command"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {parentObj}=no_command"));

		// Set $command on the parent
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&CMD_{token} {parentObj}=${token}:@pemit %#=Inherited {token}"));

		// Fire the command — child should inherit it
		await Parser.CommandParse(1, ConnectionService, MModule.single(token));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"Inherited {token}")),
				TestHelpers.MatchingObject(childObj), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// no_command on attribute blocks inherited command AND tree descendants.
	/// PennMUSH testatree.t: atree.command.16-17 / atree.sortorder.17-18
	/// </summary>
	[Test]
	public async ValueTask NoCommandOnAttribute_BlocksTreeDescendants()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var token = TestIsolationHelpers.GenerateUniqueName("ncb");

		var parentObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"NcParent_{token}");
		var childObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"NcChild_{token}");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {childObj}={parentObj}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {childObj}=!no_command"));

		// Set tree command on parent: CMD`LEAF
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&CMD_{token}`LEAF {parentObj}=${token}leaf:@pemit %#=Leaf fired"));

		// Set no_command on the tree root on child (blocks whole tree)
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&CMD_{token} {childObj}=$dummy:say dummy"));
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@set {childObj}/CMD_{token}=no_command"));

		// The tree leaf command should be blocked
		await Parser.CommandParse(1, ConnectionService, MModule.single($"{token}leaf"));

		// Should NOT have received the leaf command notification
		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, "Leaf fired")),
				TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// Child's local $cmd masks parent's $cmd`leaf (child override blocks parent tree branch).
	/// PennMUSH testatree.t: atree.command.13-14
	/// </summary>
	[Test]
	public async ValueTask ChildCommand_MasksParentTreeBranch()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var token = TestIsolationHelpers.GenerateUniqueName("cmk");

		var parentObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"MskPar_{token}");
		var childObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"MskChi_{token}");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {childObj}={parentObj}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {childObj}=!no_command"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {parentObj}=no_command"));

		// Parent has tree command
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&CMD_{token} {parentObj}=${token}:@pemit %#=Parent {token}"));
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&CMD_{token}`LEAF {parentObj}=${token}leaf:@pemit %#=Parent leaf"));

		// Child overrides the root — should mask parent's tree descendants
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&CMD_{token} {childObj}=${token}:@pemit %#=Child {token}"));

		// Fire root command — should get child's version
		await Parser.CommandParse(1, ConnectionService, MModule.single(token));
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"Child {token}")),
				TestHelpers.MatchingObject(childObj), INotifyService.NotificationType.Announce);

		// Fire leaf command — should still work (parent's leaf not masked, child only overrides root name)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"{token}leaf"));
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, "Parent leaf")),
				TestHelpers.MatchingObject(childObj), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// no_inherit on parent attr causes fallthrough to grandparent command.
	/// PennMUSH testatree.t: atree.command.27-29
	/// </summary>
	[Test]
	public async ValueTask NoInherit_FallsThrough_ToGrandparent()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var token = TestIsolationHelpers.GenerateUniqueName("nif");

		var grandObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"NiGrand_{token}");
		var parentObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"NiPar_{token}");
		var childObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"NiChi_{token}");

		// Set up chain: child -> parent -> grand
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {childObj}={parentObj}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {parentObj}={grandObj}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {childObj}=!no_command"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {parentObj}=no_command"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {grandObj}=no_command"));

		// Grand has bar`baz command
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&CMD_{token}`LEAF {grandObj}=${token}leaf:@pemit %#=Grand leaf"));

		// Parent has bar and bar`baz, but bar is no_inherit
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&CMD_{token} {parentObj}=${token}:@pemit %#=Parent root"));
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&CMD_{token}`LEAF {parentObj}=${token}leaf:@pemit %#=Parent leaf"));
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@set {parentObj}/CMD_{token}=no_inherit"));

		// Fire leaf — parent's CMD_token has no_inherit so entire branch skipped, falls to grand
		await Parser.CommandParse(1, ConnectionService, MModule.single($"{token}leaf"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, "Grand leaf")),
				TestHelpers.MatchingObject(childObj), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// no_command on parent's tree root blocks leaf inheritance even when child has no local override.
	/// PennMUSH testatree.t: atree.command.19-21
	/// </summary>
	[Test]
	public async ValueTask ParentNoCommand_BlocksLeafInheritance()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var token = TestIsolationHelpers.GenerateUniqueName("pnc");

		var parentObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"PncPar_{token}");
		var childObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"PncChi_{token}");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {childObj}={parentObj}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {childObj}=!no_command"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {parentObj}=no_command"));

		// Parent has tree commands
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&CMD_{token} {parentObj}=${token}:@pemit %#=Root {token}"));
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&CMD_{token}`LEAF {parentObj}=${token}leaf:@pemit %#=Leaf {token}"));

		// Set no_command on parent's root attr
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@set {parentObj}/CMD_{token}=no_command"));

		// Fire leaf — should get "Huh?" (no match), not "Leaf {token}"
		await Parser.CommandParse(1, ConnectionService, MModule.single($"{token}leaf"));

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"Leaf {token}")),
				Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	// ── Leading-space $command matching (bug repro) ───────────────────────────
	// Reported bug: a $command like `$test:@emit ...` does NOT match when the
	// player types " test" (a leading space before the command). PennMUSH strips
	// leading whitespace from a command before matching, so this SHOULD fire.
	// These tests assert the expected (PennMUSH-compatible) behavior; if the bug
	// is present they fail with a "Huh?" (zero notifications received).

	/// <summary>
	/// Control: an exact-match $command with NO leading space fires (baseline for the leading-space tests).
	/// </summary>
	[Test]
	public async ValueTask NoLeadingSpace_TerminalEntry_Matches()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcNoLeadSpace");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&UTEST_NOLEAD {obj}=${token}:@emit {token} Matched"));

		// No leading space — the control case.
		await Parser.CommandParse(1, ConnectionService, MModule.single($"{token}"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"{token} Matched")),
				TestHelpers.MatchingObject(obj), INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Terminal entry (direct player input): typing " {token}" (leading space) should still match the $command.
	/// This is the exact scenario from the bug report.
	/// </summary>
	[Test]
	public async ValueTask LeadingSpace_TerminalEntry_StillMatches()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcLeadSpaceTerm");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&UTEST_LEAD_TERM {obj}=${token}:@emit {token} Matched"));

		// Leading space before the command (player typed " test").
		await Parser.CommandParse(1, ConnectionService, MModule.single($" {token}"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"{token} Matched")),
				TestHelpers.MatchingObject(obj), INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Command-list path (queued/callback, e.g. softcode action lists): a single command with a
	/// leading space run via CommandListParse should still match the $command.
	/// </summary>
	[Test]
	public async ValueTask LeadingSpace_CommandList_StillMatches()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var listParser = WebAppFactoryArg.CommandParser;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcLeadSpaceList");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&UTEST_LEAD_LIST {obj}=${token}:@emit {token} Matched"));

		// Command-list entry point with a leading space before the command.
		await listParser.CommandListParse(MModule.single($" {token}"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"{token} Matched")),
				TestHelpers.MatchingObject(obj), INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Command-list path with a space after a ';' separator ("@@ comment;  {token}"). The $command is the
	/// second command in the list; it must match its own per-command slice, not the whole list source.
	/// </summary>
	[Test]
	public async ValueTask LeadingSpace_AfterSemicolonInCommandList_StillMatches()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var listParser = WebAppFactoryArg.CommandParser;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcLeadSpaceSemi");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&UTEST_LEAD_SEMI {obj}=${token}:@emit {token} Matched"));

		// A null command followed by a space-prefixed $command in the same list.
		await listParser.CommandListParse(MModule.single($"@@ ignore;  {token}"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"{token} Matched")),
				TestHelpers.MatchingObject(obj), INotifyService.NotificationType.Emit);
	}

	// ── Command-list per-command $command matching ────────────────────────────
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
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var listParser = WebAppFactoryArg.CommandParser;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcSemiNoSpace");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&UTEST_SEMI_NOSPACE {obj}=${token}:@emit {token} Matched"));

		// Second command in the list, NO leading space.
		await listParser.CommandListParse(MModule.single($"@@ ignore;{token}"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"{token} Matched")),
				TestHelpers.MatchingObject(obj), INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Terminal entry: a trailing space after the command (" {token} ") should still match an exact $command.
	/// The wildcard patterns are anchored at the end (^...$), so a trailing space breaks an exact match.
	/// </summary>
	[Test]
	public async ValueTask TrailingSpace_TerminalEntry_StillMatches()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcTrailSpaceTerm");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&UTEST_TRAIL_TERM {obj}=${token}:@emit {token} Matched"));

		// Trailing space after the command.
		await Parser.CommandParse(1, ConnectionService, MModule.single($"{token} "));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"{token} Matched")),
				TestHelpers.MatchingObject(obj), INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Command-list path: a trailing space after the command run via CommandListParse should still match.
	/// </summary>
	[Test]
	public async ValueTask TrailingSpace_CommandList_StillMatches()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var listParser = WebAppFactoryArg.CommandParser;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcTrailSpaceList");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&UTEST_TRAIL_LIST {obj}=${token}:@emit {token} Matched"));

		// Command-list entry point with a trailing space after the command.
		await listParser.CommandListParse(MModule.single($"{token} "));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"{token} Matched")),
				TestHelpers.MatchingObject(obj), INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// A $command that is the FIRST command in a list with a trailing built-in ("{token};@emit X").
	/// It must match its own slice, not "token;@emit X".
	/// </summary>
	[Test]
	public async ValueTask SemicolonList_FirstCommand_WithTail_Match()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var listParser = WebAppFactoryArg.CommandParser;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcSemiFirst");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&UTEST_SEMI_FIRST {obj}=${token}:@emit {token} Matched"));

		await listParser.CommandListParse(MModule.single($"{token};@emit TAIL"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"{token} Matched")),
				TestHelpers.MatchingObject(obj), INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Three-command list: a $command in the MIDDLE and at the END both match. Guards the
	/// Stop.StopIndex arithmetic for the final list element.
	/// </summary>
	[Test]
	public async ValueTask SemicolonList_MiddleAndLastCommands_Match()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var listParser = WebAppFactoryArg.CommandParser;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcSemiThree");
		var mid = TestIsolationHelpers.GenerateUniqueName("ucmid");
		var last = TestIsolationHelpers.GenerateUniqueName("uclast");
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&UTEST_SEMI_MID {obj}=${mid}:@emit {mid} MidMatched"));
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&UTEST_SEMI_LAST {obj}=${last}:@emit {last} LastMatched"));

		await listParser.CommandListParse(MModule.single($"@emit HEAD;{mid};{last}"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"{mid} MidMatched")),
				TestHelpers.MatchingObject(obj), INotifyService.NotificationType.Emit);
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"{last} LastMatched")),
				TestHelpers.MatchingObject(obj), INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Per-command argument capture in a list (the "funky indexes" guard). A wildcard $command as the
	/// SECOND command must capture %0 from its OWN slice — "Bob" — not leak the first command's text or a
	/// wrong offset. The first command is deliberately a different length than the second.
	/// </summary>
	[Test]
	public async ValueTask SemicolonList_WildcardArg_CapturesPerCommandSlice()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var listParser = WebAppFactoryArg.CommandParser;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcSemiArg");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&UTEST_SEMI_ARG {obj}=${token} *:@emit GREET=<%0>"));

		// First command is a longer @emit; second is the wildcard $command with arg "Bob".
		await listParser.CommandListParse(MModule.single($"@emit AAAAAAAAAA;{token} Bob"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, "GREET=<Bob>")),
				TestHelpers.MatchingObject(obj), INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Two-wildcard $command as the second command in a list: %0 and %1 each capture from the
	/// per-command slice. Further guards capture-group index alignment after slicing.
	/// </summary>
	[Test]
	public async ValueTask SemicolonList_TwoWildcardArgs_CapturePerCommandSlice()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var listParser = WebAppFactoryArg.CommandParser;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UdcSemiArg2");
		var token = TestIsolationHelpers.GenerateUniqueName("uc");
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&UTEST_SEMI_ARG2 {obj}=${token} * to *:@emit MSG=<%0>-<%1>"));

		await listParser.CommandListParse(MModule.single($"@emit IGNORE;{token} Alice to Bob"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, "MSG=<Alice>-<Bob>")),
				TestHelpers.MatchingObject(obj), INotifyService.NotificationType.Emit);
	}
}
