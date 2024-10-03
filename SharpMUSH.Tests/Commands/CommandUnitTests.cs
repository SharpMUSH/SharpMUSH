using NSubstitute;
using NSubstitute.ReceivedExtensions;
using Serilog;

namespace SharpMUSH.Tests.Commands;

public class CommandUnitTests : BaseUnitTest
{
	[Before(Class)]
	public static void SetupLogger()
	{
		Log.Logger = new LoggerConfiguration()
											.WriteTo.Console()
											.MinimumLevel.Debug()
											.CreateLogger();
	}

	[Test]
	[Arguments("think add(1,2)", 
		"3")]
	[Arguments("think [add(1,2)]", 
		"3")]
	[Arguments("[ansi(hr,think)] Words", 
		"Words")]
	[Arguments("think Command1 Arg;think Command2 Arg", 
		"Command1 Arg;think Command2 Arg")]
	public async Task Test(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var parser = TestParser();
		await parser.CommandParse("1", MModule.single(str));

		await parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(parser.CurrentState.Executor!.Value, expected);
	}

	[Test]
	[Arguments("think add(1,2);think add(2,3)", 
		"3", 
		"5")]
	[Arguments("think [add(1,2)];think add(3,2)",
		"3",
		"5")]
	[Arguments("[ansi(hy,think)] [ansi(hr,red)];[ansi(hg,think)] [ansi(hg,green)]", 
		"\u001b[1;31mred\u001b[0m", 
		"\u001b[1;32mgreen\u001b[0m")]
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
		var parser = TestParser();
		await parser.CommandListParse(MModule.single(str));

		await parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(parser.CurrentState.Executor!.Value, expected1);

		await parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(parser.CurrentState.Executor!.Value, expected2);
	}
}