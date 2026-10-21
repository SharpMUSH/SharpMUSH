using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Implementation;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics.Measurements;

/// <summary>
/// What one measurement process does. <c>build</c> loads a dataset into a new issued fixture world and leaves
/// it for later processes; <c>measure</c> adopts that world in a fresh process and runs each rate twice: the
/// first pass right after process start (the OS page cache is not dropped), then a second, warm pass.
/// </summary>
internal sealed record SemanticLoadSpec(string Mode, string Label, string Dataset, string Output, string? World,
	int[] Rates, int PhaseSeconds, double CommandsPerSecond, bool Rebuild, bool Final);

/// <summary>Skipped unless <paramref name="variable"/> names a spec file.</summary>
internal sealed class RequiresEnvironmentAttribute(string variable) : SkipAttribute($"Runs only when {variable} names a measurement spec.")
{
	public override Task<bool> ShouldSkip(TestRegisteredContext context)
		=> Task.FromResult(Environment.GetEnvironmentVariable(variable) is null);
}

/// <summary>
/// The fixture side of the 5c measurements: semantic query load at a fixed open-loop rate against a
/// fixture-backed world, with ordinary commands through the real command queue alongside, and the storage,
/// memory and GC figures sampled each second. Results are recorded, not judged: nothing here asserts a bound.
/// </summary>
public class SemanticLoadMeasurement
{
	internal const string Variable = "SHARPMUSH_SEMANTIC_LOAD";

	[ClassDataSource<NatsTestServer>(Shared = SharedType.PerTestSession)]
	public required NatsTestServer NatsTestServer { get; init; }
	[ClassDataSource<MySqlTestServer>(Shared = SharedType.PerTestSession)]
	public required MySqlTestServer MySqlTestServer { get; init; }

