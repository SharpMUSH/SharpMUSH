using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// Profiling harness: <c>dotnet run -c Release -- profile [scenario,...] [--seconds N] [--wait N]</c>.
/// Boots the same host as the benchmarks, then runs each scenario in a tight loop and reports
/// operations per second, managed bytes allocated per operation, and ArangoDB HTTP requests per
/// operation (read from the server's <c>/_admin/statistics</c> counter, so it counts what actually
/// crossed the wire). <c>--wait</c> pauses before the measured loop and prints the PID so
/// <c>dotnet-trace collect -p</c> can attach to a steady-state process.
/// </summary>
public sealed class ProfileHarness : BaseBenchmark
{
	private sealed record Scenario(string Name, string Input, bool IsCommand, bool IsCommandList = false);

	private static readonly Scenario[] Scenarios =
	[
		new("think", "think Hello World", true),
		new("think-subst", "think %#", true),
		new("pemit-fn", "@pemit me=[add(1,2)]", true),
		new("fn1", "[add(1,2)]", false),
		new("fn10", BuildNested(10), false),
		new("cat3", "[cat(%#,%#,%#)]", false),
		new("iter50", "iter(lnum(50),%i0)", false),
		new("mixed", "[switch(%#,#1,[iter(lnum(1,10),[add(##,1)])],other)]", false),
		new("text", "[cat(Hello there, this is a fairly ordinary line of prose that a player might see, with %# in it)]", false),
		new("ufun", "[u(me/PROFILE_FN,5)]", false),
		new("get", "[get(me/PROFILE_FN)]", false),
		new("haspower", "[haspower(me,see_all)]", false),
		// A write invalidates the attribute caches, so the read that follows is a cache miss: this
		// scenario counts what one uncached attribute listing costs on the wire.
		new("set", "&PROFILE_X me=x", true),
		new("set+lattr", "&PROFILE_X me=x;think [lattr(me)]", true, IsCommandList: true),
		new("set+get", "&PROFILE_X me=x;think [get(me/PROFILE_FN)]", true, IsCommandList: true),
	];

	private static string BuildNested(int depth)
	{
		var sb = new StringBuilder();
		for (var i = 0; i < depth; i++) sb.Append("[add(1,");
		sb.Append('1');
		for (var i = 0; i < depth; i++) sb.Append(")]");
		return sb.ToString();
	}

	public static async Task RunAsync(string[] args)
	{
		var seconds = 10;
		var wait = 0;
		var selected = new List<string>();
		for (var i = 0; i < args.Length; i++)
		{
			switch (args[i])
			{
				case "--seconds": seconds = int.Parse(args[++i]); break;
				case "--wait": wait = int.Parse(args[++i]); break;
				default: selected.AddRange(args[i].Split(',', StringSplitOptions.RemoveEmptyEntries)); break;
			}
		}

		var scenarios = selected.Count == 0
			? Scenarios
			: Scenarios.Where(s => selected.Contains(s.Name, StringComparer.OrdinalIgnoreCase)).ToArray();

		var harness = new ProfileHarness();
		await harness.Setup();
		try
		{
			await harness.RunScenariosAsync(scenarios, seconds, wait);
		}
		finally
		{
			await harness.Cleanup();
		}
	}

	private async Task RunScenariosAsync(Scenario[] scenarios, int seconds, int wait)
	{
		var god = (await _database!.GetObjectNodeAsync(new DBRef(1))).AsPlayer!;
		var one = god.Object.DBRef;
		var baseParser = _server!.Services.GetRequiredService<IMUSHCodeParser>();
		await _database.SetAttributeAsync(new DBRef(1), ["PROFILE_FN"], MModule.single("[mul(%0,2)]"), god);

		using var http = new HttpClient { BaseAddress = new Uri(ArangoBaseAddress!) };
		http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
			Convert.ToBase64String(Encoding.ASCII.GetBytes("root:password")));

		async Task<long> ArangoRequestsAsync()
		{
			using var doc = JsonDocument.Parse(await http.GetStringAsync("/_admin/statistics"));
			return doc.RootElement.GetProperty("http").GetProperty("requestsTotal").GetInt64();
		}

		foreach (var scenario in scenarios)
		{
			var input = MModule.single(scenario.Input);
			for (var i = 0; i < 200; i++) await RunOnce(baseParser, one, scenario, input);
		}

		if (wait > 0)
		{
			Console.WriteLine($"READY pid={Environment.ProcessId} - attach now; measured loop starts in {wait}s");
			await Task.Delay(TimeSpan.FromSeconds(wait));
		}

		Console.WriteLine();
		Console.WriteLine($"{"scenario",-12} {"ops/s",10} {"us/op",10} {"KB/op",9} {"arango req/op",14} {"gen0/kop",9}");
		foreach (var scenario in scenarios)
		{
			var input = MModule.single(scenario.Input);
			var reqBefore = await ArangoRequestsAsync();
			var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
			var gen0Before = GC.CollectionCount(0);
			var sw = Stopwatch.StartNew();
			long ops = 0;
			while (sw.Elapsed.TotalSeconds < seconds)
			{
				await RunOnce(baseParser, one, scenario, input);
				ops++;
			}

			sw.Stop();
			var alloc = GC.GetTotalAllocatedBytes(precise: true) - allocBefore;
			var gen0 = GC.CollectionCount(0) - gen0Before;
			// Minus the statistics call itself, which is the one request between the two reads.
			var req = await ArangoRequestsAsync() - reqBefore - 1;
			Console.WriteLine(
				$"{scenario.Name,-12} {ops / sw.Elapsed.TotalSeconds,10:N0} {sw.Elapsed.TotalMilliseconds * 1000 / ops,10:N1} {alloc / 1024.0 / ops,9:N1} {(double)req / ops,14:N2} {gen0 * 1000.0 / ops,9:N2}");
		}
	}

	private static async ValueTask RunOnce(IMUSHCodeParser baseParser, DBRef one, Scenario scenario, MString input)
	{
		// A fresh state per operation, as every player command gets: the invocation counters live on it.
		var parser = baseParser.FromState(BenchmarkHelpers.FreshState(one));
		if (scenario.IsCommandList)
			await parser.CommandListParse(input);
		else if (scenario.IsCommand)
			await parser.CommandParse(input);
		else
			await parser.FunctionParse(input);
	}
}
