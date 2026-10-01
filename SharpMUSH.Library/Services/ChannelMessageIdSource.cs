using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Channel line ids taken from the clock: the microsecond the id was taken, or one more than the last id
/// when the clock has not moved past it.
/// </summary>
/// <remarks>
/// <para>One sequence for every channel, so an id names a line on its own. It comes from the clock rather
/// than a counter because the recall buffer is in memory and a counter would start again at 1 after a
/// restart, while the portal's read markers, which hold these ids, are persisted: a marker at 5000 would
/// then call every new line read. Microseconds since 1970 stay below 2^53 until the year 2255, so a
/// browser reads the id from JSON exactly.</para>
/// <para>Per instance, not static: a test run starts several engine hosts in one process.</para>
/// </remarks>
public sealed class ChannelMessageIdSource(TimeProvider time) : IChannelMessageIdSource
{
	private long _last;

	public ChannelMessageIdSource() : this(TimeProvider.System)
	{
	}

	public long Next()
	{
		while (true)
		{
			var last = Interlocked.Read(ref _last);
			var next = Math.Max(last + 1, (time.GetUtcNow() - DateTimeOffset.UnixEpoch).Ticks / TimeSpan.TicksPerMicrosecond);
			if (Interlocked.CompareExchange(ref _last, next, last) == last)
			{
				return next;
			}
		}
	}
}
