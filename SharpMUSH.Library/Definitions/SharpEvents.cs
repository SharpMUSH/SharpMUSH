namespace SharpMUSH.Library.Definitions;

/// <summary>
/// Names of SharpMUSH-fired events (attributes evaluated on the configured event_handler object).
/// PennMUSH-parity events are fired with string literals at their call sites; this holds
/// SharpMUSH extension events so the name has a single definition shared by firer and tests.
/// </summary>
public static class SharpEvents
{
	/// <summary>
	/// Fired (room-scoped, not actor-scoped) whenever a room's visible contents change — an
	/// object enters/leaves, or a player connects/disconnects in it. Args: (roomobjid, cause).
	/// The handler is expected to fan out structured pushes to that room's connected occupants.
	/// </summary>
	public const string RoomContents = "ROOM`CONTENTS";
}
