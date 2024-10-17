using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Commands;

public class GeneralCommandTests : BaseUnitTest
{
	private static ISharpDatabase? database;
	private static IPermissionService? permission;

	[Before(Class)]
	public static async Task OneTimeSetup()
	{
		database = (await IntegrationServer()).Database;		
		
		permission = Substitute.For<IPermissionService>();
		permission.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>()).Returns(true);
		permission.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>()).Returns(true);
		permission.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<IPermissionService.InteractType>()).Returns(true);
	}

	[Test]
	[Arguments("@pemit #1=This is a test", "This is a test")]
	[Arguments("@pemit #1=This is a test;", "This is a test;")]
	public async Task Test(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var parser = TestParser(ds: database, ls: new LocateService(), ps: permission);
		await parser.CommandParse("1", MModule.single(str));

		await parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<DBRef>(), expected);
	}

	[Test]
	public async Task DoListSimple()
	{
		var parser = TestParser(ds: database, ls: new LocateService(), ps: permission);
		await parser.CommandParse("1", MModule.single("@dolist 1 2 3=@pemit #1=This is a test"));

		await parser.NotifyService
			.Received(Quantity.Exactly(3))
			.Notify(Arg.Any<DBRef>(), "This is a test");
	}

	[Test]
	public async Task DoListSimple2()
	{
		var parser = TestParser(ds: database, ls: new LocateService(), ps: permission);
		await parser.CommandParse("1", MModule.single("@dolist 1 2 3=@pemit #1={This is, a test};"));

		await parser.NotifyService
			.Received(Quantity.Exactly(3))
			.Notify(Arg.Any<DBRef>(), "This is, a test");
	}

	[Test]
	public async Task DoListComplex()
	{
		var parser = TestParser(ds: database, ls: new LocateService(), ps: permission);
		await parser.CommandParse("1", MModule.single("@dolist 1 2 3={@pemit #1=This is a test; @pemit #1=This is also a test}"));

		await parser.NotifyService
			.Received(Quantity.Exactly(3))
			.Notify(Arg.Any<DBRef>(), "This is a test");
		await parser.NotifyService
			.Received(Quantity.Exactly(3))
			.Notify(Arg.Any<DBRef>(), "This is also a test");
	}
	
	[Test]
	public async Task DoListComplex2()
	{
		var parser = TestParser(ds: database, ls: new LocateService(), ps: permission);
		await parser.CommandParse("1", MModule.single("@dolist 1 2 3={@pemit #1=This is a test; @pemit #1=This is also a test}; @pemit #1=Repeat 3 times in this mode."));

		await parser.NotifyService
			.Received(Quantity.Exactly(3))
			.Notify(Arg.Any<DBRef>(), "This is a test");
		await parser.NotifyService
			.Received(Quantity.Exactly(3))
			.Notify(Arg.Any<DBRef>(), "This is also a test");
		await parser.NotifyService
			.Received(Quantity.Exactly(3))
			.Notify(Arg.Any<DBRef>(), "Repeat 3 times in this mode.");
	}
	
	[Test]
	public async Task DoListComplex3()
	{
		var parser = TestParser(ds: database, ls: new LocateService(), ps: permission);
		await parser.CommandParse("1", MModule.single("@dolist 1={@dolist 1 2 3=@pemit #1=This is a test}; @pemit #1=Repeat 1 times in this mode."));

		await parser.NotifyService
			.Received(Quantity.Exactly(3))
			.Notify(Arg.Any<DBRef>(), "This is a test");
		await parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<DBRef>(), "Repeat 1 times in this mode.");
	}
	
	[Test]
	public async Task DoListComplex4()
	{
		var parser = TestParser(ds: database, ls: new LocateService(), ps: permission);
		await parser.CommandParse("1", MModule.single("@dolist 1 2={@dolist 1 2 3=@pemit #1=This is a test}; @pemit #1=Repeat 2 times in this mode."));

		await parser.NotifyService
			.Received(Quantity.Exactly(6))
			.Notify(Arg.Any<DBRef>(), "This is a test");
		await parser.NotifyService
			.Received(Quantity.Exactly(2))
			.Notify(Arg.Any<DBRef>(), "Repeat 2 times in this mode.");
	}
		
	[Test /*, Skip("Severe parser issue found. It's evaluating before it should be evaluating.") */]
	public async Task DoListComplex5()
	{
		var parser = TestParser(ds: database, ls: new LocateService(), ps: permission);
		await parser.CommandParse("1", MModule.single("@dolist a b={@dolist 1 2 3=@pemit #1=This is a test %i0}; @pemit #1=Repeat 1 times in this mode %i0"));

		await parser.NotifyService
			.Received(Quantity.Exactly(2))
			.Notify(Arg.Any<DBRef>(), "This is a test 1");
		await parser.NotifyService
			.Received(Quantity.Exactly(2))
			.Notify(Arg.Any<DBRef>(), "This is a test 2");
		await parser.NotifyService
			.Received(Quantity.Exactly(2))
			.Notify(Arg.Any<DBRef>(), "This is a test 3");
		await parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<DBRef>(), "Repeat 1 times in this mode a");
		await parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<DBRef>(), "Repeat 1 times in this mode b");
	}
}