	private static string ScratchRoot => Path.Join(Path.GetTempPath(), "semantic-load");
	private static string ConfigFile => Path.Join(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst");
	private static readonly JsonSerializerOptions Json = new()
	{
		WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
	};

	private sealed record DatasetPage(string Id, DatasetKey Key, string Title, string[]? Categories);
	private sealed record DatasetKey(string Package, string Namespace, string SourceKey);
	private sealed record DatasetFact(string Subject, string Property, string Type, JsonElement Value, FactProvenance Provenance);
	private sealed record DatasetRule(string Scope, string Key, string[] Read);
	private sealed record DatasetMeta(string? HotTarget, string[]? Readers);

	[Test]
	[RequiresEnvironment(Variable)]
	public async Task Run()
	{
		var spec = JsonSerializer.Deserialize<SemanticLoadSpec>(File.ReadAllText(Environment.GetEnvironmentVariable(Variable)!))!;
		Directory.CreateDirectory(spec.Output);
		Environment.SetEnvironmentVariable("NATS_URL", $"nats://localhost:{NatsTestServer.Instance.GetMappedPublicPort(4222)}");
		if (spec.Mode == "build") await BuildAsync(spec);
		else await MeasureAsync(spec);
	}

	private static IEnumerable<T> Lines<T>(string path)
	{
		foreach (var line in File.ReadLines(path))
			if (!string.IsNullOrWhiteSpace(line)) yield return JsonSerializer.Deserialize<T>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
	}

	private async Task BuildAsync(SemanticLoadSpec spec)
	{
		var clock = Stopwatch.StartNew();
		var host = new SemanticWikiPrototypeHost(ScratchRoot, MySqlTestServer.Instance.GetConnectionString(), ConfigFile, null, "mysql", null);
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var timings = new Dictionary<string, double> { ["open"] = clock.Elapsed.TotalSeconds };

		// Activation bootstraps at most 128 existing pages, so the projection is activated first and every
		// dataset page then commits through it, as an edit on an active world does.
		var step = Stopwatch.StartNew();
		string? activationFailure = null;
		try { await host.ActivateAtomicWikiProjectionAsync(); }
		catch (Exception error) when (error is WikiFixtureMutationException or InvalidOperationException) { activationFailure = error.ToString(); }
		timings["activate"] = step.Elapsed.TotalSeconds;

		var rulesPath = Path.Join(spec.Dataset, "rules.json");
		var rules = File.Exists(rulesPath) ? JsonSerializer.Deserialize<DatasetRule[]>(File.ReadAllText(rulesPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })! : [];
		foreach (var rule in rules)
			await wiki.SetRequirementsAsync(new WikiRuleTarget(Enum.Parse<WikiRuleScope>(rule.Scope, true), rule.Key),
				new Dictionary<WikiAction, IReadOnlyList<string>> { [WikiAction.Read] = rule.Read }, "#1");

		step.Restart();
		var ids = new Dictionary<string, string>(StringComparer.Ordinal);
		var refused = 0;
		foreach (var page in Lines<DatasetPage>(Path.Join(spec.Dataset, "pages.jsonl")))
		{
			var ns = WikiHelpers.ParseNamespace(page.Key.Namespace) ?? WikiNamespace.Main;
			if ((await wiki.CreateAsync(page.Title, $"# {page.Title}", "#1", ns, null, page.Categories ?? [], slug: page.Key.SourceKey)).Value is WikiPage created)
				ids[page.Id] = created.Id;
			else refused++;
		}
		timings["create"] = step.Elapsed.TotalSeconds;

		var imported = 0;
		var facts = 0;
		if (activationFailure is null)
		{
			step.Restart();
			var bySubject = Lines<DatasetFact>(Path.Join(spec.Dataset, "facts.jsonl")).GroupBy(f => f.Subject);
			var batch = new List<SemanticFixtureFactImport>();
			foreach (var group in bySubject)
			{
				if (!ids.TryGetValue(group.Key, out var subject)) continue;
				var converted = group.Select(f => Convert(f, subject, ids)).OfType<SemanticFact>().ToImmutableArray();
				if (converted.IsEmpty) continue;
				facts += converted.Length;
				batch.Add(new(subject, new(AtomicWikiLayout.Schema.Version, converted)));
				if (batch.Count == 256) { imported += await provider.ImportSemanticFixtureFactsAsync(batch, CancellationToken.None); batch.Clear(); }
			}
			if (batch.Count > 0) imported += await provider.ImportSemanticFixtureFactsAsync(batch, CancellationToken.None);
			timings["import"] = step.Elapsed.TotalSeconds;
		}

		var meta = Path.Join(spec.Dataset, "meta.json");
		var hot = File.Exists(meta) ? JsonSerializer.Deserialize<DatasetMeta>(File.ReadAllText(meta), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.HotTarget : null;
		var storage = host.Services.GetRequiredService<IStorageCapacityService>().Measure();
		await File.WriteAllTextAsync(Path.Join(spec.Output, "build.json"), JsonSerializer.Serialize(new
		{
			spec.Label,
			host.WorldPath,
			host.FixtureId,
			Pages = ids.Count,
			Refused = refused,
			ImportedPages = imported,
			Facts = facts,
			ActivationFailure = activationFailure,
			Timings = timings,
			HotTarget = hot is not null && ids.TryGetValue(hot, out var hotId) ? hotId : null,
			Storage = new { storage.MapSizeBytes, storage.FileBytes, storage.AllocatedBytes, storage.LiveBytes, storage.FreeBytes },
			Ids = Path.Join(spec.Output, "ids.json")
		}, Json));
		await File.WriteAllTextAsync(Path.Join(spec.Output, "ids.json"), JsonSerializer.Serialize(ids));
		// The world stays for the measurement processes: the host closes, its world is not removed.
		await host.CloseAsync();
	}

	private static SemanticFact? Convert(DatasetFact fact, string subject, IReadOnlyDictionary<string, string> ids)
	{
		SemanticValue? value = fact.Type switch
		{
			"Integer" => fact.Value.GetInt64(),
			"Keyword" => new Keyword(fact.Value.GetString()!),
			"Boolean" => fact.Value.GetBoolean(),
			"PageRef" => ids.TryGetValue(fact.Value.GetString()!, out var target) ? new PageRef(target) : null,
			_ => null
		};
		return value is { } v ? new SemanticFact(new(subject), fact.Property, v, fact.Provenance) : null;
	}

	private static readonly WikiReader[] Readers =
	[
		WikiReader.From(["wiki.read"], "#5"),
		WikiReader.From(["wiki.read", "char.read"], "#5"),
		WikiReader.From(["wiki.read", "char.read", "secret.read", "wiki.drafts"], "#5"),
		WikiReader.From(["wiki.read", "wiki.admin", "wiki.drafts"], "#1")
	];

	private sealed record QuerySample(string Pass, int Rate, string Kind, int Reader, double ScheduledMs, double StartedMs, double LatencyMs, string Outcome);
	private sealed record CommandSample(string Pass, int Rate, string Command, double ScheduledMs, double LatencyMs, string Outcome);

	private async Task MeasureAsync(SemanticLoadSpec spec)
	{
		var process = Stopwatch.StartNew();
		var build = JsonDocument.Parse(File.ReadAllText(Path.Join(spec.World!, "build.json"))).RootElement;
		var world = build.GetProperty("WorldPath").GetString()!;
		var hot = build.TryGetProperty("HotTarget", out var hotTarget) && hotTarget.ValueKind == JsonValueKind.String ? hotTarget.GetString() : null;
		var ids = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Join(spec.World!, "ids.json")))!;
		var host = SemanticWikiPrototypeHost.AdoptAfterOwnerExit(world, MySqlTestServer.Instance.GetConnectionString(), ConfigFile);
		await host.OpenAsync();
		var opened = process.Elapsed.TotalSeconds;
		var services = host.Services;
		var provider = services.GetRequiredService<LightningDatabase>();
		var targets = ClassTargets(spec.Dataset, ids);
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(services, services.GetRequiredService<IMediator>(),
			services.GetRequiredService<IConnectionService>(), "SemanticLoad");
		var summaries = new List<object>();
		foreach (var rate in spec.Rates)
			foreach (var pass in new[] { "cold", "warm" })
				summaries.Add(await PhaseAsync(spec, host, provider, player, rate, pass, targets, hot, opened));
		await File.WriteAllTextAsync(Path.Join(spec.Output, "summary.json"), JsonSerializer.Serialize(new { spec.Label, OpenSeconds = opened, Phases = summaries }, Json));
		if (spec.Final) await host.DisposeAsync();
		else await host.CloseAsync();
	}

