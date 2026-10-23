using System.Text.Json;
using SharpMUSH.Tools.WikiSemantics.Native;

// Disposable test process only; no production registration, arbitrary fault hooks or native-file edits.
var options = new Dictionary<string, string>(StringComparer.Ordinal);
if (args.Length % 2 != 0) throw new ArgumentException("option/value pairs required");
for (var i = 0; i < args.Length; i += 2)
	if (!options.TryAdd(args[i], args[i + 1])) throw new ArgumentException("Duplicate option");
if (options.Keys.Any(k => k is not ("--scenario" or "--world" or "--evidence"))) throw new ArgumentException("Unknown option");
var scenario = options["--scenario"];
var world = Path.GetFullPath(options["--world"]);
var evidence = Path.GetFullPath(options["--evidence"]);
if (world == evidence || evidence.StartsWith(world + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new ArgumentException("Evidence must be outside world");
using var watchdog = new Timer(_ => Environment.Exit(124), null, TimeSpan.FromSeconds(90), Timeout.InfiniteTimeSpan);
void Mark(string phase)
{
	Console.WriteLine(JsonSerializer.Serialize(new { phase, pid = Environment.ProcessId, scenario, world }));
	Console.Out.Flush();
}
if (scenario is "category-commit-failure-initial" or "category-commit-failure-resume")
{
	await CategoryCommitFailureScenario.RunAsync(world, evidence, scenario == "category-commit-failure-resume", phase => Mark(phase));
	return;
}
if (scenario is "category-group-commit-failure-initial" or "category-group-commit-failure-resume")
{
	await CategoryCommitFailureScenario.RunGroupedAsync(world, evidence, scenario == "category-group-commit-failure-resume", phase => Mark(phase));
	return;
}
if (scenario == "cipher-cursor-error")
{
	CipherCursorErrorScenario.Run(world, evidence, phase => Mark(phase));
	return;
}
throw new ArgumentException("Unsupported scenario");
