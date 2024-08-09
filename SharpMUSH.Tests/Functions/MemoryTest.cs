using System.Text;

namespace SharpMUSH.Tests.Functions;

[TestClass]
public class MemoryTest : BaseUnitTest
{
	[TestMethod, Ignore("Until we get the Ambiguities removed from the grammar.")]
	public void Depth()
	{

		var sb = new StringBuilder();
		foreach(var i in Enumerable.Range(0, 256))
		{
			sb.Append("add(1,");
		}
		sb.Append('1');
		foreach (var i in Enumerable.Range(0, 256))
		{
			sb.Append(')');
		}
		var str = sb.ToString();

		var parser = TestParser();
		var result = parser.FunctionParse(MModule.single(str))?.Message?.ToString();

		Assert.AreEqual("257", result);
	}
}
