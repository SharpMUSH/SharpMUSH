using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messages;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.NATS;
using StackExchange.Redis;
using System.Diagnostics;
using System.Text;

namespace SharpMUSH.Tests.Performance;

/// <summary>
/// In-process side-by-side comparison of Kafka+Redis versus NATS JetStream.
/// Measures wall-clock throughput, single-message latency, CPU time, and GC
/// allocations for both the <see cref="IMessageBus"/> and
/// <see cref="IConnectionStateStore"/> abstractions.
///
/// All <c>[Explicit]</c> tests spin up real containers and should only be run
/// when specifically requested. The two always-on tests do a quick smoke-check
/// to confirm both transports are healthy and produce comparable output.
/// </summary>
[NotInParallel]
public class TransportComparisonBenchmarks
{
	// Kafka + Redis come from the standard server factory (already fully wired).
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactory { get; init; }

	[ClassDataSource<NatsTestServer>(Shared = SharedType.PerTestSession)]
	public required NatsTestServer NatsTestServer { get; init; }

	[ClassDataSource<RedisTestServer>(Shared = SharedType.PerTestSession)]
	public required RedisTestServer RedisTestServer { get; init; }

	// ── helpers ─────────────────────────────────────────────────────────────

	private IMessageBus KafkaBus => WebAppFactory.Services.GetRequiredService<IMessageBus>();

	private async Task<NatsJetStreamMessageBus> CreateNatsBusAsync()
	{
		var port = NatsTestServer.Instance.GetMappedPublicPort(4222);
		var options = new NatsOptions { Url = $"nats://localhost:{port}" };
		var logger = new LoggerFactory().CreateLogger<NatsJetStreamMessageBus>();
		return await NatsJetStreamMessageBus.CreateAsync(options, logger);
	}

	private IConnectionStateStore CreateRedisStore()
	{
		var port = RedisTestServer.Instance.GetMappedPublicPort(6379);
		var configuration = ConfigurationOptions.Parse($"localhost:{port}");
		configuration.AbortOnConnectFail = false;
		configuration.ConnectRetry = 3;
		configuration.ConnectTimeout = 5000;
		var redis = ConnectionMultiplexer.Connect(configuration);
		var logger = new LoggerFactory().CreateLogger<RedisConnectionStateStore>();
		return new RedisConnectionStateStore(redis, logger);
	}

	private async Task<IConnectionStateStore> CreateNatsStoreAsync()
	{
		var port = NatsTestServer.Instance.GetMappedPublicPort(4222);
		var logger = new LoggerFactory().CreateLogger<NatsConnectionStateStore>();
		return await NatsConnectionStateStore.CreateAsync($"nats://localhost:{port}", logger);
	}

	private static ConnectionStateData MakeState(long handle) => new()
	{
		Handle = handle,
		PlayerRef = new DBRef((int)(handle % 1000)),
		State = "LoggedIn",
		IpAddress = "10.0.0.1",
		Hostname = "bench.test",
		ConnectionType = "telnet",
		ConnectedAt = DateTimeOffset.UtcNow,
		LastSeen = DateTimeOffset.UtcNow,
		Metadata = new Dictionary<string, string> { { "bench", "true" } }
	};

	private static (long cpuMs, long allocBytes) Snapshot()
	{
		var cpu = (long)Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds;
		var alloc = GC.GetTotalAllocatedBytes(precise: true);
		return (cpu, alloc);
	}

	private static void PrintStats(
		string label,
		long wallMs,
		long cpuMs,
		long allocBytes,
		int count)
	{
		Console.WriteLine($"  {label,-35} wall={wallMs,6}ms  cpu={cpuMs,5}ms  alloc={allocBytes / 1024.0:F1}KB  " +
		                  $"throughput={(count * 1000.0 / Math.Max(wallMs, 1)):F0} ops/s");
	}

	// ── MessageBus comparison ────────────────────────────────────────────────

