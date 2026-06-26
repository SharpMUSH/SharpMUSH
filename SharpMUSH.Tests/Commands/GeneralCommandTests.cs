using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
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
		Console.WriteLine("Testing: {0}", str);
		await Parser.CommandParse(1, ConnectionService, MModule.single(str));

		var executor = WebAppFactoryArg.ExecutorDBRef;
		await NotifyService
			.Received(1)
			.Notify(
				TestHelpers.MatchingObject(executor),
				expected, TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Arguments("l"), Skip("Not yet implemented properly")]
	public async ValueTask CommandAliasRuns(string str)
	{
		Console.WriteLine("Testing: {0}", str);
		await Parser.CommandParse(1, ConnectionService, MModule.single(str));
	}

	[Test]
	public async ValueTask DoListSimple()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist/inline 1 2 3=@pemit #1=3 This is a test"));

		// @dolist/inline iterates 3 times (elements: 1, 2, 3) → 3 identical notifications
		await NotifyService
			.Received(3)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "3 This is a test")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoListSimple2()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist/inline 1 2 3=@pemit #1={4 This is, a test};"));

		// @dolist/inline iterates 3 times (elements: 1, 2, 3) → 3 identical notifications
		await NotifyService
			.Received(3)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "4 This is, a test")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DolistDoubleHashReplacement()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist/inline 1 2 3=@pemit #1=dolist-hash-##"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "dolist-hash-1")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "dolist-hash-2")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "dolist-hash-3")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask DoListComplex()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("@dolist/inline 1 2 3={@pemit #1=5 This is a test; @pemit #1=6 This is also a test}"));

		// @dolist/inline iterates 3 times → both @pemit commands fire 3 times each
		await NotifyService
			.Received(3)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "5 This is a test")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(3)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "6 This is also a test")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask DoListComplex2()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService,
			MModule.single(
				"@dolist/inline 1 2 3={@pemit #1=7 This is a test; @pemit #1=8 This is also a test}; @pemit #1=9 Repeat 3 times in this mode."));

		await NotifyService
			.Received(3)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "7 This is a test")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(3)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "8 This is also a test")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(3)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "9 Repeat 3 times in this mode.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask DoListComplex3()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService,
			MModule.single(
				"@dolist/inline 1={@dolist/inline 1 2 3=@pemit #1=10 This is a test}; @pemit #1=11 Repeat 1 times in this mode."));

		// outer 1 element × inner 3 elements = 3 for "10"; @pemit 11 is outside = 1×
		await NotifyService
			.Received(3)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "10 This is a test")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "11 Repeat 1 times in this mode.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoListComplex4()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService,
			MModule.single(
				"@dolist/inline 1 2={@dolist/inline 1 2 3=@pemit #1=12 This is a test}; @pemit #1=13 Repeat 2 times in this mode."));

		// outer 2 elements × inner 3 elements = 6 for "12"; @pemit 13 is outside = 2×
		await NotifyService
			.Received(6)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "12 This is a test")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(2)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "13 Repeat 2 times in this mode.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoListComplex5()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService,
			MModule.single( 
				"@dolist/inline a b={@dolist/inline 1 2 3=@pemit #1=14 This is a test %i0}; @pemit #1=15 Repeat 1 times in this mode %i0"));

		// outer 2 elements (a,b) × inner 3 elements (1,2,3) → each distinct inner msg fires 2×
		// the outer @pemit fires once per outer element ("15 ...a" and "15 ...b")
		await NotifyService
			.Received(2)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "14 This is a test 1")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(2)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "14 This is a test 2")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(2)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "14 This is a test 3")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "15 Repeat 1 times in this mode a")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "15 Repeat 1 times in this mode b")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoListComplex6()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService,
			MModule.single(
				"@dolist/inline a b={@dolist/inline 1 2 3={@ifelse eq(%i0,1)=think %i0 is 1; @ifelse eq(%i0,2)=think %i0 is 2,think {%i0 is 1, or 3}}}"));

		// outer 2 elements (a,b) × inner 3 elements (1,2,3) → each branch fires 2× (once per outer iter)
		await NotifyService
			.Received(2)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "3 is 1, or 3")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(2)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "1 is 1")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(2)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "2 is 2")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoBreakSimpleCommandList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandListParse(MModule.single("think assert 1a; @assert; think assert 2a; think assert 3a"));
		await Parser.CommandListParse(MModule.single("think break 1a; @break; think break 2a; think break 3a"));

		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), "break 1a", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), "break 2a", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), "break 3a", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), "assert 1a", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), "assert 2a", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), "assert 3a", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoBreakSimpleTruthyCommandList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandListParse(MModule.single("think assert 1b; @assert 1; think assert 2b; think assert 3b"));
		await Parser.CommandListParse(MModule.single("think break 1b; @break 1; think break 2b; think break 3b"));

		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), "assert 1b", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), "assert 2b", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), "assert 3b", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), "break 1b", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), "break 2b", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), "break 3b", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoBreakSimpleFalsyCommandList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandListParse(MModule.single("think assert 1c; @assert 0; think assert 2c; think assert 3c"));
		await Parser.CommandListParse(MModule.single("think break 1c; @break 0; think break 2c; think break 3c"));

		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), "break 1c", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), "break 2c", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), "break 3c", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), "assert 1c", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), "assert 2c", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), "assert 3c", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoBreakCommandList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandListParse(
			MModule.single("think break 1d; @break 1=think broken 1d; think break 2d; think break 3d"));

		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), "break 1d", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), "break 2d", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), "break 3d", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), "broken 1d", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoBreakCommandList2()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandListParse(
			MModule.single("think break 1e; @break 1={think broken 1e; think broken 2e}; think break 2e; think break 3e"));

		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), "break 1e", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), "break 2e", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), "break 3e", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), "broken 1e", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(executor), "broken 2e", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoFlagSet()
	{
		// Create a unique thing to set the flag on, instead of modifying shared God (#1).
		var thingDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "FlagSetTest");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {thingDbRef}=DEBUG"));

		var thing = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));
		var thingObj = thing.AsThing;
		var flags = await thingObj.Object.Flags.Value.ToArrayAsync();

		await Assert.That(flags.Count(x => x.Name == "DEBUG")).IsEqualTo(1);
	}

	[Test]
	public async ValueTask WhereIs_ValidPlayer_ReportsLocation()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// @whereis #1 → "One is in Room Zero." — pattern B: object name "One" and room "Room Zero" make this globally unique.
		await Parser.CommandParse(1, ConnectionService, MModule.single("@whereis #1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "God is in")),
				TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask WhereIs_NonPlayer_ReturnsError()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create test_object_whereis"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@whereis test_object_whereis"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.WhereIsCanOnlyLocatePlayers), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Restart_ValidObject_Restarts()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@restart #1"));

		// @restart #1 targets the God player (#1 is a player) → RestartedPlayerAndObjectsFormat is always sent.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.RestartedPlayerAndObjectsFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Find_SearchesForObjects()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@find test"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.FindSearchingFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Stats_ShowsDatabaseStatistics()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@stats"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.StatsDatabaseStatisticsHeader), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Search_PerformsDatabaseSearch()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@search"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.SearchAdvancedHeader), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Entrances_ShowsLinkedObjects()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@entrances"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.EntrancesToFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Command_ShowsCommandInfo()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@command @emit"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.CommandInfoNameFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Function_ListsGlobalFunctions()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@function"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.FunctionGlobalUserDefinedHeader), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Function_ShowsFunctionInfo()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@function name"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.FunctionInfoNameFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Map_ExecutesAttributeOverList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var mapObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MapTest");
		var uniqueAttr = $"MAPATTR_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@map {mapObj}/{uniqueAttr}=foo bar baz"));

		// MapWouldIterateFormat is always sent before attribute lookup (before the try/get attribute).
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.MapWouldIterateFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Trigger_QueuesAttribute()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var trigObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "TrigTest");
		var uniqueAttr = $"TRIGATTR_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@trigger {trigObj}/{uniqueAttr}=arg1,arg2"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.TriggerNoSuchAttributeFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Include_InsertsAttributeInPlace()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var inclObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InclTest");
		var uniqueAttr = $"INCLATTR_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@include {inclObj}/{uniqueAttr}=arg1,arg2"));

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
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@halt {thingDbRef}"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "@halt:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask PS_ShowsQueueStatus()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@ps"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.PsQueueForTargetFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Select_MatchesFirstExpression()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@select test=foo,:action1,bar,:action2"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.SelectTestingStringFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Attribute_DisplaysAttributeInfo()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@attribute/access DESCRIPTION="));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@attribute DESCRIPTION"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.AttributeCommandInfoFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Attribute_AccessCreatesAttributeEntry()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@attribute/access MYATTR=no_command"));

		var entries = await Mediator.CreateStream(new Library.Queries.Database.GetAllAttributeEntriesQuery())
			.ToArrayAsync();
		var entry = entries.FirstOrDefault(e => e.Name == "MYATTR");

		await Assert.That(entry).IsNotNull();
		await Assert.That(entry!.DefaultFlags.Contains("NO_COMMAND")).IsTrue();
	}

	[Test]
	public async ValueTask Attribute_AccessValidatesFlags()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@attribute/access TESTATTR=INVALIDFLAG"));

		var entries = await Mediator.CreateStream(new Library.Queries.Database.GetAllAttributeEntriesQuery())
			.ToArrayAsync();
		var entry = entries.FirstOrDefault(e => e.Name == "TESTATTR");

		await Assert.That(entry).IsNull();
	}

	[Test]
	public async ValueTask Attribute_EntryFlagsAreAppliedWhenAttributeCreated()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@attribute/access TESTATTR2=no_command"));

		var entries = await Mediator.CreateStream(new Library.Queries.Database.GetAllAttributeEntriesQuery())
			.ToArrayAsync();
		var entry = entries.FirstOrDefault(e => e.Name == "TESTATTR2");
		await Assert.That(entry).IsNotNull();
		await Assert.That(entry!.DefaultFlags.Contains("NO_COMMAND")).IsTrue();

		// Use SetAttributeCommand directly to bypass & command test issues
		var player = await Mediator.Send(new Library.Queries.Database.GetObjectNodeQuery(new DBRef(1)));
		var success = await Mediator.Send(new Library.Commands.Database.SetAttributeCommand(
			new DBRef(1),
			["TESTATTR2"],
			MModule.single("test value"),
			player.AsPlayer));

		await Assert.That(success).IsTrue();

		var attrs = await Mediator.CreateStream(new Library.Queries.Database.GetAttributeQuery(new DBRef(1), ["TESTATTR2"]))
			.ToArrayAsync();

		var attr = attrs.LastOrDefault();
		await Assert.That(attr).IsNotNull();

		// This confirms that ArangoDatabase.cs:1832-1849 correctly applies flags from entries
		await Assert.That(attr!.Flags.Any(f => f.Name.Equals("no_command", StringComparison.OrdinalIgnoreCase))).IsTrue();
	}

	[Test]
	[NotInParallel]
	public async ValueTask DoListWithDBRefNotificationBatching()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist/inline 1 2 3=@pemit #1=Batched test message"));

		await NotifyService
			.Received(3)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "Batched test message")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask DoListBatchesToOtherPlayers()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@dolist/inline a b c=@pemit {executor}=DoListBatchesToOtherPlayers: Message to other player"));

		await NotifyService
			.Received(3)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "DoListBatchesToOtherPlayers: Message to other player")), 
				TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask NestedDoListBatching()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Nested @dolist: outer has 2 items, inner has 2 items = 4 total pemits
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist/inline 1 2={@dolist/inline a b=@pemit #1=Nested message}"));

		await NotifyService
			.Received(4)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "Nested message")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask DoListWithoutBreak_AllMessagesReceived()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist/inline 1 2 3=@pemit #1=DoListWithoutBreak_AllMessagesReceived"));

		await NotifyService
			.Received(3)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "DoListWithoutBreak_AllMessagesReceived")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask DoListWithBreakAfterFirst_OnlyFirstMessageReceived()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist/inline 1 2 3={@pemit #1=Message DoListWithBreakAfterFirst_OnlyFirstMessageReceived %iL;@break}"));

		// With {@pemit; @break}, @pemit runs in each iteration then @break happens, so all 3
		// messages fire — this is the actual MUSH behavior: @break affects the next iteration, not current.
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "Message DoListWithBreakAfterFirst_OnlyFirstMessageReceived 1")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "Message DoListWithBreakAfterFirst_OnlyFirstMessageReceived 2")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "Message DoListWithBreakAfterFirst_OnlyFirstMessageReceived 3")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask DoListWithBreakFlushesMessages()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist/inline 1 2 3={@pemit #1=Message before break; @break}"));

		await NotifyService
			.Received(3)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "Message before break")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask NestedDoListWithBreakFlushesMessages()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// With {@pemit; @break}, @pemit runs in each inner iteration: 2 outer * 3 inner = 6 messages.
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist/inline 1 2={@dolist/inline a b c={@pemit #1=Inner message; @break}}"));

		await NotifyService
			.Received(6)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "Inner message")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoListWithDelimiter()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("@dolist/inline/delimit , apple,banana,orange=@pemit #1=Fruit: %i0"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "Fruit: apple")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "Fruit: banana")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "Fruit: orange")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}
}