	private static string[] ClassTargets(string dataset, IReadOnlyDictionary<string, string> ids)
		=> [.. Lines<DatasetFact>(Path.Join(dataset, "facts.jsonl")).Where(f => f.Property == "pf2e:class" && f.Type == "PageRef")
			.Select(f => f.Value.GetString()!).Distinct().Select(id => ids.GetValueOrDefault(id)).OfType<string>().Take(256)];

	private static SemanticWikiQuery QueryFor(int k, string[] targets, string? hot)
	{
		var low = 1 + k % 18;
		return (k % 5) switch
		{
			0 => new("game", [new SemanticPredicate.PageEquals("pf2e:class", hot is not null && k % 10 == 0 ? hot : targets.Length > 0 ? targets[k % targets.Length] : "wiki_page/1")], ["pf2e:level"], 7),
			1 => new("game", [new SemanticPredicate.IntegerRange("pf2e:level", low, low + 3)], ["pf2e:level"], 7),
			2 => new("game", [new SemanticPredicate.KeywordEquals("pf2e:tradition", new[] { "arcane", "divine", "occult", "primal" }[k % 4])], ["pf2e:level"], 7),
			3 => new("game", [new SemanticPredicate.KeywordEquals("pf2e:tradition", "arcane"), new SemanticPredicate.IntegerRange("pf2e:level", low, low + 2)], ["pf2e:level"], 7),
			_ => SemanticLists.ArcaneByLevel.Query
		};
	}

	private static readonly string[] QueryKinds = ["Q1-relationship", "Q2-range", "Q2-keyword", "Q3-conjunction", "Q4-list"];
	private static readonly string[] Commands = ["look", "say measuring", "pose measures", "@set me=SEMANTICLOAD:{0}"];

