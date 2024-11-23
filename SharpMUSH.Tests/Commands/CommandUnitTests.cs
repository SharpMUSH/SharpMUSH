using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Commands;

public class CommandUnitTests : BaseUnitTest
{
	private static Infrastructure? infrastructure;

	[Before(Class)]
	public static async Task OneTimeSetup()
	{
		(_,infrastructure) = await IntegrationServer();
	}
	
	[After(Class)]
	public static async Task OneTimeTeardown()
	{
		await Task.Delay(1);
		infrastructure!.Dispose();
	}
	
	[Test]
	[Arguments("think add(1,2)", 
		"3")]
	[Arguments("think [add(1,2)]", 
		"3")]
	[Arguments("]think [add(1,2)]", 
		"[add(1,2)]")]
	[Arguments("think Command1 Arg;think Command2 Arg", 
		"Command1 Arg;think Command2 Arg")]
	public async Task Test(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var parser = await TestParser(
			ds: infrastructure!.Services.GetService(typeof(ISharpDatabase)) as ISharpDatabase,
			ps: infrastructure!.Services.GetService(typeof(IPermissionService)) as IPermissionService,
			ls: infrastructure!.Services.GetService(typeof(ILocateService)) as ILocateService
		);
		await parser.CommandParse("1", MModule.single(str));

		await parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), expected);
	}

	[Test]
	[Arguments("think add(1,2);think add(2,3)", 
		"3", 
		"5")]
	[Arguments("think [add(1,2)];think add(3,2)",
		"3",
		"5")]
	[Arguments("think [ansi(hr,red)];think [ansi(hg,green)]", 
		"\u001b[1;31mred\u001b[0m", 
		"\u001b[1;32mgreen\u001b[0m")]
	[Arguments("think Command1 Arg;think Command2 Arg", 
		"Command1 Arg", 
		"Command2 Arg")]
	[Arguments("think Command1 Arg;think Command2 Arg.;", 
		"Command1 Arg", 
		"Command2 Arg.")]
	public async Task TestSingle(string str, string expected1, string expected2)
	{
		Console.WriteLine("Testing: {0}", str);
		var parser = await TestParser(
			ds: infrastructure!.Services.GetService(typeof(ISharpDatabase)) as ISharpDatabase,
			ps: infrastructure!.Services.GetService(typeof(IPermissionService)) as IPermissionService,
			ls: infrastructure!.Services.GetService(typeof(ILocateService)) as ILocateService
		);
		await parser.CommandListParse(MModule.single(str));

		await parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), expected1);

		await parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), expected2);
	}
}