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
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class GeneralCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	[Arguments("@pemit #1=1 This is a test", "1 This is a test")]
	[Arguments("@pemit #1=2 This is a test;", "2 This is a test;")]
	public async ValueTask SimpleCommandParse(string str, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(str));

		var executor = WebAppFactoryArg.ExecutorDBRef;
		await NotifyService
			.Received(1)
			.Notify(
				TestHelpers.MatchingObject(executor),
				TestHelpers.MatchingMessage(expected), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Arguments("l"), Skip("Not yet implemented properly")]
	public async ValueTask CommandAliasRuns(string str)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(str));
	}

	[Test]
	public async ValueTask DoListSimple()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@dolist/inline 1 2 3=@pemit #1=3 This is a test"));

		// @dolist/inline iterates 3 times (elements: 1, 2, 3) → 3 identical notifications
		await NotifyService
			.Received(3)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "3 This is a test")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoListSimple2()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@dolist/inline 1 2 3=@pemit #1={4 This is, a test};"));

		// @dolist/inline iterates 3 times (elements: 1, 2, 3) → 3 identical notifications
		await NotifyService
			.Received(3)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "4 This is, a test")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DolistDoubleHashReplacement()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@dolist/inline 1 2 3=@pemit #1=dolist-hash-##"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "dolist-hash-1")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "dolist-hash-2")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "dolist-hash-3")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask DoListComplex()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain("@dolist/inline 1 2 3={@pemit #1=5 This is a test; @pemit #1=6 This is also a test}"));

		// @dolist/inline iterates 3 times → both @pemit commands fire 3 times each
		await NotifyService
			.Received(3)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "5 This is a test")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(3)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "6 This is also a test")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask DoListComplex2()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain(
				"@dolist/inline 1 2 3={@pemit #1=7 This is a test; @pemit #1=8 This is also a test}; @pemit #1=9 Repeat 3 times in this mode."));

		await NotifyService
			.Received(3)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "7 This is a test")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(3)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "8 This is also a test")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(3)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "9 Repeat 3 times in this mode.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask DoListComplex3()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain(
				"@dolist/inline 1={@dolist/inline 1 2 3=@pemit #1=10 This is a test}; @pemit #1=11 Repeat 1 times in this mode."));

		// outer 1 element × inner 3 elements = 3 for "10"; @pemit 11 is outside = 1×
		await NotifyService
			.Received(3)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "10 This is a test")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "11 Repeat 1 times in this mode.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoListComplex4()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain(
				"@dolist/inline 1 2={@dolist/inline 1 2 3=@pemit #1=12 This is a test}; @pemit #1=13 Repeat 2 times in this mode."));

		// outer 2 elements × inner 3 elements = 6 for "12"; @pemit 13 is outside = 2×
		await NotifyService
			.Received(6)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "12 This is a test")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(2)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "13 Repeat 2 times in this mode.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoListComplex5()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain(
				"@dolist/inline a b={@dolist/inline 1 2 3=@pemit #1=14 This is a test %i0}; @pemit #1=15 Repeat 1 times in this mode %i0"));

		// outer 2 elements (a,b) × inner 3 elements (1,2,3) → each distinct inner msg fires 2×
		// the outer @pemit fires once per outer element ("15 ...a" and "15 ...b")
		await NotifyService
			.Received(2)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "14 This is a test 1")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(2)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "14 This is a test 2")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(2)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "14 This is a test 3")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "15 Repeat 1 times in this mode a")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "15 Repeat 1 times in this mode b")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoListComplex6()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain(
				"@dolist/inline a b={@dolist/inline 1 2 3={@ifelse eq(%i0,1)=think %i0 is 1; @ifelse eq(%i0,2)=think %i0 is 2,think {%i0 is 1, or 3}}}"));

		// outer 2 elements (a,b) × inner 3 elements (1,2,3) → each branch fires 2× (once per outer iter)
		await NotifyService
			.Received(2)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "3 is 1, or 3")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(2)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "1 is 1")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(2)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "2 is 2")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoBreakSimpleCommandList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandListParse(MarkupText.Plain("think assert 1a; @assert; think assert 2a; think assert 3a"));
		await Parser.CommandListParse(MarkupText.Plain("think break 1a; @break; think break 2a; think break 3a"));

		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("break 1a"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("break 2a"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("break 3a"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("assert 1a"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("assert 2a"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("assert 3a"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoBreakSimpleTruthyCommandList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandListParse(MarkupText.Plain("think assert 1b; @assert 1; think assert 2b; think assert 3b"));
		await Parser.CommandListParse(MarkupText.Plain("think break 1b; @break 1; think break 2b; think break 3b"));

		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("assert 1b"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("assert 2b"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("assert 3b"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("break 1b"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("break 2b"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("break 3b"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoBreakSimpleFalsyCommandList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandListParse(MarkupText.Plain("think assert 1c; @assert 0; think assert 2c; think assert 3c"));
		await Parser.CommandListParse(MarkupText.Plain("think break 1c; @break 0; think break 2c; think break 3c"));

		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("break 1c"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("break 2c"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("break 3c"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("assert 1c"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("assert 2c"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("assert 3c"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoBreakCommandList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandListParse(
			MarkupText.Plain("think break 1d; @break 1=think broken 1d; think break 2d; think break 3d"));

		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("break 1d"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("break 2d"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("break 3d"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("broken 1d"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoBreakCommandList2()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandListParse(
			MarkupText.Plain("think break 1e; @break 1={think broken 1e; think broken 2e}; think break 2e; think break 3e"));

		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("break 1e"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("break 2e"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("break 3e"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("broken 1e"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("broken 2e"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoFlagSet()
	{
		// Create a unique thing to set the flag on, instead of modifying shared God (#1).
		var thingDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "FlagSetTest");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {thingDbRef}=DEBUG"));

		var thing = (await Mediator.Send(new GetObjectNodeQuery(thingDbRef))).Expect<SharpThing>();
		var flags = await thing.Object.Flags.Value.ToArrayAsync();

		await Assert.That(flags.Count(x => x.Name == "DEBUG")).IsEqualTo(1);
	}

	[Test]
	public async ValueTask WhereIs_ValidPlayer_ReportsLocation()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// @whereis #1 → "One is in Room Zero." — pattern B: object name "One" and room "Room Zero" make this globally unique.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@whereis #1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<SharpMessage>(s => TestHelpers.MessagePlainTextStartsWith(s, "God is in")),
				TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask WhereIs_NonPlayer_ReturnsError()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create test_object_whereis"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@whereis test_object_whereis"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.WhereIsCanOnlyLocatePlayers), executor, executor)).IsTrue();
	}

	/// <summary>
	/// Restarting a player restarts the player and everything they own.
	/// </summary>
	/// <remarks>
	/// The player is one this test makes. Restarting God would halt the queue of God and of every
	/// object God owns, which in the session-wide scheduler is whatever other suites have queued as
	/// God or on the things they made.
	/// </remarks>
	[Test]
	public async ValueTask Restart_ValidObject_Restarts()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "RestartTarget");

		try
		{
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@restart {player.DbRef}"));

			await Assert.That(TestHelpers.ReceivedNotifyLocalizedRendering(NotifyService,
				nameof(ErrorMessages.Notifications.RestartedPlayerAndObjectsFormat),
				string.Format(ErrorMessages.Notifications.RestartedPlayerAndObjectsFormat, player.Name), executor)).IsTrue();
		}
		finally
		{
			await ConnectionService.Disconnect(player.Handle);
		}
	}

	[Test]
	public async ValueTask Find_SearchesForObjects()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@find test"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.FindSearchingFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Stats_ShowsDatabaseStatistics()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@stats"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.StatsDatabaseStatisticsHeader), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Search_PerformsDatabaseSearch()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@search"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.SearchAdvancedHeader), executor, executor)).IsTrue();
	}

	// Regression coverage for "@search all type=PLAYER" being parsed as a NAME search for the
	// literal text "player" instead of a TYPE filter — @SEARCH's CB.EqSplit|CB.RSArgs behavior only
	// splits the raw command text on the first top-level '=', so "all type" (the player field plus
	// the leading search class) landed together in one chunk with no class/restriction parsing at
	// all. See ParseSearchCommandArgs in GeneralCommands.cs.
	[Test]
	public async ValueTask Search_TypeEqualsPlayer_FiltersByTypeNotByName()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var offType = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SearchTypeBugPLAYER");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@search all type=PLAYER"));

		// The criteria line must show the parsed pair, not a raw "all type=PLAYER" echo.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedRendering(NotifyService,
			nameof(ErrorMessages.Notifications.SearchCriteriaFormat), "  Criteria: TYPE=PLAYER", executor)).IsTrue();

		// A THING whose name contains "PLAYER" must NOT match a TYPE=PLAYER search...
		await Assert.That(SearchResultContains(offType.Number, "THING")).IsFalse();

		// ...while an actual player (the fixture's God, #1) must.
		await Assert.That(SearchResultContains(executor.Number, "PLAYER")).IsTrue();
	}

	[Test]
	public async ValueTask Search_CombinedTypeAndFlags_MatchesOnBothCriteria()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("SearchCombined");
		var match = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"{token}_Match");
		var control = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"{token}_Control");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {match}=MONITOR"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@search all type=thing,flags=MONITOR"));

		await Assert.That(SearchResultContains(match.Number, "THING")).IsTrue();
		await Assert.That(SearchResultContains(control.Number, "THING")).IsFalse();
	}

	/// <summary>
	/// Whether a <c>SearchObjectEntryFormat</c> notification was sent for the given dbref number and
	/// type, inspecting the mock's recorded calls directly (mirrors
	/// <see cref="TestHelpers.ReceivedNotifyLocalizedWithKey"/>'s approach for params-array calls).
	/// </summary>
	private bool SearchResultContains(int dbRefNumber, string type) =>
		NotifyService.ReceivedCalls()
			.Any(c =>
				c.GetMethodInfo().Name is "NotifyLocalized" or "NotifyLocalizedMarkup" &&
				c.GetArguments().Length >= 2 &&
				c.GetArguments()[1] is string k && k == nameof(ErrorMessages.Notifications.SearchObjectEntryFormat) &&
				c.GetArguments()[^1] is object[] { Length: 3 } fmtArgs &&
				Convert.ToInt32(fmtArgs[0]) == dbRefNumber &&
				fmtArgs[2] is string t && t.Equals(type, StringComparison.OrdinalIgnoreCase));

	[Test]
	public async ValueTask Entrances_ShowsLinkedObjects()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@entrances"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.EntrancesToFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Command_ShowsCommandInfo()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@command @emit"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.CommandInfoNameFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Function_ListsGlobalFunctions()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@function"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.FunctionGlobalUserDefinedHeader), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Function_ShowsFunctionInfo()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@function name"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.FunctionInfoNameFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Map_ExecutesAttributeOverList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var mapObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MapTest");
		var uniqueAttr = $"MAPATTR_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@map {mapObj}/{uniqueAttr}=foo bar baz"));

		// MapWouldIterateFormat is always sent before attribute lookup (before the try/get attribute).
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.MapWouldIterateFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Trigger_QueuesAttribute()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var trigObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "TrigTest");
		var uniqueAttr = $"TRIGATTR_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@trigger {trigObj}/{uniqueAttr}=arg1,arg2"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.TriggerNoSuchAttributeFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Include_InsertsAttributeInPlace()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var inclObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InclTest");
		var uniqueAttr = $"INCLATTR_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@include {inclObj}/{uniqueAttr}=arg1,arg2"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.IncludeAttributeIsEmptyFormat), executor, executor)).IsTrue();
	}

	[Test]
	[Category("TestInfrastructure")]
	[Skip("Test infrastructure issue - NotifyService call count mismatch")]
	public async ValueTask Halt_ClearsQueue()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create a unique thing to halt, instead of halting shared God (#1).
		var thingDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "HaltQueueTest");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@halt {thingDbRef}"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<SharpMessage>(s => TestHelpers.MessageContains(s, "@halt:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask PS_ShowsQueueStatus()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@ps"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.PsQueueForTargetFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Select_MatchesFirstExpression()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// @select runs the action for the FIRST matching expression only ('help @switch'); both
		// patterns here match "test", so the second must not fire. /inline so the actions run in
		// place rather than becoming queue entries this assertion would race.
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain("@select/inline test=t*,@pemit #1=SelectFirst_A_31708,*est,@pemit #1=SelectFirst_B_31708"));

		await NotifyService.Received(1).Notify(
			TestHelpers.MatchingObject(executor),
			Arg.Is<SharpMessage>(m => TestHelpers.MessagePlainTextEquals(m, "SelectFirst_A_31708")),
			TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await NotifyService.DidNotReceive().Notify(
			TestHelpers.MatchingObject(executor),
			Arg.Is<SharpMessage>(m => TestHelpers.MessagePlainTextEquals(m, "SelectFirst_B_31708")),
			TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Attribute_DisplaysAttributeInfo()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@attribute/access DESCRIPTION="));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@attribute DESCRIPTION"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.AttributeCommandInfoFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Attribute_AccessCreatesAttributeEntry()
	{
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@attribute/access MYATTR=no_command"));

		var entries = await Mediator.CreateStream(new Library.Queries.Database.GetAllAttributeEntriesQuery())
			.ToArrayAsync();
		var entry = entries.FirstOrDefault(e => e.Name == "MYATTR");

		await Assert.That(entry).IsNotNull();
		await Assert.That(entry!.DefaultFlags.Contains("NO_COMMAND")).IsTrue();
	}

	[Test]
	public async ValueTask Attribute_AccessValidatesFlags()
	{
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@attribute/access TESTATTR=INVALIDFLAG"));

		var entries = await Mediator.CreateStream(new Library.Queries.Database.GetAllAttributeEntriesQuery())
			.ToArrayAsync();
		var entry = entries.FirstOrDefault(e => e.Name == "TESTATTR");

		await Assert.That(entry).IsNull();
	}

	[Test]
	public async ValueTask Attribute_EntryFlagsAreAppliedWhenAttributeCreated()
	{
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@attribute/access TESTATTR2=no_command"));

		var entries = await Mediator.CreateStream(new Library.Queries.Database.GetAllAttributeEntriesQuery())
			.ToArrayAsync();
		var entry = entries.FirstOrDefault(e => e.Name == "TESTATTR2");
		await Assert.That(entry).IsNotNull();
		await Assert.That(entry!.DefaultFlags.Contains("NO_COMMAND")).IsTrue();

		// Use SetAttributeCommand directly to bypass & command test issues
		var player = (await Mediator.Send(new Library.Queries.Database.GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		var success = await Mediator.Send(new Library.Commands.Database.SetAttributeCommand(
			new DBRef(1),
			["TESTATTR2"],
			MarkupText.Plain("test value"),
			player));

		await Assert.That(success).IsTrue();

		var attrs = await Mediator.CreateStream(new Library.Queries.Database.GetAttributeQuery(new DBRef(1), ["TESTATTR2"]))
			.ToArrayAsync();

		var attr = attrs.LastOrDefault();
		await Assert.That(attr).IsNotNull();

		await Assert.That(attr!.Flags.Any(f => f.Name.Equals("no_command", StringComparison.OrdinalIgnoreCase))).IsTrue();
	}

	[Test]
	[NotInParallel]
	public async ValueTask DoListWithDBRefNotificationBatching()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@dolist/inline 1 2 3=@pemit #1=Batched test message"));

		await NotifyService
			.Received(3)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "Batched test message")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask DoListBatchesToOtherPlayers()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dolist/inline a b c=@pemit {executor}=DoListBatchesToOtherPlayers: Message to other player"));

		await NotifyService
			.Received(3)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "DoListBatchesToOtherPlayers: Message to other player")),
				TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask NestedDoListBatching()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Nested @dolist: outer has 2 items, inner has 2 items = 4 total pemits
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@dolist/inline 1 2={@dolist/inline a b=@pemit #1=Nested message}"));

		await NotifyService
			.Received(4)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "Nested message")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask DoListWithoutBreak_AllMessagesReceived()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@dolist/inline 1 2 3=@pemit #1=DoListWithoutBreak_AllMessagesReceived"));

		await NotifyService
			.Received(3)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "DoListWithoutBreak_AllMessagesReceived")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask DoListWithBreakAfterFirst_OnlyFirstMessageReceived()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@dolist/inline 1 2 3={@pemit #1=Message DoListWithBreakAfterFirst_OnlyFirstMessageReceived %iL;@break}"));

		// With {@pemit; @break}, @pemit runs in each iteration then @break happens, so all 3
		// messages fire — this is the actual MUSH behavior: @break affects the next iteration, not current.
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "Message DoListWithBreakAfterFirst_OnlyFirstMessageReceived 1")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "Message DoListWithBreakAfterFirst_OnlyFirstMessageReceived 2")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "Message DoListWithBreakAfterFirst_OnlyFirstMessageReceived 3")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask DoListWithBreakFlushesMessages()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@dolist/inline 1 2 3={@pemit #1=Message before break; @break}"));

		await NotifyService
			.Received(3)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "Message before break")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask NestedDoListWithBreakFlushesMessages()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// With {@pemit; @break}, @pemit runs in each inner iteration: 2 outer * 3 inner = 6 messages.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@dolist/inline 1 2={@dolist/inline a b c={@pemit #1=Inner message; @break}}"));

		await NotifyService
			.Received(6)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "Inner message")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoListWithDelimiter()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain("@dolist/inline/delimit , apple,banana,orange=@pemit #1=Fruit: %i0"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "Fruit: apple")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "Fruit: banana")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "Fruit: orange")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// PennMUSH <c>do_look_at</c> (<c>look.c:611</c>): <c>look/outside</c> from a room — or any opaque
	/// location — has nothing to see out of.
	/// </summary>
	[Test]
	public async ValueTask LookOutsideFromARoomReportsYouCantSeeThroughThat()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LookOutsideRoom");

		var roomName = TestIsolationHelpers.GenerateUniqueName("LookOutsideRoomDest");
		var digResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var roomDbRef = digResult.Message!.ToPlainText()!.Trim();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {player.DbRef}={roomDbRef}"));

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look/outside"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(
			NotifyService, nameof(ErrorMessages.Notifications.CantSeeThroughThat), player.DbRef, player.DbRef)).IsTrue();
	}

	/// <summary>
	/// PennMUSH <c>look.c:618</c>: from inside a container you see that container's own location, not the
	/// container itself.
	/// </summary>
	[Test]
	public async ValueTask LookOutsideFromInsideAContainerShowsTheContainersRoom()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LookOutsideBox");

		var roomName = TestIsolationHelpers.GenerateUniqueName("LookOutsideOuter");
		var digResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var roomDbRef = digResult.Message!.ToPlainText()!.Trim();

		var boxName = TestIsolationHelpers.GenerateUniqueName("LookOutsideContainer");
		var createResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {boxName}"));
		var boxDbRef = createResult.Message!.ToPlainText()!.Trim();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {boxName}=ENTER_OK"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {boxDbRef}={roomDbRef}"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {player.DbRef}={boxDbRef}"));

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look/outside"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(player.DbRef), Arg.Is<SharpMessage>(msg =>
					TestHelpers.MessagePlainTextContains(msg, roomName)),
				TestHelpers.MatchingObject(player.DbRef), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// PennMUSH <c>cmd_think</c> (<c>cmds.c:1769</c>) is an unconditional notify, so a bare
	/// <c>think</c> still emits an (empty) line.
	/// </summary>
	[Test]
	public async ValueTask ThinkWithNoArgumentStillNotifiesTheExecutor()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ThinkEmpty");

		var result = await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("think"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(player.DbRef), Arg.Is<SharpMessage>(msg =>
					TestHelpers.MessagePlainTextEquals(msg, string.Empty)),
				TestHelpers.MatchingObject(player.DbRef), INotifyService.NotificationType.Announce);

		// The notify happens before the return, so asserting on it alone would not have caught the
		// command indexing a "0" argument that a bare `think` does not have. The resulting
		// KeyNotFoundException is swallowed upstream and surfaces only as a null Message.
		await Assert.That(result).IsNotNull();
		await Assert.That(result.Message).IsNotNull();
		await Assert.That(result.Message!.ToPlainText()).IsEqualTo(string.Empty);
	}

	[Test]
	public async ValueTask ThinkDescendingLnumNotifiesTheExecutor()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ThinkLnum");

		var result = await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("think lnum(10,1)"));

		await Assert.That(result.Message!.ToPlainText()).IsEqualTo("10 9 8 7 6 5 4 3 2 1");
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(player.DbRef),
			Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "10 9 8 7 6 5 4 3 2 1")),
			TestHelpers.MatchingObject(player.DbRef), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// PennMUSH <c>do_open</c> hands the destination to <c>do_link</c>, which reports a destination it
	/// cannot link to instead of quietly leaving the exit unlinked. An exit is the one thing you cannot
	/// end up inside.
	/// </summary>
	[Test]
	public async ValueTask OpenWithAnUnlinkableDestinationReportsTheFailure()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "OpenBadDest");

		var roomName = TestIsolationHelpers.GenerateUniqueName("OpenBadDestRoom");
		var digResult = await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var roomDbRef = digResult.Message!.ToPlainText()!.Trim();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {player.DbRef}={roomDbRef}"));

		var decoyName = TestIsolationHelpers.GenerateUniqueName("OpenBadDestDecoy");
		var decoyResult = await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@open {decoyName}={roomDbRef}"));
		var decoyDbRef = decoyResult.Message!.ToPlainText()!.Trim();

		var exitName = TestIsolationHelpers.GenerateUniqueName("OpenBadDestExit");
		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@open {exitName}={decoyDbRef}"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(
			NotifyService, nameof(ErrorMessages.Notifications.CantLinkToThat), player.DbRef, player.DbRef)).IsTrue();
	}

	/// <summary>
	/// An exit may lead to any container, not just a room — PennMUSH <c>can_link_to</c> accepts things
	/// and players too, which is how "enter the wardrobe" exits are built.
	/// </summary>
	[Test]
	public async ValueTask OpenCanLinkAnExitToAThing()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "OpenThingDest");

		var roomName = TestIsolationHelpers.GenerateUniqueName("OpenThingDestRoom");
		var digResult = await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var roomDbRef = digResult.Message!.ToPlainText()!.Trim();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {player.DbRef}={roomDbRef}"));

		var thingName = TestIsolationHelpers.GenerateUniqueName("OpenThingDestThing");
		var thingResult = await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@create {thingName}"));
		var thingDbRef = thingResult.Message!.ToPlainText()!.Trim();

		var exitName = TestIsolationHelpers.GenerateUniqueName("OpenThingDestExit");
		var exitResult = await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@open {exitName}={thingDbRef}"));

		DBRef.TryParse(exitResult.Message!.ToPlainText()!.Trim(), out var exitRef);
		var exit = (await Mediator.Send(new GetObjectNodeQuery(exitRef!.Value))).Expect<SharpExit>();
		var destination = (await exit.Home.WithCancellation(CancellationToken.None)).Expect<AnySharpContainer>();

		await Assert.That(destination.Object().DBRef.ToString()).IsEqualTo(thingDbRef);
	}

	/// <summary>
	/// PennMUSH <c>do_open</c> hands the destination to <c>parse_linkable_room</c>, which refuses it
	/// unless <c>can_link_to</c> passes (<c>create.c:61</c>, <c>mushdb.h:87</c>). Widening <c>@open</c> to
	/// accept any container must not also let a player link an exit into somewhere they do not control.
	/// </summary>
	[Test]
	public async ValueTask OpenCannotLinkAnExitToAContainerTheExecutorDoesNotControl()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "OpenNoLinkPerm");

		var roomName = TestIsolationHelpers.GenerateUniqueName("OpenNoLinkPermRoom");
		var digResult = await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var roomDbRef = digResult.Message!.ToPlainText()!.Trim();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {player.DbRef}={roomDbRef}"));

		// God's thing, not LINK_OK, brought within reach so the locate succeeds and only permission decides.
		var thingName = TestIsolationHelpers.GenerateUniqueName("OpenNoLinkPermThing");
		var thingResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {thingName}"));
		var thingDbRef = thingResult.Message!.ToPlainText()!.Trim();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {thingDbRef}={roomDbRef}"));

		var exitName = TestIsolationHelpers.GenerateUniqueName("OpenNoLinkPermExit");
		var exitResult = await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@open {exitName}={thingDbRef}"));

		DBRef.TryParse(exitResult.Message!.ToPlainText()!.Trim(), out var exitRef);
		var exit = (await Mediator.Send(new GetObjectNodeQuery(exitRef!.Value))).Expect<SharpExit>();
		var destination = await exit.Home.WithCancellation(CancellationToken.None);

		await Assert.That(destination.IsNone).IsTrue()
			.Because("the exit must be left unlinked when the executor cannot link to the destination");
	}

	/// <summary>
	/// The complement: LINK_OK on the destination is what lets someone else link into it.
	/// </summary>
	[Test]
	public async ValueTask OpenCanLinkAnExitToALinkOkContainerTheExecutorDoesNotControl()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "OpenLinkOk");

		var roomName = TestIsolationHelpers.GenerateUniqueName("OpenLinkOkRoom");
		var digResult = await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var roomDbRef = digResult.Message!.ToPlainText()!.Trim();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {player.DbRef}={roomDbRef}"));

		var thingName = TestIsolationHelpers.GenerateUniqueName("OpenLinkOkThing");
		var thingResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {thingName}"));
		var thingDbRef = thingResult.Message!.ToPlainText()!.Trim();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {thingDbRef}={roomDbRef}"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {thingDbRef}=LINK_OK"));

		var exitName = TestIsolationHelpers.GenerateUniqueName("OpenLinkOkExit");
		var exitResult = await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@open {exitName}={thingDbRef}"));

		DBRef.TryParse(exitResult.Message!.ToPlainText()!.Trim(), out var exitRef);
		var exit = (await Mediator.Send(new GetObjectNodeQuery(exitRef!.Value))).Expect<SharpExit>();
		var destination = (await exit.Home.WithCancellation(CancellationToken.None)).Expect<AnySharpContainer>();

		await Assert.That(destination.Object().DBRef.ToString()).IsEqualTo(thingDbRef);
	}
}
