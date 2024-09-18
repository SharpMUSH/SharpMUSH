namespace SharpMUSH.Tests.Internal;

[TestClass]
public class NameList
{
	[TestMethod]
	[DataRow("God", "God")]
	public void SingleString(string str, string expected)
	{
		var result = Implementation.Functions.Functions.NameList(str);

		Assert.AreEqual(expected, result.Single().AsT1);
	}

	[TestMethod]
	[DataRow("#1", 1)]
	public void SingleDBRef(string str, int expected)
	{
		var result = Implementation.Functions.Functions.NameList(str);

		Assert.AreEqual(new Library.Models.DBRef(expected), result.Single().AsT0);
	}

	[TestMethod]
	[DataRow("#1:999", 1, 999)]
	public void SingleDBRefWithTimestamp(string str, int expectedDbRef, int expectedTimestamp)
	{
		var result = Implementation.Functions.Functions.NameList(str);

		Assert.AreEqual(new Library.Models.DBRef(expectedDbRef, expectedTimestamp), result.Single().AsT0);
	}
}