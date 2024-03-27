using SharpMUSH.Database;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Parser;

[TestClass]
public class BooleanExpressionUnitTests : BaseUnitTest
{
	private static ISharpDatabase? _database;

	[ClassInitialize()]
	public static async Task OneTimeSetup(TestContext _)
	{
		_database = await IntegrationServer();
	}


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
		Assert.AreEqual(expected, bep.Parse(input, new DBRef(1), new DBRef(1)));
	}

	[DataRow("type^Player & #TRUE", true)]
	[DataRow("type^Player & #FALSE", false)]
	[DataRow("type^Player & !type^Player", false)]
	[DataRow("type^Thing", false)]
	[DataRow("type^Player", true)]
	[TestMethod]
	public void TypeExpressions(string input, bool expected)
	{
		var bep = BooleanExpressionParser(TestParser(ds: _database));

		Assert.IsTrue(bep.Validate(input, new DBRef(1)));
		Assert.AreEqual(expected, bep.Parse(input, new DBRef(1), new DBRef(1)));
	}

	[DataRow("type^Player", true)]
	[DataRow("type^Thing", true)]
	[DataRow("type^Room", true)]
	[DataRow("type^Exit", true)]
	[DataRow("type^Nonsense", false)]
	[TestMethod]
	public void TypeValidation(string input, bool expected)
	{
		var bep = BooleanExpressionParser(TestParser());

		Assert.AreEqual(expected, bep.Validate(input, new DBRef(1)));
	}
}