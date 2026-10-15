using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc cref="IActivityLedger"/>
public sealed class ActivityLedger(Lazy<IMediator> mediator, TimeProvider clock, ILogger<ActivityLedger> logger)
	: IActivityLedger
{
	private static readonly AsyncLocal<bool> InPose = new();

	private readonly Lock _gate = new();
	private Pending _pending = new();

	public void RecordPose(long player, long? room) => RecordPose(player, room, clock.GetUtcNow());

	public void RecordScene(long? room) => RecordScene(room, clock.GetUtcNow());

	/// <summary><see cref="RecordPose(long, long?)"/> at a given moment.</summary>
	public void RecordPose(long player, long? room, DateTimeOffset at)
	{
		if (InPose.Value) return;

		var ms = at.ToUnixTimeMilliseconds();
		var day = DateOnly.FromDateTime(at.UtcDateTime).DayNumber;
		var hour = 1 << at.UtcDateTime.Hour;

		lock (_gate)
		{
			var playerDay = _pending.Players.GetValueOrDefault((day, player));
			_pending.Players[(day, player)] = (playerDay.Poses + 1, playerDay.Hours | hour);
			_pending.Last[player] = Math.Max(_pending.Last.GetValueOrDefault(player), ms);

			if (room is not { } roomNumber) return;
			var roomDay = RoomDay(day, roomNumber);
			roomDay.Poses++;
			roomDay.Posers.Add(player);
			_pending.Last[roomNumber] = Math.Max(_pending.Last.GetValueOrDefault(roomNumber), ms);
		}
	}

	/// <summary><see cref="RecordScene(long?)"/> at a given moment.</summary>
	public void RecordScene(long? room, DateTimeOffset at)
	{
		var day = DateOnly.FromDateTime(at.UtcDateTime).DayNumber;
		lock (_gate)
		{
			// A scene in no room still counts toward the game's total, under room -1.
			RoomDay(day, room ?? -1).Scenes++;
		}
	}

	public IDisposable PoseScope()
	{
		var outer = InPose.Value;
		InPose.Value = true;
		return new Scope(() => InPose.Value = outer);
	}

	private sealed class Scope(Action close) : IDisposable
	{
		public void Dispose() => close();
	}

	public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
	{
		Pending taken;
		lock (_gate)
		{
			if (_pending.IsEmpty) return;
			taken = _pending;
			_pending = new Pending();
		}

		try
		{
			await mediator.Value.Send(new MergeActivityCommand(taken.ToBatch()), cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// Keep the counts for the next flush rather than lose them.
			logger.LogError(ex, "Writing activity counts failed; they are kept for the next flush");
			lock (_gate)
			{
				_pending.Absorb(taken);
			}
		}
	}

	private RoomCounts RoomDay(int day, long room)
	{
		if (!_pending.Rooms.TryGetValue((day, room), out var counts))
		{
			counts = new RoomCounts();
			_pending.Rooms[(day, room)] = counts;
		}

		return counts;
	}

	private sealed class RoomCounts
	{
		public int Poses;
		public int Scenes;
		public readonly HashSet<long> Posers = [];
	}

	private sealed class Pending
	{
		public readonly Dictionary<(int Day, long Player), (int Poses, int Hours)> Players = [];
		public readonly Dictionary<(int Day, long Room), RoomCounts> Rooms = [];
		public readonly Dictionary<long, long> Last = [];

		public bool IsEmpty => Players.Count == 0 && Rooms.Count == 0 && Last.Count == 0;

		public ActivityBatch ToBatch() => new(
			Players.Select(p => new ActivityPlayerDay(p.Key.Day, p.Key.Player, p.Value.Poses, p.Value.Hours)).ToList(),
			Rooms.Select(r => new ActivityRoomDay(r.Key.Day, r.Key.Room, r.Value.Poses, r.Value.Scenes, r.Value.Posers.ToList()))
				.ToList(),
			new Dictionary<long, long>(Last));

		public void Absorb(Pending other)
		{
			foreach (var (key, value) in other.Players)
			{
				var kept = Players.GetValueOrDefault(key);
				Players[key] = (kept.Poses + value.Poses, kept.Hours | value.Hours);
			}

			foreach (var (key, value) in other.Rooms)
			{
				if (!Rooms.TryGetValue(key, out var kept))
				{
					Rooms[key] = value;
					continue;
				}

				kept.Poses += value.Poses;
				kept.Scenes += value.Scenes;
				kept.Posers.UnionWith(value.Posers);
			}

			foreach (var (key, value) in other.Last)
			{
				Last[key] = Math.Max(Last.GetValueOrDefault(key), value);
			}
		}
	}
}
