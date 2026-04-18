using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// Benchmarks for the output pipeline: <c>INotifyService.Notify</c> throughput.
/// Uses a real NATS JetStream container (spun up in <c>BaseBenchmark.Setup</c>) to
/// measure end-to-end message-bus latency from notify call to publish acknowledgement.
/// </summary>
[BenchmarkCategory("Output Pipeline")]
public class NotifyPipelineBenchmarks : BaseBenchmark
{
	private INotifyService? _notifyService;
	private IMUSHCodeParser? _parser;

	private static readonly MString ShortMsg = MModule.single("Hello World");
	private static readonly MString LongMsg = MModule.single(new string('x', 1000));
	private static readonly MString ThinkCmd = MModule.single("think Hello World");
	private static readonly MString PemitCmd = MModule.single("@pemit me=Hello World");

	public NotifyPipelineBenchmarks()
	{
		Setup().ConfigureAwait(false).GetAwaiter().GetResult();
		_notifyService = _server!.Services.GetRequiredService<INotifyService>();
		_parser = TestParser().ConfigureAwait(false).GetAwaiter().GetResult();
	}

	// ── Direct notify calls (no parsing) ──────────────────────────────────────

	[Benchmark(Description = "INotifyService.Notify — single short message")]
	public async Task NotifyShortMessage() =>
		await _notifyService!.Notify(1L, ShortMsg, null);

	[Benchmark(Description = "INotifyService.Notify — single long message (1 KB)")]
	public async Task NotifyLongMessage() =>
		await _notifyService!.Notify(1L, LongMsg, null);

	[Benchmark(Description = "INotifyService.Notify — 10 sequential messages")]
	public async Task Notify10Sequential()
	{
		for (var i = 0; i < 10; i++)
			await _notifyService!.Notify(1L, ShortMsg, null);
	}

	[Benchmark(Description = "INotifyService.Notify — 100 sequential messages")]
	public async Task Notify100Sequential()
	{
		for (var i = 0; i < 100; i++)
			await _notifyService!.Notify(1L, ShortMsg, null);
	}

	// ── End-to-end command path (includes parsing + notify) ───────────────────

	[Benchmark(Description = "CommandParse think — full output path")]
	public async Task ThinkThroughPipeline() =>
		await _parser!.CommandParse(ThinkCmd);

	[Benchmark(Description = "CommandParse @pemit — full output path")]
	public async Task PemitThroughPipeline() =>
		await _parser!.CommandParse(PemitCmd);
}
