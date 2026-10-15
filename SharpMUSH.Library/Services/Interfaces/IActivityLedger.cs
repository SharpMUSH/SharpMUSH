namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Counts play as it happens: a pose by a character, a scene started in a room. Counts are held in memory
/// and written to <see cref="IActivityStore"/> in one batch a minute (and when the server stops), so a pose
/// costs no write of its own; a crash loses at most that minute's counts.
/// </summary>
/// <remarks>
/// The same pose often arrives twice: the engine counts the POSE/SAY/SEMIPOSE/@EMIT a character types, and
/// the Scene plugin counts what its logger records, which for a typed pose is that same line. A pose by the
/// same character in the same room within <see cref="SharpMUSH.Library.Services.ActivityLedger.SamePoseWindow"/> of the last one counted
/// is taken for the same pose and not counted again.
/// </remarks>
public interface IActivityLedger
{
	/// <summary>Counts one pose by <paramref name="player"/> (a dbref number) in <paramref name="room"/>, or nowhere.</summary>
	void RecordPose(long player, long? room);

	/// <summary>Counts one scene started in <paramref name="room"/>, or in no room.</summary>
	void RecordScene(long? room);

	/// <summary>Writes what has been counted since the last flush.</summary>
	ValueTask FlushAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Something that feeds <see cref="IActivityLedger"/>, named so the reports know what was counted: the
/// Scene plugin registers <c>scenes</c>, and without it the reports leave scene numbers out instead of
/// showing zero.
/// </summary>
public sealed record ActivitySource(string Name)
{
	public const string Scenes = "scenes";
}