	private async Task<object> PhaseAsync(SemanticLoadSpec spec, SemanticWikiPrototypeHost host, LightningDatabase provider,
		TestIsolationHelpers.TestPlayer player, int rate, string pass, string[] targets, string? hot, double openSeconds)
	{
		var services = host.Services;
		var scheduler = services.GetRequiredService<ITaskScheduler>();
		var connections = services.GetRequiredService<IConnectionService>();
		var capacity = services.GetRequiredService<IStorageCapacityService>();
		var parser = new MUSHCodeParser(services.GetRequiredService<ILogger<MUSHCodeParser>>(),
			services.GetRequiredService<LibraryService<string, FunctionDefinition>>(),
			services.GetRequiredService<LibraryService<string, CommandDefinition>>(),
			services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>(), services,
			state: ParserState.RootFor(player.DbRef) with { Handle = player.Handle, Flags = ParserStateFlags.DirectInput });
		var queries = new ConcurrentBag<QuerySample>();
		var commands = new ConcurrentBag<CommandSample>();
		var histograms = new ConcurrentDictionary<string, ConcurrentBag<double>>();
		using var listener = new MeterListener();
		listener.InstrumentPublished = (instrument, l) =>
		{
			if (instrument.Meter.Name == "SharpMUSH" && instrument.Name.StartsWith("sharpmush.storage.", StringComparison.Ordinal) && instrument is Histogram<double>)
				l.EnableMeasurementEvents(instrument);
		};
		listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => histograms.GetOrAdd(instrument.Name, _ => []).Add(value));
		listener.Start();

		var duration = TimeSpan.FromSeconds(spec.PhaseSeconds);
		var phase = Stopwatch.StartNew();
		var gcPauseStart = GC.GetTotalPauseDuration();
		var allocatedStart = GC.GetTotalAllocatedBytes(true);
		var (minorStart, majorStart) = Faults();
		var samples = new StringBuilder("seconds,working_set,private_bytes,minor_faults,major_faults,allocated,gc_pause_ms,gen0,gen1,gen2,writer_queued,writer_waits,writer_holds,reads_active,reads_peak,reads,map_bytes,file_bytes,live_bytes,free_bytes,journal_entries\n");
		var inflight = new ConcurrentBag<Task>();
		using var stop = new CancellationTokenSource(duration);

		async Task QueryLoop()
		{
			if (rate <= 0) return;
			for (var k = 0; !stop.IsCancellationRequested; k++)
			{
				var due = TimeSpan.FromSeconds(k / (double)rate);
				if (!await WaitUntil(phase, due, stop.Token)) break;
				var index = k;
				inflight.Add(Task.Run(async () =>
				{
					var started = phase.Elapsed.TotalMilliseconds;
					var reader = Readers[index % Readers.Length];
					string outcome;
					try
					{
						var result = await SemanticWikiQueries.RunAsync(provider, $"#player{index % 64}", _ => ValueTask.FromResult(reader), QueryFor(index, targets, hot), null, CancellationToken.None);
						outcome = result switch
						{
							SemanticQueryResult.NotCertified { Outcome: var o } => "NotCertified." + o.GetType().Name,
							_ => result.GetType().Name
						};
					}
					catch (Exception error) { outcome = "Exception." + error.GetType().Name; }
					queries.Add(new(pass, rate, QueryKinds[index % 5], index % Readers.Length, due.TotalMilliseconds, started,
						phase.Elapsed.TotalMilliseconds - due.TotalMilliseconds, outcome));
				}));
			}
		}

		async Task CommandLoop()
		{
			if (spec.CommandsPerSecond <= 0) return;
			for (var k = 0; !stop.IsCancellationRequested; k++)
			{
				var due = TimeSpan.FromSeconds(k / spec.CommandsPerSecond);
				if (!await WaitUntil(phase, due, stop.Token)) break;
				var text = string.Format(CultureInfo.InvariantCulture, Commands[k % Commands.Length], k);
				var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				var admitted = await scheduler.AdmitSocketWork(async () =>
				{
					await parser.CommandParse(player.Handle, connections, MarkupText.Plain(text));
					return null;
				}, "semantic-load", "semantic-load", () => released.TrySetResult());
				var name = text.Split(' ')[0];
				if (!admitted.Accepted) { commands.Add(new(pass, rate, name, due.TotalMilliseconds, 0, "Refused")); continue; }
				inflight.Add(released.Task.ContinueWith(_ => commands.Add(new(pass, rate, name, due.TotalMilliseconds,
					phase.Elapsed.TotalMilliseconds - due.TotalMilliseconds, "Completed")), System.Threading.Tasks.TaskScheduler.Default));
			}
		}

