using System.Globalization;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>Reserves durable blocks of opaque handles, so delayed output cannot reach a later socket.</summary>
public sealed class DurableDescriptorGeneratorService : IDescriptorGeneratorService, IAsyncDisposable
{
	internal const string HighWaterKey = "high-water";
	private const string BucketName = "connection_descriptors";
	private readonly INatsKVStore _store;
	private readonly NatsConnection? _nats;
	private readonly int _blockSize;
	private readonly long _initialHighWater;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private static readonly TimeSpan ReservationTimeout = TimeSpan.FromSeconds(5);
	private long _next;
	private long _last;
	private bool _hasBlock;

	private DurableDescriptorGeneratorService(INatsKVStore store, NatsConnection? nats, int blockSize, long initialHighWater)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);
		ArgumentOutOfRangeException.ThrowIfNegative(initialHighWater);
		_store = store;
		_nats = nats;
		_blockSize = blockSize;
		_initialHighWater = initialHighWater;
	}

	public static async Task<DurableDescriptorGeneratorService> CreateAsync(string url, CancellationToken ct = default)
	{
		var nats = new NatsConnection(new NatsOpts { Url = url });
		try
		{
			await nats.ConnectAsync();
			var kv = new NatsKVContext(new NatsJSContext(nats));
			var store = await kv.CreateOrUpdateStoreAsync(new NatsKVConfig(BucketName) { MaxAge = TimeSpan.Zero }, ct);
			var generator = new DurableDescriptorGeneratorService(store, nats, 4096, int.MaxValue);
			await generator.ReserveBlockAsync(ct);
			return generator;
		}
		catch
		{
			await nats.DisposeAsync();
			throw;
		}
	}

	internal static async Task<DurableDescriptorGeneratorService> CreateAsync(INatsKVStore store,
		int blockSize, long initialHighWater = int.MaxValue)
	{
		var generator = new DurableDescriptorGeneratorService(store, null, blockSize, initialHighWater);
		await generator.ReserveBlockAsync(CancellationToken.None);
		return generator;
	}

	private async Task ReserveBlockAsync(CancellationToken ct)
	{
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
		deadline.CancelAfter(ReservationTimeout);
		ct = deadline.Token;
		for (var attempt = 0; attempt < 32; attempt++)
		{
			var entry = await _store.TryGetEntryAsync<string>(HighWaterKey, cancellationToken: ct);
			var highWater = _initialHighWater;
			ulong revision = 0;
			if (entry.Success)
			{
				if (!long.TryParse(entry.Value.Value, NumberStyles.None, CultureInfo.InvariantCulture, out highWater)
					|| highWater < _initialHighWater)
					throw new InvalidDataException("Invalid connection descriptor high-water mark.");
				revision = entry.Value.Revision;
			}
			else if (entry.Error is not NatsKVKeyNotFoundException)
			{
				// In particular, a deleted allocator must never silently reset its sequence.
				throw entry.Error;
			}

			var last = checked(highWater + _blockSize);
			var reserved = await _store.TryUpdateAsync(HighWaterKey, last.ToString(CultureInfo.InvariantCulture), revision,
				cancellationToken: ct);
			if (reserved.Success)
			{
				_next = checked(highWater + 1);
				_last = last;
				_hasBlock = true;
				return;
			}
			if (reserved.Error is not NatsKVWrongLastRevisionException) throw reserved.Error;
		}
		throw new InvalidOperationException("Could not reserve a connection descriptor block after concurrent updates.");
	}

	private async ValueTask<long> NextAsync(CancellationToken ct)
	{
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
		deadline.CancelAfter(ReservationTimeout);
		await _gate.WaitAsync(deadline.Token);
		try
		{
			if (!_hasBlock) await ReserveBlockAsync(deadline.Token);
			var result = _next;
			if (result == _last) _hasBlock = false;
			else _next++;
			return result;
		}
		finally { _gate.Release(); }
	}

	public long GetNextTelnetDescriptor() => NextAsync(default).AsTask().GetAwaiter().GetResult();
	public long GetNextWebSocketDescriptor() => NextAsync(default).AsTask().GetAwaiter().GetResult();
	public ValueTask<long> GetNextTelnetDescriptorAsync(CancellationToken ct = default) => NextAsync(ct);
	public ValueTask<long> GetNextWebSocketDescriptorAsync(CancellationToken ct = default) => NextAsync(ct);
	public void ReserveWebSocketDescriptor(long descriptor) { }
	public void ReleaseTelnetDescriptor(long descriptor) { }
	public void ReleaseWebSocketDescriptor(long descriptor) { }

	public async ValueTask DisposeAsync()
	{
		if (_nats is not null) await _nats.DisposeAsync();
	}
}
