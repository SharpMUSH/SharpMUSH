using BenchmarkDotNet.Running;

namespace SharpMUSH.Benchmarks;

public class Program
{
	public static void Main(string[] args)
	{
		// If --simple flag is passed, run simple benchmarks instead of BenchmarkDotNet
		if (args.Contains("--simple"))
		{
			SimpleBenchmark.Run();
			return;
		}
		
		var summary = BenchmarkRunner.Run<SimpleFunctionCalls>();
		// var sfc = new SimpleFunctionCalls();
		// sfc.Depth().ConfigureAwait(false).GetAwaiter().GetResult();
	}
}