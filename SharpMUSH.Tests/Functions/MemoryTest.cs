using System.Text;
using MoreLinq.Extensions;
using TUnit.Assertions.AssertConditions.Throws;

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
	
	[Test]
	public async Task HeavyMemoryUsage()
	{
		var longstring = new string('1', 1024*5*1024);
		var sb = new StringBuilder();
		
		foreach (var _ in Enumerable.Range(0, 100))
		{
			sb.Append($"[strcat(1,");
		}
		sb.Append(longstring);
		foreach (var _ in Enumerable.Range(0, 100))
		{
			sb.Append(")]");
		}
		var str = sb.ToString();
		var len = str.Length;

		var parser = await TestParser();

		await Assert.That(async () => await parser.FunctionParse(MModule.single(str))).ThrowsNothing();

		var result = await parser.FunctionParse(MModule.single(str));
		Console.WriteLine(result);
	}
}