	/// <summary>
	/// Publishes 1 000 messages sequentially through each bus and compares
	/// wall-clock time, CPU consumption, and GC allocations.
	/// </summary>
	[Test, Explicit]
	public async Task MessageBus_SequentialThroughput_KafkaVsNats()
	{
		const int count = 1_000;
		var messages = Enumerable.Range(0, count)
			.Select(i => new TelnetOutputMessage(1, Encoding.UTF8.GetBytes($"Bench message {i}")))
			.ToList();

		Console.WriteLine($"=== MessageBus sequential throughput ({count} messages) ===");

		// ── Kafka ──
		GC.Collect(2, GCCollectionMode.Forced, blocking: true);
		var (cpuBefore, allocBefore) = Snapshot();
		var sw = Stopwatch.StartNew();
		foreach (var m in messages)
			await KafkaBus.Publish(m);
		sw.Stop();
		var (cpuAfter, allocAfter) = Snapshot();
		var kafkaWall = sw.ElapsedMilliseconds;
		var kafkaCpu = cpuAfter - cpuBefore;
		var kafkaAlloc = allocAfter - allocBefore;
		PrintStats("Kafka (sequential)", kafkaWall, kafkaCpu, kafkaAlloc, count);

		// ── NATS ──
		await using var natsBus = await CreateNatsBusAsync();
		GC.Collect(2, GCCollectionMode.Forced, blocking: true);
		(cpuBefore, allocBefore) = Snapshot();
		sw.Restart();
		foreach (var m in messages)
			await natsBus.Publish(m);
		sw.Stop();
		(cpuAfter, allocAfter) = Snapshot();
		var natsWall = sw.ElapsedMilliseconds;
		var natsCpu = cpuAfter - cpuBefore;
		var natsAlloc = allocAfter - allocBefore;
		PrintStats("NATS  (sequential)", natsWall, natsCpu, natsAlloc, count);

		// ── Summary ──
		Console.WriteLine();
		Console.WriteLine($"  Wall-clock ratio  (NATS / Kafka): {(double)natsWall  / Math.Max(kafkaWall, 1):F2}x");
		Console.WriteLine($"  CPU ratio         (NATS / Kafka): {(double)natsCpu   / Math.Max(kafkaCpu,  1):F2}x");
		Console.WriteLine($"  Allocation ratio  (NATS / Kafka): {(double)natsAlloc / Math.Max(kafkaAlloc,1):F2}x");

		// Both must complete within 30 s (generous upper bound for CI containers)
		await Assert.That(kafkaWall).IsLessThan(30_000).Because("Kafka sequential publish must complete within 30s");
		await Assert.That(natsWall).IsLessThan(30_000).Because("NATS sequential publish must complete within 30s");
	}

	/// <summary>
	/// Publishes 1 000 messages concurrently through each bus and compares
	/// wall-clock time, CPU consumption, and GC allocations.
	/// </summary>
	[Test, Explicit]
	public async Task MessageBus_ConcurrentThroughput_KafkaVsNats()
	{
		const int count = 1_000;
		var messages = Enumerable.Range(0, count)
			.Select(i => new TelnetOutputMessage(1, Encoding.UTF8.GetBytes($"Concurrent bench {i}")))
			.ToList();

		Console.WriteLine($"=== MessageBus concurrent throughput ({count} messages) ===");

		// ── Kafka ──
		GC.Collect(2, GCCollectionMode.Forced, blocking: true);
		var (cpuBefore, allocBefore) = Snapshot();
		var sw = Stopwatch.StartNew();
		await Task.WhenAll(messages.Select(m => KafkaBus.Publish(m)));
		sw.Stop();
		var (cpuAfter, allocAfter) = Snapshot();
		var kafkaWall = sw.ElapsedMilliseconds;
		PrintStats("Kafka (concurrent)", kafkaWall, cpuAfter - cpuBefore, allocAfter - allocBefore, count);

		// ── NATS ──
		await using var natsBus = await CreateNatsBusAsync();
		GC.Collect(2, GCCollectionMode.Forced, blocking: true);
		(cpuBefore, allocBefore) = Snapshot();
		sw.Restart();
		await Task.WhenAll(messages.Select(m => natsBus.Publish(m)));
		sw.Stop();
		(cpuAfter, allocAfter) = Snapshot();
		var natsWall = sw.ElapsedMilliseconds;
		PrintStats("NATS  (concurrent)", natsWall, cpuAfter - cpuBefore, allocAfter - allocBefore, count);

		Console.WriteLine();
		Console.WriteLine($"  Wall-clock ratio  (NATS / Kafka): {(double)natsWall / Math.Max(kafkaWall, 1):F2}x");

		await Assert.That(kafkaWall).IsLessThan(30_000).Because("Kafka concurrent publish must complete within 30s");
		await Assert.That(natsWall).IsLessThan(30_000).Because("NATS concurrent publish must complete within 30s");
	}

