using Serilog;

namespace AntlrCSharpTests;

[TestClass]
public class FunctionUnitTests
{
	public FunctionUnitTests()
	{
		Log.Logger = new LoggerConfiguration()
										.WriteTo.Console()
										.MinimumLevel.Debug()
										.CreateLogger();
	}

	[TestMethod]
	[DataRow("strcat(strcat(),wi`th a[strcat(strcat(strcat(depth of 5)))])")]
	[DataRow("strcat(strcat(dog)", "strcat(dog")]
	[DataRow("strcat(foo\\,dog)", "foo,dog")]
	[DataRow("strcat(foo\\\\,dog)", "foo\\dog")]
	[DataRow("strcat(foo,dog)", "foodog")]
	[DataRow("strcat(%s)")]
	[DataRow("strcat(%q0)")]
	[DataRow("strcat(%q<test>)")]
	[DataRow("%s")]
	[DataRow("%q<test>")]
	[DataRow("%q<0>")]
	[DataRow("strcat(%q<0>)")]
	[DataRow("strcat(%q<word()>)")]
	[DataRow("strcat(%q<[strcat(1,0)]>)")]
	[DataRow("strcat(%q<%s>)")]
	[DataRow("strcat(%q<%q0>)")]
	[DataRow("strcat(%q<Word %q<5> [strcat(%q<6six>)]>)")]
	public void Test(string str, string? expected = null)
	{
		Console.WriteLine("Testing: {0}", str);
		var parser = new AntlrCSharp.Implementation.Parser();
		var result = parser.FunctionParse(str)?.Message;

		Console.WriteLine(string.Join("", result));

		if(expected != null)
		{
			Assert.AreEqual(expected, result);
		}
	}
}