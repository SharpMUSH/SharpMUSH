using System.Text;

namespace SharpMUSH.Tests.Functions;

[TestClass]
public class MemoryTest : BaseUnitTest
{
	[TestMethod]
	public void Depth()
	{
		var sb = new StringBuilder();
		foreach(var i in Enumerable.Range(0, 50))
		{
			sb.Append("add(1,");
		}
		sb.Append('1');
		foreach (var i in Enumerable.Range(0, 50))
		{
			sb.Append(')');
		}
		var str = sb.ToString();

		var parser = TestParser();
		var result = parser.FunctionParse(MModule.single(str))?.Message?.ToString();
		
		Assert.AreEqual("51", result);
	}
}