	/// <summary>
	/// Warms up each bus then measures single-message round-trip latency.
	/// </summary>
	[Test, Explicit]
	public async Task MessageBus_SingleMessageLatency_KafkaVsNats()
	{
		const int warmupCount = 5;
		const int sampleCount = 20;
		var msg = new TelnetOutputMessage(1, Encoding.UTF8.GetBytes("Latency probe"));

		Console.WriteLine($"=== MessageBus single-message latency (p50/p95 over {sampleCount} samples after {warmupCount} warmups) ===");

		// ── Kafka warm-up ──
		for (var i = 0; i < warmupCount; i++)
			await KafkaBus.Publish(msg);
		await Task.Delay(50);

		var kafkaSamples = new List<long>(sampleCount);
		for (var i = 0; i < sampleCount; i++)
		{
			var sw = Stopwatch.StartNew();
			await KafkaBus.Publish(msg);
			sw.Stop();
			kafkaSamples.Add(sw.ElapsedMilliseconds);
			await Task.Delay(5);
		}

		// ── NATS warm-up ──
		await using var natsBus = await CreateNatsBusAsync();
		for (var i = 0; i < warmupCount; i++)
			await natsBus.Publish(msg);
		await Task.Delay(50);

		var natsSamples = new List<long>(sampleCount);
		for (var i = 0; i < sampleCount; i++)
		{
			var sw = Stopwatch.StartNew();
			await natsBus.Publish(msg);
			sw.Stop();
			natsSamples.Add(sw.ElapsedMilliseconds);
			await Task.Delay(5);
		}

		kafkaSamples.Sort();
		natsSamples.Sort();

		long Percentile(List<long> sorted, double pct) =>
			sorted[(int)Math.Ceiling(pct / 100.0 * sorted.Count) - 1];

		Console.WriteLine($"  Kafka p50={Percentile(kafkaSamples, 50),4}ms  p95={Percentile(kafkaSamples, 95),4}ms  avg={(double)kafkaSamples.Sum() / sampleCount:F1}ms");
		Console.WriteLine($"  NATS  p50={Percentile(natsSamples,  50),4}ms  p95={Percentile(natsSamples,  95),4}ms  avg={(double)natsSamples.Sum()  / sampleCount:F1}ms");

		await Assert.That(Percentile(kafkaSamples, 95)).IsLessThan(500).Because("Kafka p95 latency should be under 500ms");
		await Assert.That(Percentile(natsSamples,  95)).IsLessThan(500).Because("NATS  p95 latency should be under 500ms");
	}

	// ── ConnectionStateStore comparison ─────────────────────────────────────

	/// <summary>
	/// Performs 200 sequential Set+Get operations on each state store and
	/// compares wall-clock time, CPU, and allocations.
	/// </summary>
	[Test, Explicit]
	public async Task StateStore_SequentialThroughput_RedisVsNats()
	{
		const int count = 200;
		Console.WriteLine($"=== ConnectionStateStore sequential Set+Get ({count} ops) ===");

		// ── Redis ──
		var redis = CreateRedisStore();
		GC.Collect(2, GCCollectionMode.Forced, blocking: true);
		var (cpuBefore, allocBefore) = Snapshot();
		var sw = Stopwatch.StartNew();
		for (var i = 0; i < count; i++)
		{
			var h = 200_000L + i;
			await redis.SetConnectionAsync(h, MakeState(h));
			await redis.GetConnectionAsync(h);
		}
		sw.Stop();
		var (cpuAfter, allocAfter) = Snapshot();
		var redisWall = sw.ElapsedMilliseconds;
		PrintStats("Redis (Set+Get)", redisWall, cpuAfter - cpuBefore, allocAfter - allocBefore, count);

		// ── NATS ──
		var nats = await CreateNatsStoreAsync();
		GC.Collect(2, GCCollectionMode.Forced, blocking: true);
		(cpuBefore, allocBefore) = Snapshot();
		sw.Restart();
		for (var i = 0; i < count; i++)
		{
			var h = 300_000L + i;
			await nats.SetConnectionAsync(h, MakeState(h));
			await nats.GetConnectionAsync(h);
		}
		sw.Stop();
		(cpuAfter, allocAfter) = Snapshot();
		var natsWall = sw.ElapsedMilliseconds;
		PrintStats("NATS  (Set+Get)", natsWall, cpuAfter - cpuBefore, allocAfter - allocBefore, count);

		Console.WriteLine();
		Console.WriteLine($"  Wall-clock ratio  (NATS / Redis): {(double)natsWall / Math.Max(redisWall, 1):F2}x");

		await Assert.That(redisWall).IsLessThan(30_000).Because("Redis sequential store ops must complete within 30s");
		await Assert.That(natsWall).IsLessThan(30_000).Because("NATS  sequential store ops must complete within 30s");
	}

