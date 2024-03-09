namespace SharpMUSH.Tests.Parser
{
	[TestClass]
	public class SubstitutionUnitTests : BaseUnitTest
    {

		[TestMethod]
		[DataRow("strcat(strcat(),wi`th a[strcat(strcat(strcat(depth of 5)))])")]
		public void Test(string str, string? expected = null)
		{
			Console.WriteLine("Testing: {0}", str);
			var parser = TestParser();
			var result = parser.FunctionParse(str)?.Message?.ToString();

			Console.WriteLine(string.Join("", result));

			if (expected != null)
			{
				Assert.AreEqual(expected, result);
			}
		}
	}
}
