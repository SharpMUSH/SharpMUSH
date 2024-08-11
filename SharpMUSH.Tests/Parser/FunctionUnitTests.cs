namespace SharpMUSH.Tests.Parser;

[TestClass]
public class FunctionUnitTests : BaseUnitTest
{
	[TestMethod]
	[DataRow("strcat(strcat(),wi`th a[strcat(strcat(strcat(depth of 5)))])")]
	// [DataRow("strcat(strcat(dog)", "strcat(dog")] // Currently Illegal according to the Parser. Fix needed.
	[DataRow("strcat(foo\\,dog)", "foo,dog")]
	[DataRow("strcat(foo\\\\,dog)", "foo\\dog")]
	[DataRow("strcat(foo,-dog))", "foo-dog)")]
	[DataRow("\\t", "t")]
	[DataRow("add(1,5)","6")]
	[DataRow("add(1,add(2,3),add(2,2))", "10")]
	[DataRow("add(1,2)[add(5,5)]", "310")]
	[DataRow("add(1,2)[add(5,5)]word()", "310word()")]
	public void Test(string str, string? expected = null)
	{
		Console.WriteLine("Testing: {0}", str);
		var parser = TestParser();
		var result = parser.FunctionParse(MModule.single(str))?.Message?.ToString();

		Console.WriteLine(string.Join("", result));

		if (expected != null)
		{
			Assert.AreEqual(expected, result);
		}
	}
}