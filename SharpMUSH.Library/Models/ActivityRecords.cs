namespace SharpMUSH.Library.Models;

/// <summary>
/// One character's poses on one UTC day. No text, no rooms: a count and the hours it fell in.
/// </summary>
/// <param name="Day">The UTC day, as <see cref="DateOnly.DayNumber"/>.</param>
/// <param name="Player">The character's dbref number.</param>
/// <param name="Poses">Poses that day.</param>
/// <param name="Hours">Bit <c>h</c> is set when they posed in UTC hour <c>h</c> (0-23).</param>
public sealed record ActivityPlayerDay(int Day, long Player, int Poses, int Hours);

/// <summary>One room's poses and scenes on one UTC day.</summary>
/// <param name="Day">The UTC day, as <see cref="DateOnly.DayNumber"/>.</param>
/// <param name="Room">The room's dbref number.</param>
/// <param name="Poses">Poses there that day.</param>
/// <param name="Scenes">Scenes started there that day.</param>
/// <param name="Posers">The dbref numbers of the characters who posed there that day, each once.</param>
public sealed record ActivityRoomDay(int Day, long Room, int Poses, int Scenes, IReadOnlyList<long> Posers);

/// <summary>
/// What a flush adds: counts are added to what is kept, hours and posers are joined, and
/// <see cref="LastPoses"/> moves a dbref's last pose forward, never back.
/// </summary>
/// <param name="LastPoses">Dbref number (character or room) to the Unix milliseconds of its latest pose.</param>
public sealed record ActivityBatch(
	IReadOnlyList<ActivityPlayerDay> Players,
	IReadOnlyList<ActivityRoomDay> Rooms,
	IReadOnlyDictionary<long, long> LastPoses)
{
	public bool IsEmpty => Players.Count == 0 && Rooms.Count == 0 && LastPoses.Count == 0;
}

/// <summary>Every kept day between two UTC days, both included, and every last pose.</summary>
public sealed record ActivityWindow(
	IReadOnlyList<ActivityPlayerDay> Players,
	IReadOnlyList<ActivityRoomDay> Rooms,
	IReadOnlyDictionary<long, long> LastPoses);
