using System.Text;

namespace SharpMUSH.Tests.Functions;

[TestClass]
public class MemoryTest : BaseUnitTest
{
	[TestMethod]
	public async Task Depth()
	{
		var sb = new StringBuilder();
		foreach(var _ in Enumerable.Range(0, 200))
		{
			sb.Append("add(1,");
		}
		sb.Append('1');
		foreach (var _ in Enumerable.Range(0, 200))
		{
			sb.Append(')');
		}
		var str = sb.ToString();

		var parser = TestParser();
		var result = (await parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		Assert.AreEqual("201", result);
	}
}

