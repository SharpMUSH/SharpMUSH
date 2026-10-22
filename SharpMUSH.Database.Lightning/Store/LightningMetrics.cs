using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SharpMUSH.Database.Lightning.Store;

/// <summary>
/// The store's own timings, on the meter the host already exports:
/// <list type="bullet">
///   <item><c>sharpmush_storage_writer_wait</c> — how long each write job queued before it ran; zero for a job
///   that ran on its caller's thread because nothing was ahead of it.</item>
///   <item><c>sharpmush_storage_writer_hold</c> — how long the writer was held by each batch, raw job or
///   uncontended job, commit included.</item>
///   <item><c>sharpmush_storage_writer_queued</c> — write jobs queued and not yet started.</item>
///   <item><c>sharpmush_storage_read_duration</c> — how long each read transaction stayed open.</item>
///   <item><c>sharpmush_storage_reads_active</c> — read transactions open now, each holding an LMDB reader slot.</item>
/// </list>
/// </summary>
internal static class LightningMetrics
{
	private static readonly Meter Meter = new("SharpMUSH", "1.0.0");

	internal static readonly Histogram<double> WriterWait = Meter.CreateHistogram<double>("sharpmush.storage.writer.wait", "ms",
		"How long a write job queued before it ran");
	internal static readonly Histogram<double> WriterHold = Meter.CreateHistogram<double>("sharpmush.storage.writer.hold", "ms",
		"How long a batch or job held the writer, commit included");
	internal static readonly UpDownCounter<long> WriterQueued = Meter.CreateUpDownCounter<long>("sharpmush.storage.writer.queued",
		description: "Write jobs queued and not yet started");
	internal static readonly Histogram<double> ReadDuration = Meter.CreateHistogram<double>("sharpmush.storage.read.duration", "ms",
		"How long a read transaction stayed open");
	internal static readonly UpDownCounter<long> ReadsActive = Meter.CreateUpDownCounter<long>("sharpmush.storage.reads.active",
		description: "Read transactions open now, each holding an LMDB reader slot");

	internal static double Since(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;
}
