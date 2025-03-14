using BenchmarkDotNet.Running;

namespace SharpMUSH.Benchmarks;

public class Program
{
	public static void Main(string[] args)
	{
		var summary = BenchmarkRunner.Run<SimpleFunctionCalls>();
		// var sfc = new SimpleFunctionCalls();
		// sfc.Depth().ConfigureAwait(false).GetAwaiter().GetResult();
	}
}