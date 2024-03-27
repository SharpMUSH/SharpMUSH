using System.Linq.Expressions;

namespace SharpMUSH.Tests.Parser;

[TestClass]
public class BooleanExpressionUnitTests : BaseUnitTest
{
	[Ignore("Don't break GitHub build while we know this isn't completed.")]
	[DataRow("#TRUE",true)]
	[DataRow("(#TRUE)", true)]
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

		var expression = Expression.IsTrue(bep.Parse(input));
		var result = expression.Method!.Invoke(null, []) as bool?;

		Assert.AreEqual(expected, result!);
	}
}
