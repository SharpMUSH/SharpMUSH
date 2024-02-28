using Serilog;

namespace AntlrCSharp.Tests
{
	[TestClass]
	public class MathFunctionUnitTests
	{
		public MathFunctionUnitTests()
		{
			Log.Logger = new LoggerConfiguration()
											.WriteTo.Console()
											.MinimumLevel.Debug()
											.CreateLogger();
		}

		[TestMethod]
		[DataRow("add(1,2)", "3")]
		[DataRow("add(1,add(2,3))", "6")]
		[DataRow("add(1,add(2,3),add(2,2))", "10")]
		[DataRow("add(1,2)[add(5,5)]", "310")]
		[DataRow("add(1,2)[add(5,5)]word()", "310word()")]
		public void Test(string str, string expected)
		{
			Console.WriteLine("Testing: {0}", str);
			var parser = new AntlrCSharp.Implementation.Parser();
			var result = parser.FunctionParse(str)?.Message;

			Console.WriteLine(string.Join("", result));

			Assert.AreEqual<string>(expected, string.Join("",result));
		}
	}
}
