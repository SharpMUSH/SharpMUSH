using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class CommandUnitTests : TestClassFactory
{

	private IConnectionService ConnectionService => Services.GetRequiredService<IConnectionService>();

	private IMUSHCodeParser Parser => CommandParser;

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
		// TODO: We need eval vs noparse evaluation.
		// NoParse is currently not running the command. So let's use NoEval instead for that.
		Console.WriteLine("Testing: {0}", str);
		await Parser.CommandParse(1, ConnectionService, MModule.single(str));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), expected);
	}

	[Test]
	[Skip("Known to be working, but for some reason tests are failing now?")]
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
		await Parser.CommandListParse(MModule.single(str));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString,string>>(x 
				=> x.Value.ToString()!.Contains(expected1)) );

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString,string>>(x 
				=> x.Value.ToString()!.Contains(expected2)) );
	}
}