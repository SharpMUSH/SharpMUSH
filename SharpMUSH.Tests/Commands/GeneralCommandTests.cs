using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class GeneralCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	
	[Test]
	[Arguments("@pemit #1=1 This is a test", "1 This is a test")]
	[Arguments("@pemit #1=2 This is a test;", "2 This is a test;")]
	public async ValueTask SimpleCommandParse(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		await Parser.CommandParse(1, MModule.single(str));

		await Parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), expected);
	}

	[Test]
	[Arguments("l"), Skip("Not yet implemented properly")]
	public async ValueTask CommandAliasRuns(string str)
	{
		Console.WriteLine("Testing: {0}", str);
		await Parser.CommandParse(1, MModule.single(str));
	}

	[Test]
	public async ValueTask DoListSimple()
	{
		await Parser.CommandParse(1, MModule.single("@dolist 1 2 3=@pemit #1=3 This is a test"));

		await Parser.NotifyService
			.Received(Quantity.Exactly(3))
			.Notify(Arg.Any<AnySharpObject>(), "3 This is a test");
	}

	[Test]
	public async ValueTask DoListSimple2()
	{
		await Parser.CommandParse(1, MModule.single("@dolist 1 2 3=@pemit #1={4 This is, a test};"));

		await Parser.NotifyService
			.Received(Quantity.Exactly(3))
			.Notify(Arg.Any<AnySharpObject>(), "4 This is, a test");
	}

	[Test]
	public async ValueTask DoListComplex()
	{
		await Parser.CommandParse(1,
			MModule.single("@dolist 1 2 3={@pemit #1=5 This is a test; @pemit #1=6 This is also a test}"));

		await Parser.NotifyService
			.Received(Quantity.Exactly(3))
			.Notify(Arg.Any<AnySharpObject>(), "5 This is a test");
		await Parser.NotifyService
			.Received(Quantity.Exactly(3))
			.Notify(Arg.Any<AnySharpObject>(), "6 This is also a test");
	}

	[Test]
	public async ValueTask DoListComplex2()
	{
		await Parser.CommandParse(1,
			MModule.single(
				"@dolist 1 2 3={@pemit #1=7 This is a test; @pemit #1=8 This is also a test}; @pemit #1=9 Repeat 3 times in this mode."));

		await Parser.NotifyService
			.Received(Quantity.Exactly(3))
			.Notify(Arg.Any<AnySharpObject>(), "7 This is a test");
		await Parser.NotifyService
			.Received(Quantity.Exactly(3))
			.Notify(Arg.Any<AnySharpObject>(), "8 This is also a test");
		await Parser.NotifyService
			.Received(Quantity.Exactly(3))
			.Notify(Arg.Any<AnySharpObject>(), "9 Repeat 3 times in this mode.");
	}

	[Test]
	public async ValueTask DoListComplex3()
	{
		await Parser.CommandParse(1,
			MModule.single(
				"@dolist 1={@dolist 1 2 3=@pemit #1=10 This is a test}; @pemit #1=11 Repeat 1 times in this mode."));

		await Parser.NotifyService
			.Received(Quantity.Exactly(3))
			.Notify(Arg.Any<AnySharpObject>(), "10 This is a test");
		await Parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "11 Repeat 1 times in this mode.");
	}

	[Test]
	public async ValueTask DoListComplex4()
	{
		await Parser.CommandParse(1,
			MModule.single(
				"@dolist 1 2={@dolist 1 2 3=@pemit #1=12 This is a test}; @pemit #1=13 Repeat 2 times in this mode."));

		await Parser.NotifyService
			.Received(Quantity.Exactly(6))
			.Notify(Arg.Any<AnySharpObject>(), "12 This is a test");
		await Parser.NotifyService
			.Received(Quantity.Exactly(2))
			.Notify(Arg.Any<AnySharpObject>(), "13 Repeat 2 times in this mode.");
	}

	[Test]
	public async ValueTask DoListComplex5()
	{
		await Parser.CommandParse(1,
			MModule.single(
				"@dolist a b={@dolist 1 2 3=@pemit #1=14 This is a test %i0}; @pemit #1=15 Repeat 1 times in this mode %i0"));

		await Parser.NotifyService
			.Received(Quantity.Exactly(2))
			.Notify(Arg.Any<AnySharpObject>(), "14 This is a test 1");
		await Parser.NotifyService
			.Received(Quantity.Exactly(2))
			.Notify(Arg.Any<AnySharpObject>(), "14 This is a test 2");
		await Parser.NotifyService
			.Received(Quantity.Exactly(2))
			.Notify(Arg.Any<AnySharpObject>(), "14 This is a test 3");
		await Parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "15 Repeat 1 times in this mode a");
		await Parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "15 Repeat 1 times in this mode b");
	}

	[Test]
	public async ValueTask DoListComplex6()
	{
		await Parser.CommandParse(1,
			MModule.single(
				"@dolist a b={@dolist 1 2 3={@ifelse eq(%i0,1)=think %i0 is 1; @ifelse eq(%i0,2)=think %i0 is 2,think {%i0 is 1, or 3}}}"));

		await Parser.NotifyService
			.Received(Quantity.Exactly(2))
			.Notify(Arg.Any<AnySharpObject>(), "3 is 1, or 3");
		await Parser.NotifyService
			.Received(Quantity.Exactly(2))
			.Notify(Arg.Any<AnySharpObject>(), "1 is 1");
		await Parser.NotifyService
			.Received(Quantity.Exactly(2))
			.Notify(Arg.Any<AnySharpObject>(), "2 is 2");
	}

	[Test]
	public async ValueTask DoDigForCommandListCheck()
	{
		await Parser.CommandParse(1, MModule.single("@dig Bar Room=Exit;ExitAlias,ExitBack;ExitAliasBack"));

		await Parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<DBRef>(), "Bar Room created with room number #4.");
		await Parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<DBRef>(), "Linked exit #5 to #4");
		await Parser.NotifyService
			.Received(Quantity.Exactly(2))
			.Notify(Arg.Any<DBRef>(), "Trying to link...");
		await Parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<DBRef>(), "Linked exit #6 to #0");
	}

	[Test]
	public async ValueTask DoBreakSimpleCommandList()
	{
		await Parser.CommandListParse(MModule.single("think assert 1a; @assert; think assert 2a; think assert 3a"));
		await Parser.CommandListParse(MModule.single("think break 1a; @break; think break 2a; think break 3a"));

		await Parser.NotifyService.Received(Quantity.Exactly(1)).Notify(Arg.Any<AnySharpObject>(), "break 1a");
		await Parser.NotifyService.Received(Quantity.Exactly(1)).Notify(Arg.Any<AnySharpObject>(), "break 2a");
		await Parser.NotifyService.Received(Quantity.Exactly(1)).Notify(Arg.Any<AnySharpObject>(), "break 3a");
		await Parser.NotifyService.Received(Quantity.Exactly(1)).Notify(Arg.Any<AnySharpObject>(), "assert 1a");
		await Parser.NotifyService.Received(Quantity.Exactly(0)).Notify(Arg.Any<AnySharpObject>(), "assert 2a");
		await Parser.NotifyService.Received(Quantity.Exactly(0)).Notify(Arg.Any<AnySharpObject>(), "assert 3a");
	}
	
	[Test]
	public async ValueTask DoBreakSimpleTruthyCommandList()
	{
		await Parser.CommandListParse(MModule.single("think assert 1b; @assert 1; think assert 2b; think assert 3b"));
		await Parser.CommandListParse(MModule.single("think break 1b; @break 1; think break 2b; think break 3b"));

		await Parser.NotifyService.Received(Quantity.Exactly(1)).Notify(Arg.Any<AnySharpObject>(), "assert 1b");
		await Parser.NotifyService.Received(Quantity.Exactly(1)).Notify(Arg.Any<AnySharpObject>(), "assert 2b");
		await Parser.NotifyService.Received(Quantity.Exactly(1)).Notify(Arg.Any<AnySharpObject>(), "assert 3b");
		await Parser.NotifyService.Received(Quantity.Exactly(1)).Notify(Arg.Any<AnySharpObject>(), "break 1b");
		await Parser.NotifyService.Received(Quantity.Exactly(0)).Notify(Arg.Any<AnySharpObject>(), "break 2b");
		await Parser.NotifyService.Received(Quantity.Exactly(0)).Notify(Arg.Any<AnySharpObject>(), "break 3b");
	}
	
	[Test]
	public async ValueTask DoBreakSimpleFalsyCommandList()
	{
		await Parser.CommandListParse(MModule.single("think assert 1c; @assert 0; think assert 2c; think assert 3c"));
		await Parser.CommandListParse(MModule.single("think break 1c; @break 0; think break 2c; think break 3c"));

		await Parser.NotifyService.Received(Quantity.Exactly(1)).Notify(Arg.Any<AnySharpObject>(), "break 1c");
		await Parser.NotifyService.Received(Quantity.Exactly(1)).Notify(Arg.Any<AnySharpObject>(), "break 2c");
		await Parser.NotifyService.Received(Quantity.Exactly(1)).Notify(Arg.Any<AnySharpObject>(), "break 3c");
		await Parser.NotifyService.Received(Quantity.Exactly(1)).Notify(Arg.Any<AnySharpObject>(), "assert 1c");
		await Parser.NotifyService.Received(Quantity.Exactly(0)).Notify(Arg.Any<AnySharpObject>(), "assert 2c");
		await Parser.NotifyService.Received(Quantity.Exactly(0)).Notify(Arg.Any<AnySharpObject>(), "assert 3c");
	}
	
	[Test]
	public async ValueTask DoBreakCommandList()
	{
		await Parser.CommandListParse(MModule.single("think break 1d; @break 1=think broken 1d; think break 2d; think break 3d"));

		await Parser.NotifyService.Received(Quantity.Exactly(1)).Notify(Arg.Any<AnySharpObject>(), "break 1d");
		await Parser.NotifyService.Received(Quantity.Exactly(0)).Notify(Arg.Any<AnySharpObject>(), "break 2d");
		await Parser.NotifyService.Received(Quantity.Exactly(0)).Notify(Arg.Any<AnySharpObject>(), "break 3d");
		await Parser.NotifyService.Received(Quantity.Exactly(1)).Notify(Arg.Any<AnySharpObject>(), "broken 1d");
	}
	
	[Test]
	public async ValueTask DoBreakCommandList2()
	{
		await Parser.CommandListParse(MModule.single("think break 1e; @break 1={think broken 1e; think broken 2e}; think break 2e; think break 3e"));

		await Parser.NotifyService.Received(Quantity.Exactly(1)).Notify(Arg.Any<AnySharpObject>(), "break 1e");
		await Parser.NotifyService.Received(Quantity.Exactly(0)).Notify(Arg.Any<AnySharpObject>(), "break 2e");
		await Parser.NotifyService.Received(Quantity.Exactly(0)).Notify(Arg.Any<AnySharpObject>(), "break 3e");
		await Parser.NotifyService.Received(Quantity.Exactly(1)).Notify(Arg.Any<AnySharpObject>(), "broken 1e");
		await Parser.NotifyService.Received(Quantity.Exactly(1)).Notify(Arg.Any<AnySharpObject>(), "broken 2e");
	}

	[Test, DependsOn(nameof(DoDigForCommandListCheck))]
	public async ValueTask DoDigForCommandListCheck2()
	{
		await Parser.CommandListParse(MModule.single("@dig Foo Room={Exit;ExitAlias},{ExitBack;ExitAliasBack}"));

		await Parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<DBRef>(), "Foo Room created with room number #7.");
		await Parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<DBRef>(), "Linked exit #8 to #7");
		await Parser.NotifyService
			.Received(Quantity.Exactly(4))
			.Notify(Arg.Any<DBRef>(), "Trying to link...");
		await Parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<DBRef>(), "Linked exit #9 to #0");
	}
	

	[Test]
	[DependsOn(nameof(DoDigForCommandListCheck2))]
	public async Task DigAndMoveTest()
	{
		if(Parser is null) throw new Exception("Parser is null");
		await Parser.CommandParse(1, MModule.single("@dig NewRoom=Forward;F,Backward;B"));
		await Parser.CommandParse(1, MModule.single("think %l start"));
		await Parser.CommandParse(1, MModule.single("goto Forward"));
		await Parser.CommandParse(1, MModule.single("think %l forward"));
		await Parser.CommandParse(1, MModule.single("goto Backward"));
		await Parser.CommandParse(1, MModule.single("think %l back"));
		
		await Parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "#0 start");
		await Parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "#10 forward");
		await Parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "#0 back");
	}

	[Test]	 
	public async ValueTask DoFlagSet()
	{
		await Parser.CommandParse(1, MModule.single("@set #1=DEBUG"));

		var one = await Parser.Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		var onePlayer = one.AsPlayer;
		var flags = await onePlayer.Object.Flags.WithCancellation(CancellationToken.None);

		await Assert.That(flags.Count(x => x.Name == "DEBUG")).IsEqualTo(1);
	}
}