namespace SharpMUSH.Tests.Functions;

[TestClass]
public class MathFunctionUnitTests: BaseUnitTest
{

	[TestMethod]
	[DataRow("add(1,2)", "3")]
	[DataRow("add(1.5,5)", "6.5")]
	[DataRow("add(-1.5,5)", "3.5")]
	[DataRow("add(1,1,1,1)", "4")]
	public async Task Add(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var parser = TestParser();
		var result = (await parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		Assert.AreEqual(expected, result);
	}
}
