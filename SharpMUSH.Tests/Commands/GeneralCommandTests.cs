using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Commands;

public class GeneralCommandTests : BaseUnitTest
{
	private static readonly IMUSHCodeParser Parser = TestParser(ns: Substitute.For<INotifyService>())
		.ConfigureAwait(false)
		.GetAwaiter()
		.GetResult();

	[Test]
	[Arguments("@pemit #1=1 This is a test", "1 This is a test")]
	[Arguments("@pemit #1=2 This is a test;", "2 This is a test;")]
	public async ValueTask SimpleCommandParse(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		await Parser.CommandParse("1", MModule.single(str));

		await Parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), expected);
	}

	[Test]
	[Arguments("l"), Skip("Not yet implemented properly")]
	public async ValueTask CommandAliasRuns(string str)
	{
		Console.WriteLine("Testing: {0}", str);
		await Parser.CommandParse("1", MModule.single(str));
	}

	[Test]
	public async ValueTask DoListSimple()
	{
		await Parser.CommandParse("1", MModule.single("@dolist 1 2 3=@pemit #1=3 This is a test"));

		await Parser.NotifyService
			.Received(Quantity.Exactly(3))
			.Notify(Arg.Any<AnySharpObject>(), "3 This is a test");
	}

	[Test]
	public async ValueTask DoListSimple2()
	{
		await Parser.CommandParse("1", MModule.single("@dolist 1 2 3=@pemit #1={4 This is, a test};"));

		await Parser.NotifyService
			.Received(Quantity.Exactly(3))
			.Notify(Arg.Any<AnySharpObject>(), "4 This is, a test");
	}

	[Test]
	public async ValueTask DoListComplex()
	{
		await Parser.CommandParse("1",
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
		await Parser.CommandParse("1",
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
		await Parser.CommandParse("1",
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
		await Parser.CommandParse("1",
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
		await Parser.CommandParse("1",
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
		await Parser.CommandParse("1",
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
	public async ValueTask DoDigForCommandlistCheck()
	{
		await Parser.CommandParse("1", MModule.single("@dig Bar Room=Exit;ExitAlias,ExitBack;ExitAliasBack"));

		await Parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<DBRef>(), "Bar Room created with room number #3.");
		await Parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<DBRef>(), "Linked exit #4 to #3");
		await Parser.NotifyService
			.Received(Quantity.Exactly(2))
			.Notify(Arg.Any<DBRef>(), "Trying to link...");
		await Parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<DBRef>(), "Linked exit #5 to #0");
	}

	[Test, DependsOn(nameof(DoDigForCommandlistCheck))]
	public async ValueTask DoDigForCommandlistCheck2()
	{
		await Parser.CommandListParse(MModule.single("@dig Foo Room={Exit;ExitAlias},{ExitBack;ExitAliasBack}"));

		await Parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<DBRef>(), "Foo Room created with room number #6.");
		await Parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<DBRef>(), "Linked exit #7 to #6");
		await Parser.NotifyService
			.Received(Quantity.Exactly(4))
			.Notify(Arg.Any<DBRef>(), "Trying to link...");
		await Parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<DBRef>(), "Linked exit #8 to #0");
	}
	
	[Test, Skip("Not yet implemented")]
	public async ValueTask SpicyFunctionCall()
	{
		await Parser.CommandListParse(MModule.single("&foo me=ucstr; think [get(me/foo)](bar)"));

		await Parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<DBRef>(), "BAR");
	}

	[Test]	 
	public async ValueTask DoFlagSet()
	{
		await Parser.CommandParse("1", MModule.single("@set #1=DEBUG"));

		var one = await Parser.Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		var onePlayer = one.AsPlayer;
		var flags = await onePlayer.Object.Flags.WithCancellation(CancellationToken.None);

		await Assert.That(flags.Count(x => x.Name == "DEBUG")).IsEqualTo(1);
	}
}