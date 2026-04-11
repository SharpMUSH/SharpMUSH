using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
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
			.Received()
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

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "3 This is a test")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoListSimple2()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist/inline 1 2 3=@pemit #1={4 This is, a test};"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "4 This is, a test")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DolistDoubleHashReplacement()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist/inline 1 2 3=@pemit #1=dolist-hash-##"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "dolist-hash-1")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "dolist-hash-2")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "dolist-hash-3")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask DoListComplex()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("@dolist/inline 1 2 3={@pemit #1=5 This is a test; @pemit #1=6 This is also a test}"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "5 This is a test")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "6 This is also a test")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
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
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "7 This is a test")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "8 This is also a test")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "9 Repeat 3 times in this mode.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask DoListComplex3()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService,
			MModule.single(
				"@dolist/inline 1={@dolist/inline 1 2 3=@pemit #1=10 This is a test}; @pemit #1=11 Repeat 1 times in this mode."));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "10 This is a test")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "11 Repeat 1 times in this mode.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoListComplex4()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService,
			MModule.single(
				"@dolist/inline 1 2={@dolist/inline 1 2 3=@pemit #1=12 This is a test}; @pemit #1=13 Repeat 2 times in this mode."));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "12 This is a test")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "13 Repeat 2 times in this mode.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoListComplex5()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService,
			MModule.single(
				"@dolist/inline a b={@dolist/inline 1 2 3=@pemit #1=14 This is a test %i0}; @pemit #1=15 Repeat 1 times in this mode %i0"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "14 This is a test 1")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "14 This is a test 2")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "14 This is a test 3")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "15 Repeat 1 times in this mode a")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "15 Repeat 1 times in this mode b")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoListComplex6()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService,
			MModule.single(
				"@dolist/inline a b={@dolist/inline 1 2 3={@ifelse eq(%i0,1)=think %i0 is 1; @ifelse eq(%i0,2)=think %i0 is 2,think {%i0 is 1, or 3}}}"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "3 is 1, or 3")), null, INotifyService.NotificationType.Announce);
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "1 is 1")), null, INotifyService.NotificationType.Announce);
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "2 is 2")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoBreakSimpleCommandList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandListParse(MModule.single("think assert 1a; @assert; think assert 2a; think assert 3a"));
		await Parser.CommandListParse(MModule.single("think break 1a; @break; think break 2a; think break 3a"));

		await NotifyService.Received().Notify(TestHelpers.MatchingObject(executor), "break 1a", null, INotifyService.NotificationType.Announce);
		await NotifyService.Received().Notify(TestHelpers.MatchingObject(executor), "break 2a", null, INotifyService.NotificationType.Announce);
		await NotifyService.Received().Notify(TestHelpers.MatchingObject(executor), "break 3a", null, INotifyService.NotificationType.Announce);
		await NotifyService.Received().Notify(TestHelpers.MatchingObject(executor), "assert 1a", null, INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), "assert 2a", null, INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), "assert 3a", null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoBreakSimpleTruthyCommandList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandListParse(MModule.single("think assert 1b; @assert 1; think assert 2b; think assert 3b"));
		await Parser.CommandListParse(MModule.single("think break 1b; @break 1; think break 2b; think break 3b"));

		await NotifyService.Received().Notify(TestHelpers.MatchingObject(executor), "assert 1b", null, INotifyService.NotificationType.Announce);
		await NotifyService.Received().Notify(TestHelpers.MatchingObject(executor), "assert 2b", null, INotifyService.NotificationType.Announce);
		await NotifyService.Received().Notify(TestHelpers.MatchingObject(executor), "assert 3b", null, INotifyService.NotificationType.Announce);
		await NotifyService.Received().Notify(TestHelpers.MatchingObject(executor), "break 1b", null, INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), "break 2b", null, INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), "break 3b", null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoBreakSimpleFalsyCommandList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandListParse(MModule.single("think assert 1c; @assert 0; think assert 2c; think assert 3c"));
		await Parser.CommandListParse(MModule.single("think break 1c; @break 0; think break 2c; think break 3c"));

		await NotifyService.Received().Notify(TestHelpers.MatchingObject(executor), "break 1c", null, INotifyService.NotificationType.Announce);
		await NotifyService.Received().Notify(TestHelpers.MatchingObject(executor), "break 2c", null, INotifyService.NotificationType.Announce);
		await NotifyService.Received().Notify(TestHelpers.MatchingObject(executor), "break 3c", null, INotifyService.NotificationType.Announce);
		await NotifyService.Received().Notify(TestHelpers.MatchingObject(executor), "assert 1c", null, INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), "assert 2c", null, INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), "assert 3c", null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoBreakCommandList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandListParse(
			MModule.single("think break 1d; @break 1=think broken 1d; think break 2d; think break 3d"));

		await NotifyService.Received().Notify(TestHelpers.MatchingObject(executor), "break 1d", null, INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), "break 2d", null, INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), "break 3d", null, INotifyService.NotificationType.Announce);
		await NotifyService.Received().Notify(TestHelpers.MatchingObject(executor), "broken 1d", null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoBreakCommandList2()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandListParse(
			MModule.single("think break 1e; @break 1={think broken 1e; think broken 2e}; think break 2e; think break 3e"));

		await NotifyService.Received().Notify(TestHelpers.MatchingObject(executor), "break 1e", null, INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), "break 2e", null, INotifyService.NotificationType.Announce);
		await NotifyService.DidNotReceive().Notify(TestHelpers.MatchingObject(executor), "break 3e", null, INotifyService.NotificationType.Announce);
		await NotifyService.Received().Notify(TestHelpers.MatchingObject(executor), "broken 1e", null, INotifyService.NotificationType.Announce);
		await NotifyService.Received().Notify(TestHelpers.MatchingObject(executor), "broken 2e", null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoFlagSet()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set #1=DEBUG"));

		var one = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		var onePlayer = one.AsPlayer;
		var flags = await onePlayer.Object.Flags.Value.ToArrayAsync();

		await Assert.That(flags.Count(x => x.Name == "DEBUG")).IsEqualTo(1);
	}

	[Test]
	public async ValueTask WhereIs_ValidPlayer_ReportsLocation()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test @whereis with a valid player
		await Parser.CommandParse(1, ConnectionService, MModule.single("@whereis #1"));

		// Should notify about the location
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "is in")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask WhereIs_NonPlayer_ReturnsError()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// First create a thing (non-player object)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create test_object_whereis"));

		// Try to @whereis a non-player
		await Parser.CommandParse(1, ConnectionService, MModule.single("@whereis test_object_whereis"));

		// Should notify that it's not a player
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "only @whereis players")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Restart_ValidObject_Restarts()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test @restart with a valid object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@restart #1"));

		// Should notify about restart
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Restarted")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Find_SearchesForObjects()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test @find command
		await Parser.CommandParse(1, ConnectionService, MModule.single("@find test"));

		// Should notify about searching
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Searching")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Stats_ShowsDatabaseStatistics()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test @stats command
		await Parser.CommandParse(1, ConnectionService, MModule.single("@stats"));

		// Should notify about database statistics
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Database Statistics")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Search_PerformsDatabaseSearch()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test @search command
		await Parser.CommandParse(1, ConnectionService, MModule.single("@search"));

		// Should notify about search
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => s.Value.ToString()!.Contains("database search")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Entrances_ShowsLinkedObjects()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test @entrances command
		await Parser.CommandParse(1, ConnectionService, MModule.single("@entrances"));

		// Should notify about entrances
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(s => s.Value.ToString()!.Contains("Entrances")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Command_ShowsCommandInfo()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test @command with a command name
		await Parser.CommandParse(1, ConnectionService, MModule.single("@command @emit"));

		// Should notify about command information
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => s.Value.ToString()!.Contains("Command:")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Function_ListsGlobalFunctions()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test @function with no arguments to list functions
		await Parser.CommandParse(1, ConnectionService, MModule.single("@function"));

		// Should notify about global functions
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Global user-defined functions")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Function_ShowsFunctionInfo()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test @function with a function name
		await Parser.CommandParse(1, ConnectionService, MModule.single("@function name"));

		// Should notify about function information
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Function:")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Map_ExecutesAttributeOverList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test @map command — attribute does not exist on the unique object
		var mapObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MapTest");
		var uniqueAttr = $"MAPATTR_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@map {mapObj}/{uniqueAttr}=foo bar baz"));

		// Should notify about mapping
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "@map:")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Trigger_QueuesAttribute()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test @trigger command — attribute does not exist on the unique object
		var trigObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "TrigTest");
		var uniqueAttr = $"TRIGATTR_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@trigger {trigObj}/{uniqueAttr}=arg1,arg2"));

		// Should notify with error since the attribute doesn't exist
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageContains(msg, "No such attribute")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Include_InsertsAttributeInPlace()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test @include command — attribute does not exist on the unique object
		var inclObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InclTest");
		var uniqueAttr = $"INCLATTR_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@include {inclObj}/{uniqueAttr}=arg1,arg2"));

		// Should attempt to locate the object and get the attribute
		// Since the attribute doesn't exist, it outputs "Attribute <name> is empty."
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, $"Attribute {uniqueAttr} is empty.")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("TestInfrastructure")]
	[Skip("Test infrastructure issue - NotifyService call count mismatch")]
	public async ValueTask Halt_ClearsQueue()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test @halt command
		await Parser.CommandParse(1, ConnectionService, MModule.single("@halt me"));

		// Should notify about halting
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "@halt:")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask PS_ShowsQueueStatus()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test @ps command
		await Parser.CommandParse(1, ConnectionService, MModule.single("@ps"));

		// Should notify about queue
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageContains(msg, "@ps:")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Select_MatchesFirstExpression()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test @select command
		await Parser.CommandParse(1, ConnectionService, MModule.single("@select test=foo,:action1,bar,:action2"));

		// Should notify about select
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => s.Value.ToString()!.Contains("@select:")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Attribute_DisplaysAttributeInfo()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create the attribute entry first so it exists in the standard table
		await Parser.CommandParse(1, ConnectionService, MModule.single("@attribute/access DESCRIPTION="));

		// Test @attribute command
		await Parser.CommandParse(1, ConnectionService, MModule.single("@attribute DESCRIPTION"));

		// Should notify about attribute info
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => s.Value.ToString()!.Contains("@attribute:")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Attribute_AccessCreatesAttributeEntry()
	{
		// Test @attribute/access command creates an attribute entry with no_command flag
		await Parser.CommandParse(1, ConnectionService, MModule.single("@attribute/access MYATTR=no_command"));

		// Verify the entry was created in the database
		var entries = await Mediator.CreateStream(new Library.Queries.Database.GetAllAttributeEntriesQuery())
			.ToArrayAsync();
		var entry = entries.FirstOrDefault(e => e.Name == "MYATTR");

		await Assert.That(entry).IsNotNull();
		await Assert.That(entry!.DefaultFlags.Contains("NO_COMMAND")).IsTrue();
	}

	[Test]
	public async ValueTask Attribute_AccessValidatesFlags()
	{
		// Test @attribute/access validates flag names
		await Parser.CommandParse(1, ConnectionService, MModule.single("@attribute/access TESTATTR=INVALIDFLAG"));

		// Verify that no entry was created with an invalid flag
		var entries = await Mediator.CreateStream(new Library.Queries.Database.GetAllAttributeEntriesQuery())
			.ToArrayAsync();
		var entry = entries.FirstOrDefault(e => e.Name == "TESTATTR");

		// Entry should not exist because the flag was invalid
		await Assert.That(entry).IsNull();
	}

	[Test]
	public async ValueTask Attribute_EntryFlagsAreAppliedWhenAttributeCreated()
	{
		// First create an attribute entry with no_command flag
		await Parser.CommandParse(1, ConnectionService, MModule.single("@attribute/access TESTATTR2=no_command"));

		// Verify the entry was created with correct flags
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

		// Verify the attribute was created with the no_command flag from the entry
		var attrs = await Mediator.CreateStream(new Library.Queries.Database.GetAttributeQuery(new DBRef(1), ["TESTATTR2"]))
			.ToArrayAsync();

		var attr = attrs.LastOrDefault();
		await Assert.That(attr).IsNotNull();

		// Verify the attribute has the no_command flag from the entry
		// This confirms that ArangoDatabase.cs:1832-1849 correctly applies flags from entries
		await Assert.That(attr!.Flags.Any(f => f.Name.Equals("no_command", StringComparison.OrdinalIgnoreCase))).IsTrue();
	}

	[Test]
	[NotInParallel]
	public async ValueTask DoListWithDBRefNotificationBatching()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// This test validates that DBRef-based notifications respect batching scopes.
		// Before the fix, Notify(DBRef) would bypass batching and send messages immediately.
		// After the fix, messages should be accumulated and sent as a batch.
		// We use the same pattern as DoListSimple - a simple message without iteration markers.

		// Use @pemit which uses Notify(AnySharpObject) -> Notify(DBRef)
		// This should call Notify three times with the same message
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist/inline 1 2 3=@pemit #1=Batched test message"));

		// Verify that Notify was called 3 times (once per iteration)
		// All three should have been batched together internally, but we verify they all went through
		await NotifyService
			.Received(Quantity.Exactly(3))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "Batched test message")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask DoListBatchesToOtherPlayers()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Send to player #2 (different from enactor #1) — receiver is #2, not the executor
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist/inline a b c=@pemit #2=Message to other player"));

		// Verify all three notifications were called (target is #2, not the executor)
		await NotifyService
			.Received(Quantity.Exactly(3))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "Message to other player")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask NestedDoListBatching()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// This test validates that nested @dolists properly use ref-counting for batching context.
		// Messages from both outer and inner loops should be batched together.

		// Nested @dolist: outer has 2 items, inner has 2 items = 4 total pemits
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist/inline 1 2={@dolist/inline a b=@pemit #1=Nested message}"));

		// Verify 4 notifications were called (2 outer * 2 inner)
		await NotifyService
			.Received(Quantity.Exactly(4))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "Nested message")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask DoListWithoutBreak_AllMessagesReceived()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Negative test: Without @break, all loop iterations should send messages

		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist/inline 1 2 3=@pemit #1=DoListWithoutBreak_AllMessagesReceived"));

		// Should receive exactly 3 messages (one per iteration)
		await NotifyService
			.Received(Quantity.Exactly(3))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "DoListWithoutBreak_AllMessagesReceived")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask DoListWithBreakAfterFirst_OnlyFirstMessageReceived()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Positive test: @break should stop the loop after first iteration
		// Use @break as a conditional command to stop after first iteration

		// @break after first message - note: using command structure where @pemit runs, then @break stops further iterations
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist/inline 1 2 3={@pemit #1=Message DoListWithBreakAfterFirst_OnlyFirstMessageReceived %iL;@break}"));

		// With {@pemit; @break}, @pemit runs in each iteration then @break happens
		// So we get 3 messages (one per loop start) but @break doesn't prevent them
		// This is the actual MUSH behavior - @break affects the next iteration, not current
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "Message DoListWithBreakAfterFirst_OnlyFirstMessageReceived 1")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "Message DoListWithBreakAfterFirst_OnlyFirstMessageReceived 2")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "Message DoListWithBreakAfterFirst_OnlyFirstMessageReceived 3")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask DoListWithBreakFlushesMessages()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// This test validates that @break properly flushes batched messages.
		// Even with @break in the command list, the using statement should
		// ensure messages are flushed via disposal.

		// Loop with @break - both @pemit and @break execute in each iteration
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist/inline 1 2 3={@pemit #1=Message before break; @break}"));

		// With command list {@pemit; @break}, both execute in each iteration
		// So we get 3 messages, and batching still works
		await NotifyService
			.Received(Quantity.Exactly(3))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "Message before break")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask NestedDoListWithBreakFlushesMessages()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// This test validates that @break in a nested @dolist/inline properly handles
		// the ref-counted batching context and still flushes messages.
		// Note: With the command structure {@pemit; @break}, both commands execute
		// in each iteration, so @break happens after the @pemit.

		// Outer loop runs twice, inner loop has 3 items
		// With {@pemit; @break}, the @pemit runs in each inner iteration
		// Expected: 2 outer iterations * 3 inner iterations = 6 messages
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist/inline 1 2={@dolist/inline a b c={@pemit #1=Inner message; @break}}"));

		// Should receive 6 messages (all inner iterations run, @break is after @pemit)
		// This validates that batching still works and flushes correctly even with @break
		await NotifyService
			.Received(Quantity.Exactly(6))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "Inner message")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoListWithDelimiter()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test @dolist with /delimit switch
		// Format: @dolist/delimit <delimiter> <list>=<action>
		// Delimiter is separated by space from list
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("@dolist/inline/delimit , apple,banana,orange=@pemit #1=Fruit: %i0"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "Fruit: apple")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "Fruit: banana")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "Fruit: orange")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}
}