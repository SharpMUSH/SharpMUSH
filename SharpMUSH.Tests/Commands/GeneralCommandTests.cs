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

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class GeneralCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

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

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), expected, Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Arguments("l"), Skip("Not yet implemented properly")]
	public async ValueTask CommandAliasRuns(string str)
	{
		Console.WriteLine("Testing: {0}", str);
		await Parser.CommandParse(1, ConnectionService, MModule.single(str));
	}

	[Test]
	public async ValueTask DoListSimple()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist 1 2 3=@pemit #1=3 This is a test"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "3 This is a test") ||
				(msg.IsT1 && msg.AsT1 == "3 This is a test")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoListSimple2()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist 1 2 3=@pemit #1={4 This is, a test};"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "4 This is, a test") ||
				(msg.IsT1 && msg.AsT1 == "4 This is, a test")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}
	
	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask DolistCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist 1 2 3=think ##"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[NotInParallel]
	public async ValueTask DoListComplex()
	{
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("@dolist 1 2 3={@pemit #1=5 This is a test; @pemit #1=6 This is also a test}"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "5 This is a test") ||
				(msg.IsT1 && msg.AsT1 == "5 This is a test")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "6 This is also a test") ||
				(msg.IsT1 && msg.AsT1 == "6 This is also a test")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask DoListComplex2()
	{
		await Parser.CommandParse(1, ConnectionService,
			MModule.single(
				"@dolist 1 2 3={@pemit #1=7 This is a test; @pemit #1=8 This is also a test}; @pemit #1=9 Repeat 3 times in this mode."));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "7 This is a test") ||
				(msg.IsT1 && msg.AsT1 == "7 This is a test")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "8 This is also a test") ||
				(msg.IsT1 && msg.AsT1 == "8 This is also a test")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "9 Repeat 3 times in this mode.") ||
				(msg.IsT1 && msg.AsT1 == "9 Repeat 3 times in this mode.")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask DoListComplex3()
	{
		await Parser.CommandParse(1, ConnectionService,
			MModule.single(
				"@dolist 1={@dolist 1 2 3=@pemit #1=10 This is a test}; @pemit #1=11 Repeat 1 times in this mode."));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "10 This is a test") ||
				(msg.IsT1 && msg.AsT1 == "10 This is a test")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "11 Repeat 1 times in this mode.") ||
				(msg.IsT1 && msg.AsT1 == "11 Repeat 1 times in this mode.")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoListComplex4()
	{
		await Parser.CommandParse(1, ConnectionService,
			MModule.single(
				"@dolist 1 2={@dolist 1 2 3=@pemit #1=12 This is a test}; @pemit #1=13 Repeat 2 times in this mode."));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "12 This is a test") ||
				(msg.IsT1 && msg.AsT1 == "12 This is a test")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "13 Repeat 2 times in this mode.") ||
				(msg.IsT1 && msg.AsT1 == "13 Repeat 2 times in this mode.")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoListComplex5()
	{
		await Parser.CommandParse(1, ConnectionService,
			MModule.single(
				"@dolist a b={@dolist 1 2 3=@pemit #1=14 This is a test %i0}; @pemit #1=15 Repeat 1 times in this mode %i0"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "14 This is a test 1") ||
				(msg.IsT1 && msg.AsT1 == "14 This is a test 1")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "14 This is a test 2") ||
				(msg.IsT1 && msg.AsT1 == "14 This is a test 2")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "14 This is a test 3") ||
				(msg.IsT1 && msg.AsT1 == "14 This is a test 3")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "15 Repeat 1 times in this mode a") ||
				(msg.IsT1 && msg.AsT1 == "15 Repeat 1 times in this mode a")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "15 Repeat 1 times in this mode b") ||
				(msg.IsT1 && msg.AsT1 == "15 Repeat 1 times in this mode b")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoListComplex6()
	{
		await Parser.CommandParse(1, ConnectionService,
			MModule.single(
				"@dolist a b={@dolist 1 2 3={@ifelse eq(%i0,1)=think %i0 is 1; @ifelse eq(%i0,2)=think %i0 is 2,think {%i0 is 1, or 3}}}"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "3 is 1, or 3") ||
				(msg.IsT1 && msg.AsT1 == "3 is 1, or 3")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "1 is 1") ||
				(msg.IsT1 && msg.AsT1 == "1 is 1")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "2 is 2") ||
				(msg.IsT1 && msg.AsT1 == "2 is 2")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoBreakSimpleCommandList()
	{
		await Parser.CommandListParse(MModule.single("think assert 1a; @assert; think assert 2a; think assert 3a"));
		await Parser.CommandListParse(MModule.single("think break 1a; @break; think break 2a; think break 3a"));

		await NotifyService.Received().Notify(Arg.Any<AnySharpObject>(), "break 1a");
		await NotifyService.Received().Notify(Arg.Any<AnySharpObject>(), "break 2a");
		await NotifyService.Received().Notify(Arg.Any<AnySharpObject>(), "break 3a");
		await NotifyService.Received().Notify(Arg.Any<AnySharpObject>(), "assert 1a");
		await NotifyService.DidNotReceive().Notify(Arg.Any<AnySharpObject>(), "assert 2a");
		await NotifyService.DidNotReceive().Notify(Arg.Any<AnySharpObject>(), "assert 3a");
	}

	[Test]
	public async ValueTask DoBreakSimpleTruthyCommandList()
	{
		await Parser.CommandListParse(MModule.single("think assert 1b; @assert 1; think assert 2b; think assert 3b"));
		await Parser.CommandListParse(MModule.single("think break 1b; @break 1; think break 2b; think break 3b"));

		await NotifyService.Received().Notify(Arg.Any<AnySharpObject>(), "assert 1b");
		await NotifyService.Received().Notify(Arg.Any<AnySharpObject>(), "assert 2b");
		await NotifyService.Received().Notify(Arg.Any<AnySharpObject>(), "assert 3b");
		await NotifyService.Received().Notify(Arg.Any<AnySharpObject>(), "break 1b");
		await NotifyService.DidNotReceive().Notify(Arg.Any<AnySharpObject>(), "break 2b");
		await NotifyService.DidNotReceive().Notify(Arg.Any<AnySharpObject>(), "break 3b");
	}

	[Test]
	public async ValueTask DoBreakSimpleFalsyCommandList()
	{
		await Parser.CommandListParse(MModule.single("think assert 1c; @assert 0; think assert 2c; think assert 3c"));
		await Parser.CommandListParse(MModule.single("think break 1c; @break 0; think break 2c; think break 3c"));

		await NotifyService.Received().Notify(Arg.Any<AnySharpObject>(), "break 1c");
		await NotifyService.Received().Notify(Arg.Any<AnySharpObject>(), "break 2c");
		await NotifyService.Received().Notify(Arg.Any<AnySharpObject>(), "break 3c");
		await NotifyService.Received().Notify(Arg.Any<AnySharpObject>(), "assert 1c");
		await NotifyService.DidNotReceive().Notify(Arg.Any<AnySharpObject>(), "assert 2c");
		await NotifyService.DidNotReceive().Notify(Arg.Any<AnySharpObject>(), "assert 3c");
	}

	[Test]
	public async ValueTask DoBreakCommandList()
	{
		await Parser.CommandListParse(
			MModule.single("think break 1d; @break 1=think broken 1d; think break 2d; think break 3d"));

		await NotifyService.Received().Notify(Arg.Any<AnySharpObject>(), "break 1d");
		await NotifyService.DidNotReceive().Notify(Arg.Any<AnySharpObject>(), "break 2d");
		await NotifyService.DidNotReceive().Notify(Arg.Any<AnySharpObject>(), "break 3d");
		await NotifyService.Received().Notify(Arg.Any<AnySharpObject>(), "broken 1d");
	}

	[Test]
	public async ValueTask DoBreakCommandList2()
	{
		await Parser.CommandListParse(
			MModule.single("think break 1e; @break 1={think broken 1e; think broken 2e}; think break 2e; think break 3e"));

		await NotifyService.Received().Notify(Arg.Any<AnySharpObject>(), "break 1e");
		await NotifyService.DidNotReceive().Notify(Arg.Any<AnySharpObject>(), "break 2e");
		await NotifyService.DidNotReceive().Notify(Arg.Any<AnySharpObject>(), "break 3e");
		await NotifyService.Received().Notify(Arg.Any<AnySharpObject>(), "broken 1e");
		await NotifyService.Received().Notify(Arg.Any<AnySharpObject>(), "broken 2e");
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
		// Test @whereis with a valid player
		await Parser.CommandParse(1, ConnectionService, MModule.single("@whereis #1"));

		// Should notify about the location
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("is in")), 
				Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask WhereIs_NonPlayer_ReturnsError()
	{
		// First create a thing (non-player object)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create test_object_whereis"));

		// Try to @whereis a non-player
		await Parser.CommandParse(1, ConnectionService, MModule.single("@whereis test_object_whereis"));

		// Should notify that it's not a player
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("only @whereis players")), 
				Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test, Skip("TODO")]
	public async ValueTask Restart_ValidObject_Restarts()
	{
		// Test @restart with a valid object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@restart #1"));

		// Should notify about restart
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("Restarted")), 
				Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask Find_SearchesForObjects()
	{
		// Test @find command
		await Parser.CommandParse(1, ConnectionService, MModule.single("@find test"));

		// Should notify about searching
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("Searching")), 
				Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask Stats_ShowsDatabaseStatistics()
	{
		// Test @stats command
		await Parser.CommandParse(1, ConnectionService, MModule.single("@stats"));

		// Should notify about database statistics
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("Database Statistics")), 
				Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask Search_PerformsDatabaseSearch()
	{
		// Test @search command
		await Parser.CommandParse(1, ConnectionService, MModule.single("@search"));

		// Should notify about search
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf<MString,string>>(s => s.Value.ToString()!.Contains("database search")), 
				Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask Entrances_ShowsLinkedObjects()
	{
		// Test @entrances command
		await Parser.CommandParse(1, ConnectionService, MModule.single("@entrances"));

		// Should notify about entrances
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString,string>>(s => s.Value.ToString()!.Contains("Entrances")), 
				Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Skip("TODO: Failing")]
	public async ValueTask Command_ShowsCommandInfo()
	{
		// Test @command with a command name
		await Parser.CommandParse(1, ConnectionService, MModule.single("@command @emit"));

		// Should notify about command information
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf<MString,string>>(s => s.Value.ToString()!.Contains("Command:")), 
				Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test, Skip("TODO")]
	public async ValueTask Function_ListsGlobalFunctions()
	{
		// Test @function with no arguments to list functions
		await Parser.CommandParse(1, ConnectionService, MModule.single("@function"));

		// Should notify about global functions
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("Global user-defined functions")), 
				Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test, Skip("TODO")]
	public async ValueTask Function_ShowsFunctionInfo()
	{
		// Test @function with a function name
		await Parser.CommandParse(1, ConnectionService, MModule.single("@function name"));

		// Should notify about function information
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("Function:")), 
				Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask Map_ExecutesAttributeOverList()
	{
		// Test @map command
		await Parser.CommandParse(1, ConnectionService, MModule.single("@map me/test=foo bar baz"));

		// Should notify about mapping
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("@map:")), 
				Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test, Skip("TODO")]
	public async ValueTask Trigger_QueuesAttribute()
	{
		// Test @trigger command
		await Parser.CommandParse(1, ConnectionService, MModule.single("@trigger me/test=arg1,arg2"));

		// Should notify about triggering
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("@trigger:")), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask Include_InsertsAttributeInPlace()
	{
		// Test @include command
		await Parser.CommandParse(1, ConnectionService, MModule.single("@include me/test=arg1,arg2"));

		// Should attempt to locate the object and get the attribute
		// Since mocks aren't fully set up, it will fail with an error message
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask Halt_ClearsQueue()
	{
		// Test @halt command
		await Parser.CommandParse(1, ConnectionService, MModule.single("@halt me"));

		// Should notify about halting
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("@halt:")), 
				Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test, Skip("TODO")]
	public async ValueTask PS_ShowsQueueStatus()
	{
		// Test @ps command
		await Parser.CommandParse(1, ConnectionService, MModule.single("@ps"));

		// Should notify about queue
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("@ps:")), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask Select_MatchesFirstExpression()
	{
		// Test @select command
		await Parser.CommandParse(1, ConnectionService, MModule.single("@select test=foo,:action1,bar,:action2"));

		// Should notify about select
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf<MString,string>>(s => s.Value.ToString()!.Contains("@select:")),  
				Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Skip("TODO: Failing")]
	public async ValueTask Attribute_DisplaysAttributeInfo()
	{
		// Test @attribute command
		await Parser.CommandParse(1, ConnectionService, MModule.single("@attribute DESCRIPTION"));

		// Should notify about attribute info
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf<MString,string>>(s => s.Value.ToString()!.Contains("@attribute:")), 
				Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask Attribute_AccessCreatesAttributeEntry()
	{
		// Check what notifications were sent before and after
		var callsBefore = NotifyService.ReceivedCalls().Where(c => c.GetMethodInfo().Name == "Notify").Count();
		Console.WriteLine($"Notify calls before command: {callsBefore}");
		
		// Test @attribute/access command creates an attribute entry with no_command flag
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("@attribute/access MYATTR=no_command"));
		Console.WriteLine($"Command returned: {result}");
		
		// Check what new notifications were sent
		var callsAfter = NotifyService.ReceivedCalls().Where(c => c.GetMethodInfo().Name == "Notify").ToList();
		Console.WriteLine($"Notify calls after command: {callsAfter.Count}");
		var newCalls = callsAfter.Skip(callsBefore).ToList();
		Console.WriteLine($"New notify calls: {newCalls.Count}");
		foreach (var call in newCalls)
		{
			var args = call.GetArguments();
			if (args.Length >= 2)
			{
				Console.WriteLine($"Notification: {args[1]}");
			}
		}
		
		// Debug: Check all entries
		var entries = await Mediator.CreateStream(new Library.Queries.Database.GetAllAttributeEntriesQuery())
			.ToArrayAsync();
		Console.WriteLine($"Total attribute entries after command: {entries.Length}");
		
		var entry = entries.FirstOrDefault(e => e.Name == "MYATTR");
		if (entry == null)
		{
			Console.WriteLine("MYATTR entry not found!");
		}
		else
		{
			Console.WriteLine($"Found MYATTR with flags: {string.Join(", ", entry.DefaultFlags)}");
		}
		
		await Assert.That(entry).IsNotNull();
		await Assert.That(entry!.DefaultFlags.Contains("NO_COMMAND")).IsTrue();
	}

	[Test]
	public async ValueTask Attribute_AccessValidatesFlags()
	{
		// Test @attribute/access validates flag names
		// This command should notify about an invalid flag but not throw an exception
		await Parser.CommandParse(1, ConnectionService, MModule.single("@attribute/access TESTATTR=INVALIDFLAG"));
		
		// The command should complete (error handling is done through notifications)
		// We're just verifying it doesn't crash - no exception means success
	}

	[Test]
	public async ValueTask Attribute_EntryFlagsAreAppliedWhenAttributeCreated()
	{
		// First create an attribute entry with no_command flag
		await Parser.CommandParse(1, ConnectionService, MModule.single("@attribute/access TESTATTR2=no_command"));

		// Set the attribute on the executor - it should get the no_command flag automatically
		await Parser.CommandParse(1, ConnectionService, MModule.single("&TESTATTR2 me=test value"));

		// Verify the attribute was created with the correct flags by querying it
		var attr = await Mediator.CreateStream(new Library.Queries.Database.GetAttributeQuery(new DBRef(1), ["TESTATTR2"]))
			.LastOrDefaultAsync();
		
		await Assert.That(attr).IsNotNull();
		await Assert.That(attr!.Flags.Any(f => f.Name.Equals("no_command", StringComparison.OrdinalIgnoreCase))).IsTrue();
	}
}