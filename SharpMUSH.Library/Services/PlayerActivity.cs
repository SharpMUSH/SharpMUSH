using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// How many distinct players were connected over the last day and week, for MSSP's PLAYERS DAY and PLAYERS WEEK. The server notes,
/// each time it samples who is connected, the hour it last saw each player; entries older than a week are dropped.
/// Only the counts leave the server.
/// </summary>
/// <remarks>
/// Kept in the server data, so a restart keeps the week. Written only when an entry moves to a new hour or one is
/// dropped, so sampling every minute costs at most a write an hour per player. A player who connects and leaves
/// between two samples is not counted.
/// </remarks>
public sealed class PlayerActivity(IExpandedObjectDataService serverData)
{
	public static readonly TimeSpan Week = TimeSpan.FromDays(7);

	private readonly SemaphoreSlim _gate = new(1, 1);
	private Dictionary<int, long>? _lastSeen;

	/// <summary>Notes that <paramref name="players"/> are connected at <paramref name="now"/>.</summary>
	public async ValueTask RecordAsync(IEnumerable<int> players, DateTimeOffset now, CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			var lastSeen = await LoadAsync();
			var hour = HourOf(now);
			var changed = false;

			foreach (var player in players)
			{
				if (lastSeen.TryGetValue(player, out var seen) && seen >= hour) continue;
				lastSeen[player] = hour;
				changed = true;
			}

			var cutoff = HourOf(now - Week);
			foreach (var stale in lastSeen.Where(entry => entry.Value < cutoff).Select(entry => entry.Key).ToList())
			{
				lastSeen.Remove(stale);
				changed = true;
			}

			if (changed)
			{
				await serverData.SetExpandedServerDataAsync(new PlayerActivityData
				{
					LastSeen = lastSeen.ToDictionary(entry => entry.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), entry => entry.Value)
				});
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	/// <summary>Distinct players seen in the 24 hours and the 7 days before <paramref name="now"/>, to the hour.</summary>
	public async ValueTask<(int Day, int Week)> CountAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			var lastSeen = await LoadAsync();
			var day = HourOf(now - TimeSpan.FromDays(1));
			var week = HourOf(now - Week);
			return (lastSeen.Values.Count(seen => seen >= day), lastSeen.Values.Count(seen => seen >= week));
		}
		finally
		{
			_gate.Release();
		}
	}

	/// <summary>Called holding the gate.</summary>
	private async ValueTask<Dictionary<int, long>> LoadAsync()
	{
		if (_lastSeen is not null) return _lastSeen;

		var stored = await serverData.GetExpandedServerDataAsync<PlayerActivityData>();
		_lastSeen = (stored?.LastSeen ?? [])
			.Where(entry => int.TryParse(entry.Key, System.Globalization.CultureInfo.InvariantCulture, out _))
			.ToDictionary(entry => int.Parse(entry.Key, System.Globalization.CultureInfo.InvariantCulture), entry => entry.Value);
		return _lastSeen;
	}

	/// <summary>The start of the hour <paramref name="at"/> falls in, in Unix seconds.</summary>
	private static long HourOf(DateTimeOffset at) => at.ToUnixTimeSeconds() / 3600 * 3600;
}

/// <summary>The server data <see cref="PlayerActivity"/> keeps: player dbref number → the hour last seen, in Unix seconds.</summary>
public sealed class PlayerActivityData
{
	public Dictionary<string, long> LastSeen { get; set; } = [];
}
