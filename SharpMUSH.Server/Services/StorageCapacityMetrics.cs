using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// The capacity report as Prometheus gauges, so an alert can fire before the map, the disk or the backup
/// budget runs out rather than after a write or a backup fails:
/// <list type="bullet">
///   <item><c>sharpmush_storage_bytes{kind=…}</c> — <c>map</c>, <c>file</c>, <c>allocated</c>,
///   <c>live</c>, <c>free_pages</c>, <c>map_headroom</c>, <c>world_disk_free</c>,
///   <c>backup_copies</c>, <c>backup_next_copy</c>, <c>backup_peak</c>, <c>backup_required_free</c>,
///   <c>backup_disk_free</c>, <c>leftover_worlds</c>.</item>
///   <item><c>sharpmush_storage_backup_fits</c> — 1 when the next backup run would pass its free-space
///   check.</item>
///   <item><c>sharpmush_storage_stale_readers_cleared_total</c> — reader slots freed since startup because
///   the process holding them died mid-read.</item>
/// </list>
/// A figure the platform cannot read is left out rather than reported as zero. A hosted service only so
/// the host constructs it at startup; nothing is measured until the first scrape.
/// </summary>
public sealed class StorageCapacityMetrics : IHostedService, IDisposable
{
	/// <summary>A scrape reads several gauges in a row; one measurement serves them all.</summary>
	private static readonly TimeSpan Freshness = TimeSpan.FromSeconds(15);

	private readonly IStorageCapacityService _capacity;
	private readonly TimeProvider _time;
	private readonly Meter _meter;
	private readonly Lock _gate = new();
	private StorageCapacityReport? _last;
	private DateTimeOffset _measuredAt;

	public StorageCapacityMetrics(IStorageCapacityService capacity) : this(capacity, TimeProvider.System)
	{
	}

	public StorageCapacityMetrics(IStorageCapacityService capacity, TimeProvider time)
	{
		_capacity = capacity;
		_time = time;
		// The same meter name the host's OpenTelemetry pipeline already exports.
		_meter = new Meter("SharpMUSH", "1.0.0");
		_meter.CreateObservableGauge("sharpmush.storage.bytes", ObserveBytes, unit: "By",
			description: "World storage capacity: map limit, file length, allocated disk, live data, backup budget");
		_meter.CreateObservableGauge("sharpmush.storage.backup.fits", ObserveFits,
			description: "1 when the next backup run would find enough free disk, 0 when it would refuse to start");
		_meter.CreateObservableCounter("sharpmush.storage.stale_readers.cleared", () => Current().StaleReadersCleared,
			description: "LMDB reader slots freed since startup because the process holding them died mid-read");
	}

	private IEnumerable<Measurement<long>> ObserveBytes()
	{
		var report = Current();
		var backup = report.Backup;
		(string Kind, long Bytes)[] figures =
		[
			("map", report.MapSizeBytes),
			("file", report.FileBytes),
			("allocated", report.AllocatedBytes),
			("live", report.LiveBytes),
			("free_pages", report.FreeBytes),
			("map_headroom", report.MapHeadroomBytes),
			("world_disk_free", report.WorldDiskFreeBytes),
			("backup_copies", backup.CopiesBytes),
			("backup_next_copy", backup.NextCopyBytes),
			("backup_peak", backup.PeakBytes),
			("backup_required_free", backup.RequiredFreeBytes),
			("backup_disk_free", backup.DiskFreeBytes),
			("leftover_worlds", report.Leftovers.Sum(l => Math.Max(l.Bytes, 0)))
		];

		return figures
			.Where(f => f.Bytes >= 0)
			.Select(f => new Measurement<long>(f.Bytes, new KeyValuePair<string, object?>("kind", f.Kind)));
	}

	private int ObserveFits() => Current().Backup.NextRunFits ? 1 : 0;

	private StorageCapacityReport Current()
	{
		lock (_gate)
		{
			var now = _time.GetUtcNow();
			if (_last is null || now - _measuredAt > Freshness)
			{
				_last = _capacity.Measure();
				_measuredAt = now;
			}

			return _last;
		}
	}

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public void Dispose() => _meter.Dispose();
}
