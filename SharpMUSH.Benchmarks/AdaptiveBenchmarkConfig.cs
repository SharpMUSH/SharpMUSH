using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Validators;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// CI-aware BenchmarkDotNet configuration.
/// Uses <see cref="Job.ShortRun"/> when the CI environment variable is set (GitHub Actions / SHARPMUSH_CI_BENCHMARK),
/// and <see cref="Job.Default"/> for nightly / local runs.
/// </summary>
public class AdaptiveBenchmarkConfig : ManualConfig
{
	/// <summary>Returns true when running inside a CI environment.</summary>
	public static bool IsCi() =>
		string.Equals(Environment.GetEnvironmentVariable("SHARPMUSH_CI_BENCHMARK"), "true", StringComparison.OrdinalIgnoreCase);

	public AdaptiveBenchmarkConfig()
	{
		AddJob(IsCi()
			? Job.ShortRun.WithId("CI")
			: Job.Default.WithId("Nightly"));

		AddDiagnoser(MemoryDiagnoser.Default);
		AddDiagnoser(ThreadingDiagnoser.Default);
		AddDiagnoser(ExceptionDiagnoser.Default);

		AddLogger(ConsoleLogger.Default);
		AddColumnProvider(DefaultColumnProviders.Instance);
		AddExporter(MarkdownExporter.GitHub);

		AddValidator(JitOptimizationsValidator.FailOnError);
		AddValidator(RunModeValidator.FailOnError);
		AddValidator(GenericBenchmarksValidator.DontFailOnError);
	}
}
