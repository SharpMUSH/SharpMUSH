using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Benchmarks;

[SimpleJob(RuntimeMoniker.Net90)]
[BenchmarkCategory("Non-DB Function Evaluation")]
public class SimpleFunctionCalls : BaseBenchmark
{
	private readonly IMUSHCodeParser? _parser;

	public SimpleFunctionCalls()
	{
		Setup().ConfigureAwait(false).GetAwaiter().GetResult();
		_parser = TestParser().ConfigureAwait(false).GetAwaiter().GetResult();
	}

	[Benchmark]
	[Arguments(1)]
	[Arguments(10)]
	[Arguments(25)]
	[Arguments(50)]
	[Arguments(100)]
	public async Task Depth(int depth)
	{
		var sb = new StringBuilder();
		foreach (var _ in Enumerable.Range(0, depth))
		{
			sb.Append("[add(1,");
		}
		sb.Append('1');
		foreach (var _ in Enumerable.Range(0, depth))
		{
			sb.Append(")]");
		}
		var str = sb.ToString();

		await _parser!.FunctionParse(MModule.single(str));
	}
}