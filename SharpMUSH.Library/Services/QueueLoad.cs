namespace SharpMUSH.Library.Services;

/// <summary>
/// PennMUSH's <c>queue_load_record</c> (<c>src/cque.c:86</c>): how many queue entries started in each of
/// the last 15 minutes' seconds. <c>@ps</c> reports the mean per second over the last 1, 5 and 15 minutes.
/// </summary>
public sealed class QueueLoad(TimeProvider clock)
{
	private const int Seconds = 900;

	private readonly int[] _record = new int[Seconds];
	private readonly Lock _lock = new();
	private long _second = clock.GetUtcNow().ToUnixTimeSeconds();
	// The bucket of _second; buckets run backwards from it, as Penn's array does from index 0.
	private int _head;

	/// <summary>A queue entry started (<c>queue_load_record[0] += 1</c>, <c>src/cque.c:1139</c>).</summary>
	public void Record()
	{
		lock (_lock)
		{
			Advance();
			_record[_head]++;
		}
	}

	/// <summary>The mean entries per second over the last 1, 5 and 15 minutes, this second included.</summary>
	public (double OneMinute, double FiveMinutes, double FifteenMinutes) Averages()
	{
		lock (_lock)
		{
			Advance();
			return (Average(60), Average(300), Average(Seconds));
		}
	}

	/// <summary>
	/// Penn's <c>update_queue_load</c> (<c>src/cque.c:961</c>), run lazily: seconds that passed with nothing
	/// recorded start empty. A clock that went back changes nothing.
	/// </summary>
	private void Advance()
	{
		var now = clock.GetUtcNow().ToUnixTimeSeconds();
		var elapsed = now - _second;
		if (elapsed <= 0)
			return;

		_second = now;
		for (var step = 0; step < Math.Min(elapsed, Seconds); step++)
		{
			_head = (_head + 1) % Seconds;
			_record[_head] = 0;
		}
	}

	private double Average(int seconds)
	{
		var total = 0L;
		for (var back = 0; back < seconds; back++)
			total += _record[(_head - back + Seconds) % Seconds];
		return (double)total / seconds;
	}
}
