using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Commands;

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
}
