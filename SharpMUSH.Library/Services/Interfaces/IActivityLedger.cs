namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Counts play as it happens: a pose by a character, a scene started in a room. Counts are held in memory
/// and written to <see cref="IActivityStore"/> in one batch a minute (and when the server stops), so a pose
/// costs no write of its own; a crash loses at most that minute's counts.
/// </summary>
/// <remarks>
/// The same pose often arrives twice: the engine counts the POSE/SAY/SEMIPOSE/@EMIT a character types, and
/// the Scene plugin counts what its logger records, which for a typed pose is that same line, recorded by
/// the logger's hook while the command runs. The engine opens <see cref="PoseScope"/> around the command, and
/// a pose recorded inside it is not counted again; the engine counts the typed line itself through the scope
/// once the command is admitted.
/// </remarks>
public interface IActivityLedger
{
	/// <summary>
	/// Counts one pose by <paramref name="player"/> (a dbref number) in <paramref name="room"/>, or nowhere.
	/// Not counted inside an open <see cref="PoseScope"/>.
	/// </summary>
	void RecordPose(long player, long? room);

	/// <summary>
	/// Marks the rest of this flow as part of one typed pose by <paramref name="player"/> in
	/// <paramref name="room"/>, until the scope is disposed: <see cref="RecordPose"/> counts nothing inside it,
	/// and <see cref="IPoseScope.Count"/> counts the pose itself. Open it from the method that runs the rest of
	/// the command, not from one it awaits: the mark lives in the async flow.
	/// </summary>
	IPoseScope PoseScope(long player, long? room);

	/// <summary>Counts one scene started in <paramref name="room"/>, or in no room.</summary>
	void RecordScene(long? room);

	/// <summary>Writes what has been counted since the last flush.</summary>
	ValueTask FlushAsync(CancellationToken cancellationToken = default);
}

/// <summary>A typed pose in flight, from <see cref="IActivityLedger.PoseScope"/>.</summary>
public interface IPoseScope : IDisposable
{
	/// <summary>Counts the pose, once however often it is called: when the command is admitted to run.</summary>
	void Count();
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
