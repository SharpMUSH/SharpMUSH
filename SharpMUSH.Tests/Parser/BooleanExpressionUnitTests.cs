using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Parser;

[TestClass]
public class BooleanExpressionUnitTests : BaseUnitTest
{
	[DataRow("!#FALSE", true)]
	[DataRow("#TRUE", true)]
	[DataRow("(#TRUE)", true)]
	[DataRow("!#TRUE", false)]
	[DataRow("#FALSE", false)]
	[DataRow("(#FALSE)", false)]
	[DataRow("#TRUE & #TRUE", true)]
	[DataRow("#TRUE | #TRUE", true)]
	[DataRow("#TRUE & !#FALSE", true)]
	[DataRow("#TRUE & #FALSE", false)]
	[DataRow("#FALSE & #TRUE", false)]
	[DataRow("#TRUE | #FALSE", true)]
	[DataRow("#FALSE | #TRUE", true)]
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
