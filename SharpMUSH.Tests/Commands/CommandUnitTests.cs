using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Commands;

public class CommandUnitTests : BaseUnitTest
{
	private static IMUSHCodeParser? _parser;

	[Before(Class)]
	public static async Task OneTimeSetup()
	{
		_parser = await TestParser(
			ns: Substitute.For<INotifyService>()
			);
	}

	[Test]
	[Arguments("think add(1,2)1",
		"31")]
	[Arguments("think [add(1,2)]2",
		"32")]
	[Arguments("]think [add(1,2)]3",
		"[add(1,2)]3")]
	[Arguments("think Command1 Arg;think Command2 Arg",
		"Command1 Arg;think Command2 Arg")]
	public async Task Test(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		await _parser!.CommandParse("1", MModule.single(str));

		await _parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), expected);
	}

	[Test]
	[Arguments("think add(1,2)4;think add(2,3)5",
		"34",
		"55")]
	[Arguments("think [add(1,2)]6;think add(3,2)7",
		"36",
		"57")]
	[Arguments("think [ansi(hr,red)];think [ansi(hg,green)]",
		"\e[1;31mred\e[0m",
		"\e[1;32mgreen\e[0m")]
	[Arguments("think Command1 Arg;think Command2 Arg",
		"Command1 Arg",
		"Command2 Arg")]
	[Arguments("think Command3 Arg;think Command4 Arg.;",
		"Command3 Arg",
		"Command4 Arg.")]
	public async Task TestSingle(string str, string expected1, string expected2)
	{
		Console.WriteLine("Testing: {0}", str);
		await _parser!.CommandListParse(MModule.single(str));

		await _parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), expected1);

		await _parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), expected2);
	}
}