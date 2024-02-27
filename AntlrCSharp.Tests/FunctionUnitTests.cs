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
	[DataRow("function(test(),wi`th a[Level2(Level3(Level4(Level5)))])")]
	[DataRow("function(test(dog)")]
	[DataRow("function(foo\\,dog)")]
	[DataRow("function(foo\\\\,dog)")]
	[DataRow("function(foo,dog)")]
	[DataRow("function(%s)")]
	[DataRow("function(%q0)")]
	[DataRow("function(%q<test>)")]
	[DataRow("%s")]
	[DataRow("%q<test>")]
	[DataRow("%q<0>")]
	[DataRow("function(%q<0>)")]
	[DataRow("function(%q<word()>)")]
	[DataRow("function(%q<[word()]>)")]
	[DataRow("function(%q<%s>)")]
	[DataRow("function(%q<%q0>)")]
	[DataRow("function(%q<Word %q<5> [function(%q<6six>)]>)")]
	public void Test(string str)
	{
		Console.WriteLine("Testing: {0}", str);
		var parser = new AntlrCSharp.Implementation.Parser();
		var result = parser.FunctionParse(str);

		Console.WriteLine(string.Join("", result));
	}
}