using Mediator;
﻿using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
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
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private Task<TestIsolationHelpers.TestPlayer> CreateTestPlayerAsync(string namePrefix) =>
		TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, namePrefix);

	[Test]
	public async ValueTask WildcardEqSplitCommandPassesArgsToEmit()
	{
		var testPlayer = await CreateTestPlayerAsync("WilEqSplCom");
		var executor = testPlayer.DbRef;
		// Set a $ command attribute with an EqSplit wildcard pattern on #1 (God player, in room #0)
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"&UTEST_WILDEQ {testPlayer.DbRef}=$utest_wildeq *=*:@emit Boo! %0 - %1"));

		// Fire the command — %0 should be "a", %1 should be "b"
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("utest_wildeq a=b"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Boo! a - b")),
				Arg.Any<AnySharpObject?>(),
				INotifyService.NotificationType.Emit);
	}

	// ── Wildcard pattern tests ──────────────────────────────────────────────

	/// <summary>
	/// Single wildcard: $cmd * — %0 captures everything after the command name.
	/// </summary>
	[Test]
	public async ValueTask Wildcard_Single_SubstitutesArg()
	{
		var testPlayer = await CreateTestPlayerAsync("WilSinSubArg");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"&UTEST_SINGLE {testPlayer.DbRef}=$utest_greet *:@emit Hello, %0!"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("utest_greet World"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Hello, World!")),
				Arg.Any<AnySharpObject?>(),
				INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Two wildcards with a literal word between them: $cmd * to * — %0 and %1 are the two captures.
	/// </summary>
	[Test]
	public async ValueTask Wildcard_TwoCaptures_WithLiteralBetween_SubstitutesBothArgs()
	{
		var testPlayer = await CreateTestPlayerAsync("WilTwoCapWit");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"&UTEST_TWO {testPlayer.DbRef}=$utest_msg * to *:@emit Message from %0 to %1"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("utest_msg Alice to Bob"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Message from Alice to Bob")),
				Arg.Any<AnySharpObject?>(),
				INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Exact match (no wildcards): $cmd — fires only on the precise command string; no substitution vars.
	/// </summary>
	[Test]
	public async ValueTask Wildcard_ExactMatch_NoWildcards_FiresCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("WilExaMatNo");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"&UTEST_EXACT {testPlayer.DbRef}=$utest_ping:@emit Pong!"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("utest_ping"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Pong!")),
				Arg.Any<AnySharpObject?>(),
				INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Three wildcards: $cmd * * * — %0, %1, %2 each capture one word.
	/// </summary>
	[Test]
	public async ValueTask Wildcard_ThreeCaptures_SubstitutesAllThreeArgs()
	{
		var testPlayer = await CreateTestPlayerAsync("WilThrCapSub");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"&UTEST_THREE {testPlayer.DbRef}=$utest_three * * *:@emit A=%0 B=%1 C=%2"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("utest_three foo bar baz"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "A=foo B=bar C=baz")),
				Arg.Any<AnySharpObject?>(),
				INotifyService.NotificationType.Emit);
	}

	// ── Regex pattern tests ──────────────────────────────────────────────────

	/// <summary>
	/// Regex single capture group: %0 is the full match, %1 is the first capture group.
	/// </summary>
	[Test]
	public async ValueTask Regex_SingleCaptureGroup_SubstitutesArg()
	{
		var testPlayer = await CreateTestPlayerAsync("RegSinCapGro");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single(@"&UTEST_RX1 #1=$utest_rsay (.+):@emit You said: %1"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@set {testPlayer.DbRef}/UTEST_RX1=regexp"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("utest_rsay hello world"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "You said: hello world")),
				Arg.Any<AnySharpObject?>(),
				INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Regex %0 is the full match string: $utest_rfull prefix_(\d+) → %0="prefix_42", %1="42".
	/// Uses [0-9]+ instead of \d+ to avoid MUSH backslash escaping on attribute set.
	/// </summary>
	[Test]
	public async ValueTask Regex_PercentZeroIsFullMatch_PercentOneIsCaptureGroup()
	{
		var testPlayer = await CreateTestPlayerAsync("RegPerZerIs");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"&UTEST_RX2 {testPlayer.DbRef}=$utest_rfull prefix_([0-9]+):@emit Full: %0, Part: %1"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@set {testPlayer.DbRef}/UTEST_RX2=regexp"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("utest_rfull prefix_42"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Full: utest_rfull prefix_42, Part: 42")),
				Arg.Any<AnySharpObject?>(),
				INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Regex two capture groups: $cmd ([A-Za-z]+) ([A-Za-z]+) — %1 and %2 capture the two words.
	/// Uses [A-Za-z]+ instead of \w+ to avoid MUSH backslash escaping on attribute set.
	/// </summary>
	[Test]
	public async ValueTask Regex_TwoCaptureGroups_SubstitutesBothArgs()
	{
		var testPlayer = await CreateTestPlayerAsync("RegTwoCapGro");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"&UTEST_RX3 {testPlayer.DbRef}=$utest_rtell ([A-Za-z]+) ([A-Za-z]+):@emit %1 messaged %2"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@set {testPlayer.DbRef}/UTEST_RX3=regexp"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("utest_rtell Alice Bob"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Alice messaged Bob")),
				Arg.Any<AnySharpObject?>(),
				INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// Regex named capture groups are accessible by their numeric index (%1, %2).
	/// Uses [0-9]+ instead of \d+ to avoid MUSH backslash escaping on attribute set.
	/// </summary>
	[Test]
	public async ValueTask Regex_NamedCaptureGroups_AccessibleByIndex()
	{
		var testPlayer = await CreateTestPlayerAsync("RegNamCapGro");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"&UTEST_RX4 {testPlayer.DbRef}=$utest_roll (?<num>[0-9]+)d(?<sides>[0-9]+):@emit Rolling %1d%2"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@set {testPlayer.DbRef}/UTEST_RX4=regexp"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("utest_roll 3d6"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Rolling 3d6")),
				Arg.Any<AnySharpObject?>(),
				INotifyService.NotificationType.Emit);
	}

	[Test]
	[Category("TestInfrastructure")]
	[Skip("Test needs investigation - unrelated to communication commands")]
	public async Task SetAndResetCacheTest()
	{
		var testPlayer = await CreateTestPlayerAsync("SetAndResCac");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"&cmd`setandresetcache {testPlayer.DbRef}=$test:@pemit {testPlayer.DbRef}=Value 1 received"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("test"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"&cmd`setandresetcache {testPlayer.DbRef}=$test2:@pemit {testPlayer.DbRef}=Value 2 received"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("test2"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToPlainText() == "Value 1 received") ||
				(msg.IsT1 && msg.AsT1 == "Value 1 received")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToPlainText() == "Value 2 received") ||
				(msg.IsT1 && msg.AsT1 == "Value 2 received")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}
}