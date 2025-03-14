using System.Text;

namespace SharpMUSH.Tests.Functions;

public class MemoryTest : BaseUnitTest
{
	[Test]
	public async Task Depth()
	{
		var sb = new StringBuilder();
		foreach (var _ in Enumerable.Range(0, 200))
		{
			sb.Append("add(1,");
		}
		sb.Append('1');
		foreach (var _ in Enumerable.Range(0, 200))
		{
			sb.Append(')');
		}
		var str = sb.ToString();

		var parser = await TestParser();
		var result = (await parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert
			.That(result)
			.IsEqualTo("201");
	}
	
	[Test]
	public async Task SmallDepth()
	{
		var sb = new StringBuilder();
		foreach (var _ in Enumerable.Range(0, 10))
		{
			sb.Append("[add(1,");
		}
		sb.Append('1');
		foreach (var _ in Enumerable.Range(0, 10))
		{
			sb.Append(")]");
		}
		var str = sb.ToString();

		var parser = await TestParser();
		var result = (await parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert
			.That(result)
			.IsEqualTo("11");
	}
}