	/// <summary>
	/// Runs 10 concurrent metadata-update tasks (CAS loop under contention) on
	/// each store and compares wall-clock time, CPU, and allocations.
	/// </summary>
	[Test, Explicit]
	public async Task StateStore_ConcurrentCas_RedisVsNats()
	{
		const int workers = 10;
		Console.WriteLine($"=== ConnectionStateStore concurrent CAS ({workers} workers) ===");

		// ── Redis ──
		var redis = CreateRedisStore();
		var redisHandle = 400_001L;
		await redis.SetConnectionAsync(redisHandle, MakeState(redisHandle));
		GC.Collect(2, GCCollectionMode.Forced, blocking: true);
		var (cpuBefore, allocBefore) = Snapshot();
		var sw = Stopwatch.StartNew();
		await Task.WhenAll(Enumerable.Range(0, workers).Select(i =>
			redis.UpdateMetadataAsync(redisHandle, $"Key{i}", $"Value{i}")));
		sw.Stop();
		var (cpuAfter, allocAfter) = Snapshot();
		var redisWall = sw.ElapsedMilliseconds;
		PrintStats("Redis (CAS)", redisWall, cpuAfter - cpuBefore, allocAfter - allocBefore, workers);

		// ── NATS ──
		var nats = await CreateNatsStoreAsync();
		var natsHandle = 400_002L;
		await nats.SetConnectionAsync(natsHandle, MakeState(natsHandle));
		GC.Collect(2, GCCollectionMode.Forced, blocking: true);
		(cpuBefore, allocBefore) = Snapshot();
		sw.Restart();
		await Task.WhenAll(Enumerable.Range(0, workers).Select(i =>
			nats.UpdateMetadataAsync(natsHandle, $"Key{i}", $"Value{i}")));
		sw.Stop();
		(cpuAfter, allocAfter) = Snapshot();
		var natsWall = sw.ElapsedMilliseconds;
		PrintStats("NATS  (CAS)", natsWall, cpuAfter - cpuBefore, allocAfter - allocBefore, workers);

		Console.WriteLine();
		Console.WriteLine($"  Wall-clock ratio  (NATS / Redis): {(double)natsWall / Math.Max(redisWall, 1):F2}x");

		// Verify correctness: all 10 keys present in both stores
		var redisResult = await redis.GetConnectionAsync(redisHandle);
		var natsResult  = await nats.GetConnectionAsync(natsHandle);
		for (var i = 0; i < workers; i++)
		{
			await Assert.That(redisResult!.Metadata[$"Key{i}"]).IsEqualTo($"Value{i}");
			await Assert.That(natsResult!.Metadata[$"Key{i}"]).IsEqualTo($"Value{i}");
		}
	}

	// ── End-to-end comparison ────────────────────────────────────────────────

