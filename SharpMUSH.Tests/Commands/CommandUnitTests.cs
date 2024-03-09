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
	[DataRow("think test()")]
	[DataRow("think [test()]")]
	[DataRow("[ansi(hr,red)]")]
	[DataRow("think Command1 Arg; think Command2 Arg")]
	public void Test(string str)
	{
		Console.WriteLine("Testing: {0}", str);
		var parser = TestParser();
		var result = parser.CommandListParse(str)?.Message;

		Console.WriteLine(string.Join("", result));
	}

	[TestMethod]
	[DataRow("think test()")]
	[DataRow("think [test()]")]
	[DataRow("[ansi(hr,red)]")]
	[DataRow("think Command1 Arg; think Command2 Arg")]
	public void TestSingle(string str)
	{
		Console.WriteLine("Testing: {0}", str);
		var parser = TestParser();
		var result = parser.CommandParse(str)?.Message;

		Console.WriteLine(string.Join("", result));
	}
}