		async Task SampleLoop()
		{
			using var process = Process.GetCurrentProcess();
			while (!stop.IsCancellationRequested)
			{
				process.Refresh();
				var (minor, major) = Faults();
				var writer = provider.Store.WriterStatistics;
				var reads = provider.Store.ReaderStatistics;
				var storage = capacity.Measure();
				var journal = provider.Store.Read(tx => tx.TryGet(AtomicWikiLayout.Metadata, [SemanticRebuildJournal.CountKey], out var count) && count.Length == 8
					? System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(count) : 0UL);
				samples.Append(CultureInfo.InvariantCulture, $"{phase.Elapsed.TotalSeconds:F1},{process.WorkingSet64},{process.PrivateMemorySize64},{minor - minorStart},{major - majorStart},")
					.Append(CultureInfo.InvariantCulture, $"{GC.GetTotalAllocatedBytes() - allocatedStart},{(GC.GetTotalPauseDuration() - gcPauseStart).TotalMilliseconds:F2},")
					.Append(CultureInfo.InvariantCulture, $"{GC.CollectionCount(0)},{GC.CollectionCount(1)},{GC.CollectionCount(2)},{writer.Queued},{writer.Waits},{writer.Holds},")
					.Append(CultureInfo.InvariantCulture, $"{reads.Active},{reads.PeakActive},{reads.Reads},{storage.MapSizeBytes},{storage.FileBytes},{storage.LiveBytes},{storage.FreeBytes},{journal}\n");
				try { await Task.Delay(TimeSpan.FromSeconds(1), stop.Token); } catch (OperationCanceledException) { break; }
			}
		}

		object? rebuild = null;
		async Task RebuildAtMidpoint()
		{
			if (!spec.Rebuild || pass != "warm") return;
			try { await Task.Delay(duration / 2, stop.Token); } catch (OperationCanceledException) { return; }
			var active = AtomicWikiLayout.ForGeneration(provider.ActiveSemanticGeneration());
			var subjects = provider.Store.Read(tx => tx.Count(active.Summaries));
			var started = phase.Elapsed.TotalSeconds;
			var clock = Stopwatch.StartNew();
			string outcome;
			// A rebuild that fails is a result to record, not a reason to lose the phase's other measurements.
			try { outcome = (await provider.RebuildSemanticProjectionAsync(null, CancellationToken.None)).ToString(); }
			catch (Exception error) { outcome = "Failed: " + error.GetType().Name + ": " + error.Message; }
			rebuild = new
			{
				StartedAt = started,
				Seconds = clock.Elapsed.TotalSeconds,
				Subjects = subjects,
				SubjectsPerSecond = subjects / Math.Max(clock.Elapsed.TotalSeconds, 1e-9),
				Outcome = outcome
			};
		}

		await Task.WhenAll(QueryLoop(), CommandLoop(), SampleLoop(), RebuildAtMidpoint());
		var outstanding = Task.WhenAll(inflight);
		var drained = await Task.WhenAny(outstanding, Task.Delay(TimeSpan.FromMinutes(2))) == outstanding;
		listener.Dispose();

