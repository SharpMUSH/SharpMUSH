using System.Globalization;
using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// TinyMUX's <c>CONNINFO</c>: a player's time online, longest and last session, number of sessions and when one last
/// ended, which <c>conntotal()</c>, <c>connmax()</c>, <c>connlast()</c>, <c>connnum()</c> and <c>connleft()</c> read.
/// It is kept in TinyMUX's own layout, five numbers of seconds or sessions, so a TinyMUX import carries it over.
/// </summary>
/// <remarks>
/// Each session that ends counts once. When a player has several at once, the time they overlap is counted once:
/// a session adds to the total only the time before the next one began, or all of it when it was the only one.
/// </remarks>
public static class ConnectionStatistics
{
	public const string Attribute = "CONNINFO";

	/// <summary>Updates are read and written back, so two sessions of one player ending together go one at a time.</summary>
	private static readonly SemaphoreSlim Gate = new(1, 1);

	/// <summary>What a player's <c>CONNINFO</c> holds.</summary>
	/// <param name="TotalSeconds">Time online in the sessions that have ended.</param>
	/// <param name="LongestSeconds">The longest session that has ended.</param>
	/// <param name="LastSeconds">How long the last session to end lasted.</param>
	/// <param name="Sessions">How many sessions have ended.</param>
	/// <param name="LastLogout">When the last one ended, in seconds since the epoch; 0 for never.</param>
	public readonly record struct Record(long TotalSeconds, long LongestSeconds, long LastSeconds, long Sessions, long LastLogout)
	{
		/// <summary>The five numbers, a space between each; a missing, negative or unreadable one is 0.</summary>
		public static Record Parse(string text)
		{
			var fields = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.Select(field => long.TryParse(field, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
					? Math.Max(0, value)
					: 0)
				.Concat(Enumerable.Repeat(0L, 5))
				.ToArray();
			return new Record(fields[0], fields[1], fields[2], fields[3], fields[4]);
		}

		public override string ToString()
			=> string.Create(CultureInfo.InvariantCulture, $"{TotalSeconds} {LongestSeconds} {LastSeconds} {Sessions} {LastLogout}");
	}

	/// <summary>The record kept on <paramref name="player"/>, all zeros when there is none.</summary>
	public static async ValueTask<Record> StoredAsync(IMediator mediator, DBRef player)
	{
		var stored = await mediator.CreateStream(new GetAttributeQuery(player, [Attribute])).LastOrDefaultAsync();
		return stored is null ? default : Record.Parse(stored.Value.ToPlainText());
	}

	/// <summary>
	/// Counts a session of <paramref name="player"/>'s that began at <paramref name="started"/> and ended at
	/// <paramref name="ended"/>, while the sessions that began at <paramref name="othersStarted"/> go on.
	/// Written as God, like <c>LASTLOGOUT</c>: the attribute is wizard-only and the player is not the one writing it.
	/// </summary>
	public static async ValueTask SessionEndedAsync(IMediator mediator, DBRef player, DateTimeOffset started,
		DateTimeOffset ended, IReadOnlyCollection<DateTimeOffset> othersStarted)
	{
		await Gate.WaitAsync();
		try
		{
			if (await HelperFunctions.GetGod(mediator) is not SharpPlayer god) return;

			var record = Ended(await StoredAsync(mediator, player), started, ended, othersStarted);
			await mediator.Send(new SetAttributeCommand(player, [Attribute], MarkupText.Plain(record.ToString()), god));
		}
		finally
		{
			Gate.Release();
		}
	}

	/// <summary><paramref name="record"/> with one more session counted, as <see cref="SessionEndedAsync"/> describes.</summary>
	public static Record Ended(Record record, DateTimeOffset started, DateTimeOffset ended,
		IReadOnlyCollection<DateTimeOffset> othersStarted)
	{
		// The time online is measured from the oldest session still open, this one included.
		var oldest = othersStarted.Append(started).Min();
		var full = Seconds(ended - oldest);
		var total = record.TotalSeconds;
		var longest = record.LongestSeconds;

		if (started <= oldest)
		{
			var nextStarted = othersStarted.Where(other => other >= started).DefaultIfEmpty(ended).Min();
			total += Seconds(nextStarted - started);
			longest = Math.Max(longest, full);
		}

		return new Record(total, longest, full, record.Sessions + 1, ended.ToUnixTimeSeconds());
	}

	/// <summary>When <paramref name="connection"/> began, read as <c>conn()</c> reads it; <c>null</c> when it carries no time.</summary>
	public static DateTimeOffset? StartedAt(IConnectionService.ConnectionData connection)
		=> long.TryParse(connection.Metadata.GetValueOrDefault("ConnectionStartTime"), NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var milliseconds)
			? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
			: null;

	private static long Seconds(TimeSpan span) => Math.Max(0, (long)span.TotalSeconds);
}
