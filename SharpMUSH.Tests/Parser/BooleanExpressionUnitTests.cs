using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Parser;

[TestClass]
public class BooleanExpressionUnitTests : BaseUnitTest
{
	[DataRow("#TRUE", true)]
	[DataRow("(#TRUE)", true)]
	[DataRow("#FALSE", false)]
	[DataRow("(#FALSE)", false)]
	[DataRow("#TRUE & #TRUE", true)]
	[DataRow("#TRUE | #TRUE", true)]
	[DataRow("#TRUE & !#FALSE", true)]
	[DataRow("#TRUE & #TRUE & #TRUE", true)]
	[DataRow("#TRUE | #TRUE | #TRUE", true)]
	[DataRow("#TRUE & !#FALSE | #TRUE", true)]
	[TestMethod]
	public void SimpleExpressions(string input, bool expected)
	{
		var bep = BooleanExpressionParser(TestParser());

		Assert.IsTrue(bep.Validate(input, new DBRef(1)));
		Assert.AreEqual(expected, bep.Parse(input, new DBRef(1)));
	}
}
