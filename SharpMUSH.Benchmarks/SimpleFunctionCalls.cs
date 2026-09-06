using System.Text;

namespace SharpMUSH.Benchmarks;

[BenchmarkCategory("Non-DB Function Evaluation")]
public class SimpleFunctionCalls : BaseBenchmark
{

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
			sb.Append("[add(1,");
		sb.Append('1');
		foreach (var _ in Enumerable.Range(0, depth))
			sb.Append(")]");

		await FreshParser().FunctionParse(MModule.single(sb.ToString()));
	}
}
