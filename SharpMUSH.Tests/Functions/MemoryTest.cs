using System.Text;
using TUnit.Assertions.AssertConditions.Throws;

namespace SharpMUSH.Tests.Functions;

public class MemoryTest : BaseUnitTest
{
	[Test]
	public async Task Depth()
	{
		var sb = new StringBuilder();
		foreach (var _ in Enumerable.Range(0, 100))
		{
			sb.Append("[add(1,");
		}
		sb.Append('1');
		foreach (var _ in Enumerable.Range(0, 100))
		{
			sb.Append(")]");
		}
		var str = sb.ToString();

		var parser = await TestParser();
		var result = (await parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert
			.That(result)
			.IsEqualTo("101");
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

	[Test, Timeout(30 * 1000), NotInParallel]
	[Arguments(4)]
	[Arguments(8)]
	[Arguments(16)]
	[Arguments(32)]
	[Arguments(64)]
	[Arguments(128)]
	[Arguments(256)]
	[Arguments(512)]
	[Arguments(1024)]
	[Arguments(2048)]
	[Arguments(4096)]
	[Arguments(8192)]
	public async Task HeavyMemoryUsage(int kb, CancellationToken ct)
	{
		var longstring = new string('1', 1024 * kb);
		var sb = new StringBuilder();

		foreach (var _ in Enumerable.Range(0, 100))
		{
			sb.Append($"strcat(1,");
		}
		sb.Append(longstring);
		foreach (var _ in Enumerable.Range(0, 100))
		{
			sb.Append(")");
		}
		var str = sb.ToString();

		var parser = await TestParser();

		await Assert.That(async () => {
			var result = await parser.FunctionParse(MModule.single(str));
		}).ThrowsNothing();
	}

	[Test, Timeout(30 * 1000), NotInParallel]
	[Arguments(4)]
	[Arguments(8)]
	[Arguments(16)]
	[Arguments(32)]
	[Arguments(64)]
	[Arguments(128)]
	[Arguments(256)]
	[Arguments(512)]
	[Arguments(1024)]
	[Arguments(2048)]
	[Arguments(4096)]
	[Arguments(8192)]
	public async Task HeavyMemoryUsageForSquareBrackets(int kb, CancellationToken ct)
	{
		var longstring = new string('1', 1024 * kb);
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

		var parser = await TestParser();

		await Assert.That(async () => {
			var result = await parser.FunctionParse(MModule.single(str));
		}).ThrowsNothing();
	}
}

