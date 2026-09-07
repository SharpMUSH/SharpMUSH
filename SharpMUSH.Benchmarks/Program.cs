using BenchmarkDotNet.Running;

namespace SharpMUSH.Benchmarks;

public class Program
{
	public static async Task Main(string[] args)
	{
		if (args is ["profile", ..])
		{
			await ProfileHarness.RunAsync(args[1..]);
			return;
		}

		BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
	}
}
