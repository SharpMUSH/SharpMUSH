using Serilog;

namespace AntlrCSharp.Tests.Parser;

[TestClass]
public class FunctionUnitTests : BaseUnitTest
{
	[TestMethod]
	[DataRow("strcat(strcat(),wi`th a[strcat(strcat(strcat(depth of 5)))])")]
	[DataRow("strcat(strcat(dog)", "strcat(dog")]
	[DataRow("strcat(foo\\,dog)", "foo,dog")]
	[DataRow("strcat(foo\\\\,dog)", "foo\\dog")]
	[DataRow("strcat(foo,-dog)", "foo-dog")]
	[DataRow("strcat(%s)")]
	[DataRow("strcat(%q0)")]
	[DataRow("strcat(%q<test>)")]
	[DataRow("%s")]
	[DataRow("\\t", "t")]
	[DataRow("%q<test>")]
	[DataRow("%q<0>")]
	[DataRow("strcat(%q<0>)")]
	[DataRow("strcat(%q<word()>)")]
	[DataRow("strcat(%q<[strcat(1,0)]>)")]
	[DataRow("strcat(%q<%s>)")]
	[DataRow("strcat(%q<%q0>)")]
	[DataRow("strcat(%q<Word %q<5> [strcat(%q<6six>)]>)")]
	[DataRow("add(1,add(2,3),add(2,2))", "10")]
	[DataRow("add(1,2)[add(5,5)]", "310")]
	[DataRow("add(1,2)[add(5,5)]word()", "310word()")]
	public void Test(string str, string? expected = null)
	{
		Console.WriteLine("Testing: {0}", str);
		var parser = new Implementation.Parser();
		var result = parser.FunctionParse(str)?.Message?.ToString();

		Console.WriteLine(string.Join("", result));

		if (expected != null)
		{
			Assert.AreEqual(expected, result);
		}
	}
}