	/// <summary>
	/// Measures wall-clock time, CPU, and allocations for a full end-to-end
	/// round-trip: SetConnectionState + Publish(TelnetOutputMessage) × 100 for
	/// each transport combination (Redis+Kafka vs NATS+NATS).
	/// </summary>
	[Test, Explicit]
	public async Task EndToEnd_RoundTrip_RedisKafkaVsNatsNats()
	{
		const int count = 100;
		Console.WriteLine($"=== End-to-end round-trip: store + publish ({count} iterations) ===");
		Console.WriteLine("  Redis+Kafka  vs  NATS+NATS");
		Console.WriteLine();

		var msgPayload = Encoding.UTF8.GetBytes("E2E benchmark payload");

		// ── Redis + Kafka ──
		var redisStore = CreateRedisStore();
		GC.Collect(2, GCCollectionMode.Forced, blocking: true);
		var (cpuBefore, allocBefore) = Snapshot();
		var sw = Stopwatch.StartNew();
		for (var i = 0; i < count; i++)
		{
			var h = 500_000L + i;
			await redisStore.SetConnectionAsync(h, MakeState(h));
			await KafkaBus.Publish(new TelnetOutputMessage(h, msgPayload));
		}
		sw.Stop();
		var (cpuAfter, allocAfter) = Snapshot();
		var rkWall = sw.ElapsedMilliseconds;
		PrintStats("Redis+Kafka  (e2e)", rkWall, cpuAfter - cpuBefore, allocAfter - allocBefore, count);

		// ── NATS + NATS ──
		var natsStore = await CreateNatsStoreAsync();
		await using var natsBus = await CreateNatsBusAsync();
		GC.Collect(2, GCCollectionMode.Forced, blocking: true);
		(cpuBefore, allocBefore) = Snapshot();
		sw.Restart();
		for (var i = 0; i < count; i++)
		{
			var h = 600_000L + i;
			await natsStore.SetConnectionAsync(h, MakeState(h));
			await natsBus.Publish(new TelnetOutputMessage(h, msgPayload));
		}
		sw.Stop();
		(cpuAfter, allocAfter) = Snapshot();
		var nnWall = sw.ElapsedMilliseconds;
		PrintStats("NATS+NATS    (e2e)", nnWall, cpuAfter - cpuBefore, allocAfter - allocBefore, count);

		Console.WriteLine();
		Console.WriteLine($"  Wall-clock ratio  (NATS+NATS / Redis+Kafka): {(double)nnWall / Math.Max(rkWall, 1):F2}x");

		await Assert.That(rkWall).IsLessThan(60_000).Because("Redis+Kafka e2e must complete within 60s");
		await Assert.That(nnWall).IsLessThan(60_000).Because("NATS+NATS e2e must complete within 60s");
	}

	// ── Always-on smoke tests ────────────────────────────────────────────────

	/// <summary>
	/// Confirms that both the Kafka and NATS buses can publish a single message
	/// without error in the same test run — a fast in-process functional check.
	/// </summary>
	[Test]
	public async Task BothBuses_CanPublishOneMessage()
	{
		var msg = new TelnetOutputMessage(1, Encoding.UTF8.GetBytes("smoke test"));

		// Kafka (via DI)
		await KafkaBus.Publish(msg);

		// NATS (standalone)
		await using var natsBus = await CreateNatsBusAsync();
		await natsBus.Publish(msg);

		Console.WriteLine("[Comparison] Both Kafka and NATS published successfully.");
	}

	/// <summary>
	/// Confirms that both the Redis and NATS state stores can store and retrieve
	/// connection state correctly in the same test run.
	/// </summary>
	[Test]
	public async Task BothStores_CanRoundTripConnectionState()
	{
		const long handle = 999_888L;
		var state = MakeState(handle);

		// Redis
		var redisStore = CreateRedisStore();
		await redisStore.SetConnectionAsync(handle, state);
		var redisResult = await redisStore.GetConnectionAsync(handle);
		await Assert.That(redisResult).IsNotNull();
		await Assert.That(redisResult!.Handle).IsEqualTo(handle);

		// NATS
		var natsStore = await CreateNatsStoreAsync();
		await natsStore.SetConnectionAsync(handle, state);
		var natsResult = await natsStore.GetConnectionAsync(handle);
		await Assert.That(natsResult).IsNotNull();
		await Assert.That(natsResult!.Handle).IsEqualTo(handle);

		Console.WriteLine("[Comparison] Both Redis and NATS state stores round-tripped successfully.");
	}
}
