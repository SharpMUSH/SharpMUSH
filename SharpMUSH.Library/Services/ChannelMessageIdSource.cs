using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Channel line ids taken from the clock: the microsecond the id was taken, or one more than the last id
/// when the clock has not moved past it — and never at or below an id handed out before a restart.
/// </summary>
/// <remarks>
/// <para>One sequence for every channel, so an id names a line on its own. It comes from the clock rather
/// than a counter because the recall buffer is in memory and a counter would start again at 1 after a
/// restart, while the portal's read markers, which hold these ids, are persisted: a marker at 5000 would
/// then call every new line read. Microseconds since 1970 stay below 2^53 until the year 2255, so a
/// browser reads the id from JSON exactly.</para>
/// <para>The clock alone is not enough: set back across a restart, it would hand out ids below ones
/// already issued. So ids are reserved ahead in blocks (high/low): before an id past the reserved mark is
/// handed out, a new mark <see cref="Block"/> further on is written to the server data, and a new process
/// starts from the stored mark. One write covers a minute of lines; a crash loses nothing but the unused
/// rest of a block.</para>
/// <para>Per instance, not static: a test run starts several engine hosts in one process.</para>
/// </remarks>
public sealed class ChannelMessageIdSource(IExpandedObjectDataService serverData, TimeProvider time) : IChannelMessageIdSource
{
	/// <summary>How far ahead of the last id each written mark reaches: one minute of microseconds.</summary>
	public const long Block = 60_000_000;

	private readonly SemaphoreSlim _gate = new(1, 1);
	private long _last;
	private long _reservedThrough;
	private bool _loaded;

	public ChannelMessageIdSource(IExpandedObjectDataService serverData) : this(serverData, TimeProvider.System)
	{
	}

	public long Latest => Interlocked.Read(ref _last);

	/// <remarks>
	/// Loads the stored mark first if nothing has been handed out yet, which sets <see cref="Latest"/> to it:
	/// every id the previous process handed out is at or below it, and every id this one hands out is above.
	/// </remarks>
	public async ValueTask<long> CeilingAsync(CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			await LoadAsync();
			return Interlocked.Read(ref _last);
		}
		finally
		{
			_gate.Release();
		}
	}

	/// <summary>Reads the mark the previous process stored, once. Called holding the gate.</summary>
	private async ValueTask LoadAsync()
	{
		if (_loaded) return;

		// Everything the previous process handed out is at or below the mark it stored.
		_reservedThrough = (await serverData.GetExpandedServerDataAsync<ChannelMessageIdReservation>())?.ReservedThrough ?? 0;
		Interlocked.Exchange(ref _last, Math.Max(_last, _reservedThrough));
		_loaded = true;
	}

	public async ValueTask<long> NextAsync(CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			await LoadAsync();

			var next = Math.Max(_last + 1, (time.GetUtcNow() - DateTimeOffset.UnixEpoch).Ticks / TimeSpan.TicksPerMicrosecond);
			if (next > _reservedThrough)
			{
				var reserved = next + Block;
				await serverData.SetExpandedServerDataAsync(new ChannelMessageIdReservation { ReservedThrough = reserved });
				_reservedThrough = reserved;
			}

			Interlocked.Exchange(ref _last, next);
			return next;
		}
		finally
		{
			_gate.Release();
		}
	}
}

/// <summary>
/// The server data <see cref="ChannelMessageIdSource"/> keeps: every channel line id handed out is at or
/// below <see cref="ReservedThrough"/>.
/// </summary>
public sealed class ChannelMessageIdReservation
{
	public long ReservedThrough { get; set; }
}
