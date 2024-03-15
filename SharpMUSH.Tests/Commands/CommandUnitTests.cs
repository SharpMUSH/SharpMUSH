using NSubstitute;
using NSubstitute.ReceivedExtensions;
using Serilog;

namespace SharpMUSH.Tests.Commands;

[TestClass]
public class CommandUnitTests : BaseUnitTest
{
	public CommandUnitTests()
	{
		Log.Logger = new LoggerConfiguration()
											.WriteTo.Console()
											.MinimumLevel.Debug()
											.CreateLogger();
	}

	[TestMethod]
	[DataRow("think add(1,2)", "3")]
	[DataRow("think [add(1,2)]", "3")]
	[DataRow("[ansi(hr,think)] Words", "Words")]
	[DataRow("think Command1 Arg;think Command2 Arg", "Command1 Arg;think Command2 Arg")]
	public void Test(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var parser = TestParser();
		_ = parser.CommandParse(str);

		parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(parser.CurrentState().Executor, expected);
	}

	[TestMethod]
	[DataRow("think test()")]
	[DataRow("think [test()]")]
	[DataRow("think [ansi(hr,red)]")]
	[DataRow("think Command1 Arg; think Command2 Arg")]
	public void TestSingle(string str)
	{
		Console.WriteLine("Testing: {0}", str);
		var parser = TestParser();
		var result = parser.CommandParse(str)?.Message;

		Console.WriteLine(string.Join("", result));
	}
}