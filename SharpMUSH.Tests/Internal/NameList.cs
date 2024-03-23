namespace SharpMUSH.Tests.Internal
{
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
	}
}
