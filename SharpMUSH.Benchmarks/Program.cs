using BenchmarkDotNet.Running;

namespace SharpMUSH.Benchmarks;

public class Program
{
	public static async Task<int> Main(string[] args)
	{
		if (args is ["profile", ..])
		{
			await ProfileHarness.RunAsync(args[1..]);
			return 0;
		}

		var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args).ToArray();
		// Only measured CI shards opt in. Discovery, information and interactive invocations
		// deliberately return no summaries, and the profile harness is independent of BenchmarkDotNet.
		if (!string.Equals(Environment.GetEnvironmentVariable("SHARPMUSH_REQUIRE_BENCHMARK_RESULTS"), "true",
					StringComparison.OrdinalIgnoreCase))
		{
			return 0;
		}

		var failed = summaries.Length == 0;
		if (failed)
		{
			Console.Error.WriteLine("BenchmarkDotNet produced no summaries. Check the filter and validation messages above.");
		}

		foreach (var summary in summaries)
		{
			foreach (var error in summary.ValidationErrors.Where(error => error.IsCritical))
			{
				failed = true;
				Console.Error.WriteLine($"Benchmark validation failed: {error.Message}");
			}

			if (summary.Reports.Length == 0)
			{
				failed = true;
				Console.Error.WriteLine($"No benchmark reports were produced for {summary.Title}.");
			}

			foreach (var report in summary.Reports)
			{
				if (!report.Success || report.ResultStatistics is null)
				{
					failed = true;
					Console.Error.WriteLine($"Benchmark failed or produced no measurements: {report.BenchmarkCase.DisplayInfo}");
				}
			}
		}

		return failed ? 1 : 0;
	}
}
