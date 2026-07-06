using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.NATS.Strategy;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// Measures the user-visible output latency added by the always-on replay persist, against real NATS
/// JetStream, comparing the current INLINE path (await the JetStream publish before delivering to the
/// user) with a CHANNEL-DECOUPLED path (deliver immediately; a background drain persists). Prints the
/// numbers and asserts the decoupled path both wins big on delivery latency and still persists everything.
///
/// Marked <see cref="ExplicitAttribute"/>: it asserts an environment-dependent performance ratio against a
/// real NATS container, so it is a manual perf check rather than a CI gate.
/// </summary>
[Explicit]
public class ReplayPersistLatencyBenchmark
{
	private const int Frames = 300;

	private static double Percentile(List<double> sortedMs, double p)
		=> sortedMs[Math.Clamp((int)(p / 100.0 * sortedMs.Count), 0, sortedMs.Count - 1)];

	[Test]
	public async Task Decoupling_persist_removes_the_nats_rtt_from_output_delivery()
	{
		var strategy = new NatsTestContainerStrategy();
		var sInline = Guid.NewGuid().ToString("N");
		var sDecoupled = Guid.NewGuid().ToString("N");
		try
		{
			var url = await strategy.GetUrlAsync();
			var store = await JetStreamTerminalReplayStore.CreateAsync(
				url, NullLogger<JetStreamTerminalReplayStore>.Instance);

			var line = Encoding.UTF8.GetBytes("You see a dusty room. Exits lead north and east. A brass lantern rests here.");

			// Warm up (JIT + JetStream connection + first-publish setup) so we measure steady state.
			for (var i = 0; i < 20; i++) await store.AppendAsync(sInline, line);

			// ---- INLINE (current): delivery is gated by the JetStream publish ack. ----
			var inline = new List<double>(Frames);
			var inlineTotal = Stopwatch.StartNew();
			for (var i = 0; i < Frames; i++)
			{
				var sw = Stopwatch.StartNew();
				var (_, wrapped) = await store.AppendAsync(sInline, line); // await NATS RTT ...
				DeliverToUser(wrapped);                                    // ... then the user sees it
				inline.Add(sw.Elapsed.TotalMilliseconds);
			}
			inlineTotal.Stop();

			// ---- CHANNEL-DECOUPLED: assign seq + deliver now; a background drain persists. ----
			var channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(4096)
			{
				FullMode = BoundedChannelFullMode.Wait,
				SingleReader = true,
				SingleWriter = true
			});
			long seq = 0;
			var persisted = 0;
			var drain = Task.Run(async () =>
			{
				await foreach (var raw in channel.Reader.ReadAllAsync())
				{
					await store.AppendAsync(sDecoupled, raw); // off the delivery path
					Interlocked.Increment(ref persisted);
				}
			});

			var decoupled = new List<double>(Frames);
			var decoupledTotal = Stopwatch.StartNew();
			for (var i = 0; i < Frames; i++)
			{
				var sw = Stopwatch.StartNew();
				var s = Interlocked.Increment(ref seq);
				var wrapped = SeqEnvelope.Wrap(s, line); // in-memory
				DeliverToUser(wrapped);                  // user sees it immediately
				await channel.Writer.WriteAsync(line);   // hand off; drain persists later
				decoupled.Add(sw.Elapsed.TotalMilliseconds);
			}
			decoupledTotal.Stop();
			channel.Writer.Complete();
			await drain; // let the background persist finish

			inline.Sort();
			decoupled.Sort();
			Console.WriteLine("==== Replay persist: output-delivery latency (real NATS JetStream) ====");
			Console.WriteLine($"frames={Frames}  payload={line.Length}B");
			Console.WriteLine($"INLINE    per-frame ms  p50={Percentile(inline, 50):F3}  p99={Percentile(inline, 99):F3}  mean={inline.Average():F3}   burst_total={inlineTotal.Elapsed.TotalMilliseconds:F1}ms");
			Console.WriteLine($"DECOUPLED per-frame ms  p50={Percentile(decoupled, 50):F3}  p99={Percentile(decoupled, 99):F3}  mean={decoupled.Average():F3}   burst_total={decoupledTotal.Elapsed.TotalMilliseconds:F1}ms");
			Console.WriteLine($"speedup(burst)= {inlineTotal.Elapsed.TotalMilliseconds / decoupledTotal.Elapsed.TotalMilliseconds:F1}x   decoupled_persisted={persisted}/{Frames}");

			await store.DisposeAsync();

			// Proof: decoupled delivers the burst dramatically faster ...
			await Assert.That(decoupledTotal.Elapsed.TotalMilliseconds)
				.IsLessThan(inlineTotal.Elapsed.TotalMilliseconds / 3.0);
			// ... its per-frame delivery is far below inline's (no NATS RTT on the path) ...
			await Assert.That(decoupled.Average()).IsLessThan(inline.Average() / 3.0);
			// ... and it still persisted every frame (replay integrity preserved).
			await Assert.That(persisted).IsEqualTo(Frames);
		}
		finally
		{
			await strategy.DisposeAsync();
		}
	}

	// Stand-in for the socket write; identical cost in both paths, so it does not bias the comparison.
	private static void DeliverToUser(byte[] wrapped) => _ = wrapped.Length;
}
