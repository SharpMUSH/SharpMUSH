namespace AntlrCSharp.Tests.Functions
{
	[TestClass]
	public class BooleanFunctionUnitTests : BaseUnitTest
	{
		[TestMethod]
		[DataRow("t(1)", "1")]
		[DataRow("t(0)", "0")]
		[DataRow("t(true)", "1")]
		[DataRow("t(false)", "1")]
		[DataRow("t(#-1 Words)", "0")]
		[DataRow("t()", "0")]
		[DataRow("t( )", "1")]
		public void Add(string str, string expected)
		{
			Console.WriteLine("Testing: {0}", str);

			var parser = new Implementation.Parser();
			var result = parser.FunctionParse(str)?.Message?.ToString();

			Assert.AreEqual(expected, result);
		}

		[TestMethod]
		[DataRow("and(1,1)", "1")]
		[DataRow("and(0,1)", "0")]
		[DataRow("and(0,0,1)", "0")]
		[DataRow("and(1,1,1)", "1")]
		public void And(string str, string expected)
		{
			Console.WriteLine("Testing: {0}", str);

			var parser = new Implementation.Parser();
			var result = parser.FunctionParse(str)?.Message?.ToString();

			Assert.AreEqual(expected, result);
		}

		[TestMethod]
		[DataRow("nand(1,1)", "0")]
		[DataRow("nand(0,1)", "1")]
		[DataRow("nand(0,0,1)", "1")]
		[DataRow("nand(1,1,1)", "0")]
		public void Nand(string str, string expected)
		{
			Console.WriteLine("Testing: {0}", str);

			var parser = new Implementation.Parser();
			var result = parser.FunctionParse(str)?.Message?.ToString();

			Assert.AreEqual(expected, result);
		}
	}
}
