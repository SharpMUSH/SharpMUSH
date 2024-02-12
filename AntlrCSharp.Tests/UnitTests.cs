using Serilog;

namespace AntlrCSharpTests;

[TestClass]
public class UnitTests
{
	public UnitTests()
	{
		Log.Logger = new LoggerConfiguration()
										.WriteTo.Console()
										.MinimumLevel.Debug()
										.CreateLogger();
	}

	[TestMethod]
	public void TestMethod()
	{
		var parser = new AntlrCSharp.Implementation.Parser();
		var result = parser.Parse("function(test(),wi`th a[Level2(Level3(Level4(Level5)))])");

		Console.WriteLine(string.Join("", result));
	}
}