using Serilog;

namespace AntlrCSharpTests;

[TestClass]
public class CommandUnitTests
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
		var parser = new AntlrCSharp.Implementation.Parser();
		var result = parser.CommandListParse(str);

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
		var parser = new AntlrCSharp.Implementation.Parser();
		var result = parser.CommandParse(str);

		Console.WriteLine(string.Join("", result));
	}
}