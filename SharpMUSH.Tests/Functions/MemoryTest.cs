using System.Text;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class MemoryTest
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	public async Task Depth()
	{
		// Test deep nesting without recursion by alternating between different functions
		// Use 8 nested calls to stay well within max_depth limit of 10
		// This tests memory handling with deep nesting but without hitting limits
		var sb = new StringBuilder();
		var functions = new[] { "add", "sub", "mul", "div" };
		foreach (var i in Enumerable.Range(0, 8))
		{
			var func = functions[i % functions.Length];
			sb.Append($"[{func}(1,");
		}
		sb.Append('1');
		foreach (var _ in Enumerable.Range(0, 8))
		{
			sb.Append(")]");
		}
		var str = sb.ToString();

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		// Each operation: add(1,x)=1+x, sub(1,x)=1-x, mul(1,x)=1*x, div(1,x)=1/x
		// Working from innermost: 1
		// div(1,1) = 1
		// mul(1,1) = 1
		// sub(1,1) = 0
		// add(1,0) = 1
		// ... pattern repeats twice (8 calls / 4 functions = 2 cycles)
		// Final result: 1
		await Assert
			.That(result)
			.IsEqualTo("1");
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

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert
			.That(result)
			.IsEqualTo("11");
	}

	[Test, Timeout(30 * 1000), NotInParallel]
	[Explicit]
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
			sb.Append("strcat(1,");
		}
		sb.Append(longstring);
		foreach (var _ in Enumerable.Range(0, 100))
		{
			sb.Append(')');
		}
		var str = sb.ToString();

		await Assert.That(async () => {
			var result = await Parser.FunctionParse(MModule.single(str));
		}).ThrowsNothing();
	}

	[Test, Timeout(30 * 1000), NotInParallel]
	[Explicit]
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
			sb.Append("[strcat(1,");
		}
		sb.Append(longstring);
		foreach (var _ in Enumerable.Range(0, 100))
		{
			sb.Append(")]");
		}
		var str = sb.ToString();

		await Assert.That(async () => {
			var result = await Parser.FunctionParse(MModule.single(str));
		}).ThrowsNothing();
	}
}

