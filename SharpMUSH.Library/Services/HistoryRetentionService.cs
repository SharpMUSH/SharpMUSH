using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Runs retention over every registered <see cref="IHistoryStore"/>: a loop of bounded read-then-delete
/// batches per kind, each its own write transaction, with the batch appended to the archive and flushed
/// to disk before the delete is queued — so a configured archive always holds everything the live world
/// no longer does.
/// </summary>
public sealed class HistoryRetentionService(
	IEnumerable<IHistoryStore> stores,
	HistoryRetentionOptions options,
	ILogger<HistoryRetentionService> logger,
	TimeProvider time) : IHistoryRetentionService
{
	private readonly IReadOnlyList<IHistoryStore> _stores = stores.ToArray();

	/// <summary>One pass at a time: the schedule and a wizard's command can arrive together, and two
	/// passes over one kind would only race each other's batches.</summary>
	private readonly SemaphoreSlim _onePass = new(1, 1);

	public HistoryRetentionOptions Options => options;

	public IReadOnlyList<string> Kinds => _stores.Select(s => s.Kind).ToArray();

	public async ValueTask<IReadOnlyList<HistoryUsage>> MeasureAsync(CancellationToken ct = default)
	{
		var usage = new List<HistoryUsage>(_stores.Count);
		foreach (var store in _stores)
		{
			usage.Add(await store.MeasureAsync(ct));
		}

		return usage;
	}

	public async ValueTask<IReadOnlyList<HistoryPurgeOutcome>> PurgeAsync(CancellationToken ct = default)
	{
		await _onePass.WaitAsync(ct);
		try
		{
			// One clock for the whole pass, so every kind is judged against the same instant.
			var now = time.GetUtcNow();
			var outcomes = new List<HistoryPurgeOutcome>(_stores.Count);
			foreach (var store in _stores)
			{
				outcomes.Add(await PurgeKindAsync(store, options.RuleFor(store.Kind), now, ct));
			}

			return outcomes;
		}
		finally
		{
			_onePass.Release();
		}
	}

	private async ValueTask<HistoryPurgeOutcome> PurgeKindAsync(IHistoryStore store, HistoryRetentionRule rule,
		DateTimeOffset now, CancellationToken ct)
	{
		if (rule.KeepsEverything) return new HistoryKeptEverything(store.Kind);

		var batchSize = Math.Max(options.BatchSize, 1);
		long records = 0, bytes = 0;
		var batches = 0;
		var resumeFrom = Array.Empty<byte>();
		try
		{
			while (true)
			{
				ct.ThrowIfCancellationRequested();
				var batch = await store.FindPurgeableAsync(rule, now, resumeFrom, batchSize, ct);
				if (batch.Candidates.Count > 0)
				{
					await ArchiveAsync(store.Kind, batch, now, ct);
					var (deleted, freed) = await store.PurgeAsync(batch, rule, now, ct);
					records += deleted;
					bytes += freed;
					batches++;

					// A batch whose every candidate was re-judged worth keeping would be offered again from
					// the same place forever; the scan only moves on when something went.
					if (deleted == 0 && batch.ResumeFrom.AsSpan().SequenceEqual(resumeFrom)) break;
				}

				if (batch.IsLast) break;
				resumeFrom = batch.ResumeFrom;

				// Between batches, so the game's own writes are never queued behind a whole pass.
				await Task.Yield();
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
		{
			// An archive that cannot be written stops the kind before anything unarchived is deleted; a
			// store failure stops it with everything before it already committed. Either way the pass
			// reports what it did and why it stopped, and the next kind still runs.
			logger.LogError(ex, "History retention for {Kind} stopped after {Records} records", store.Kind, records);
			return new HistoryPurgeFailed(store.Kind, records, bytes, ex.Message);
		}

		if (records > 0)
		{
			logger.LogInformation("History retention purged {Records} {Kind} records ({Bytes} bytes) in {Batches} batches",
				records, store.Kind, bytes, batches);
		}

		return new HistoryPurged(store.Kind, rule, records, bytes, batches);
	}

	/// <summary>
	/// Appends the batch to <c>&lt;archive&gt;/&lt;kind&gt;-&lt;yyyyMMdd&gt;.jsonl</c>, one record per line,
	/// and flushes it through to the disk before returning: the delete that follows is durable, so the
	/// archive must be first.
	/// </summary>
	private async ValueTask ArchiveAsync(string kind, HistoryPurgeBatch batch, DateTimeOffset now, CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(options.ArchivePath)) return;

		Directory.CreateDirectory(options.ArchivePath);
		var file = Path.Join(options.ArchivePath,
			$"{kind.Replace('.', '-')}-{now.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.jsonl");

		var stamp = now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
		var prefix = Encoding.UTF8.GetBytes($"{{\"kind\":\"{kind}\",\"archivedAt\":\"{stamp}\",\"record\":");
		var suffix = "}\n"u8.ToArray();

		await using var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read, 64 * 1024,
			FileOptions.Asynchronous);
		foreach (var candidate in batch.Candidates)
		{
			await stream.WriteAsync(prefix, ct);
			await stream.WriteAsync(candidate.Archive, ct);
			await stream.WriteAsync(suffix, ct);
		}

		await stream.FlushAsync(ct);
		stream.Flush(flushToDisk: true);
	}
}