		var directory = Path.Join(spec.Output, $"rate-{rate}-{pass}");
		Directory.CreateDirectory(directory);
		await File.WriteAllTextAsync(Path.Join(directory, "samples.csv"), samples.ToString());
		await File.WriteAllLinesAsync(Path.Join(directory, "queries.csv"), ["pass,rate,kind,reader,scheduled_ms,started_ms,latency_ms,outcome",
			.. queries.OrderBy(q => q.ScheduledMs).Select(q => string.Join(',', q.Pass, q.Rate, q.Kind, q.Reader,
				q.ScheduledMs.ToString("F2", CultureInfo.InvariantCulture), q.StartedMs.ToString("F2", CultureInfo.InvariantCulture),
				q.LatencyMs.ToString("F3", CultureInfo.InvariantCulture), q.Outcome))]);
		await File.WriteAllLinesAsync(Path.Join(directory, "commands.csv"), ["pass,rate,command,scheduled_ms,latency_ms,outcome",
			.. commands.OrderBy(c => c.ScheduledMs).Select(c => string.Join(',', c.Pass, c.Rate, c.Command,
				c.ScheduledMs.ToString("F2", CultureInfo.InvariantCulture), c.LatencyMs.ToString("F3", CultureInfo.InvariantCulture), c.Outcome))]);
		using var self = Process.GetCurrentProcess();
		var (minorEnd, majorEnd) = Faults();
		var summary = new
		{
			Rate = rate,
			Pass = pass,
			Label = pass == "cold" ? "first run after process start, page cache not dropped" : "warm: second pass in the same process",
			OpenSeconds = openSeconds,
			Seconds = phase.Elapsed.TotalSeconds,
			Queries = Percentiles(queries.Select(q => q.LatencyMs)),
			QueryOutcomes = queries.GroupBy(q => q.Outcome).ToDictionary(g => g.Key, g => g.Count()),
			QueriesByKind = queries.GroupBy(q => q.Kind).ToDictionary(g => g.Key, g => Percentiles(g.Select(q => q.LatencyMs))),
			Commands = Percentiles(commands.Where(c => c.Outcome == "Completed").Select(c => c.LatencyMs)),
			CommandsRefused = commands.Count(c => c.Outcome != "Completed"),
			CommandsByKind = commands.Where(c => c.Outcome == "Completed").GroupBy(c => c.Command).ToDictionary(g => g.Key, g => Percentiles(g.Select(c => c.LatencyMs))),
			WriterWaitMs = Percentiles(histograms.GetValueOrDefault("sharpmush.storage.writer.wait") ?? []),
			WriterHoldMs = Percentiles(histograms.GetValueOrDefault("sharpmush.storage.writer.hold") ?? []),
			ReadTransactionMs = Percentiles(histograms.GetValueOrDefault("sharpmush.storage.read.duration") ?? []),
			PeakReadsActive = provider.Store.ReaderStatistics.PeakActive,
			PeakWorkingSet = self.PeakWorkingSet64,
			MinorFaults = minorEnd - minorStart,
			MajorFaults = majorEnd - majorStart,
			AllocatedBytes = GC.GetTotalAllocatedBytes() - allocatedStart,
			GcPauseMs = (GC.GetTotalPauseDuration() - gcPauseStart).TotalMilliseconds,
			Storage = capacity.Measure() is var s ? new { s.MapSizeBytes, s.FileBytes, s.AllocatedBytes, s.LiveBytes, s.FreeBytes } : null,
			Rebuild = rebuild,
			Drained = drained
		};
		await File.WriteAllTextAsync(Path.Join(directory, "summary.json"), JsonSerializer.Serialize(summary, Json));
		return summary;
	}

	/// <summary>Waits until <paramref name="clock"/> reaches <paramref name="due"/>; a timer may wake early, so it never returns before.</summary>
	private static async Task<bool> WaitUntil(Stopwatch clock, TimeSpan due, CancellationToken stop)
	{
		while (clock.Elapsed < due)
		{
			var wait = due - clock.Elapsed;
			try { await Task.Delay(wait > TimeSpan.FromMilliseconds(2) ? wait - TimeSpan.FromMilliseconds(1) : TimeSpan.FromMilliseconds(1), stop); }
			catch (OperationCanceledException) { return false; }
		}
		return true;
	}

	private static (long Minor, long Major) Faults()
	{
		// /proc/self/stat: the command name is parenthesised and may hold spaces; fields count from after it.
		var stat = File.ReadAllText("/proc/self/stat");
		var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
		return (long.Parse(fields[7], CultureInfo.InvariantCulture), long.Parse(fields[9], CultureInfo.InvariantCulture));
	}

	private static object Percentiles(IEnumerable<double> values)
	{
		var sorted = values.Order().ToArray();
		double At(double p) => sorted.Length == 0 ? double.NaN : sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1)];
		return new { Count = sorted.Length, P50 = At(0.50), P95 = At(0.95), P99 = At(0.99), Max = sorted.Length == 0 ? double.NaN : sorted[^1] };
	}
}
