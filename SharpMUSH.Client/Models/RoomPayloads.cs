namespace SharpMUSH.Client.Models;

// The room payloads the `room-contents` package pushes over OOB (`room.info`, `room.contents`,
// `room.exits`), typed. The wire contract is docs/softcode/room-contents-handler.md; the design
// handoff's version is docs/design/d1/README.md §7.1. OobEntryParser builds these, and every member a
// v1 payload does not carry is null (or false, or empty) — a v2 payload only adds keys to v1.

/// <summary>
/// A picture reference. <see cref="Url"/> is exactly what the game sent: whether it may be shown is
/// decided where it is rendered (site-relative <c>/…</c> or <c>https:</c> only), not here.
/// </summary>
/// <param name="Url">The picture's URL, raw.</param>
/// <param name="Alt">Alternative text, or null.</param>
/// <param name="Focal">The point to keep in frame when cropping, each coordinate 0 to 1, or null.</param>
/// <param name="Width">Intrinsic width in pixels, or null. The softcode sends none.</param>
/// <param name="Height">Intrinsic height in pixels, or null. The softcode sends none.</param>
public sealed record ImageRef(string Url, string? Alt, (double X, double Y)? Focal, int? Width, int? Height);

/// <summary>A room's description. <see cref="Format"/> is <c>text</c> from the shipped softcode.</summary>
public sealed record RoomDescription(string Format, string Text);

/// <summary>The scene running in a room, as far as the viewer may see it.</summary>
/// <param name="Id">The scene id, a string (matching <c>SceneEventMessage.SceneId</c>).</param>
/// <param name="Title">The scene's title, or null.</param>
/// <param name="Cast">How many members the scene has, or null.</param>
/// <param name="Role">The viewer's role in the scene (<c>participant</c>, <c>owner</c>, …), or null when they have none. v2.2.</param>
/// <param name="Focus">
/// Whether the viewer is focused on the scene, so that a pose they make in the room is recorded in it; null
/// when the handler did not say. v2.2.
/// </param>
public sealed record RoomScene(string Id, string? Title, int? Cast, string? Role = null, bool? Focus = null)
{
	/// <summary>
	/// A pose made in the room will not be recorded in the scene: the viewer is not focused on it. Unknown
	/// (an older handler) is not "outside": the composer stays as it was.
	/// </summary>
	public bool Outside => Focus is false;
}

/// <summary>The <c>room.info</c> payload: the room the player is in.</summary>
/// <param name="Dbref">The room's <c>#N</c>, or null.</param>
/// <param name="Name">The room's name; empty when the payload has none.</param>
/// <param name="ObjId">The room's objid (<c>#N:ctime</c>), the identity to cache by. v2.</param>
/// <param name="Area">The room's area (its zone's or parent's name), or null. v2.</param>
/// <param name="Image">The room's picture, or null. v2.</param>
/// <param name="Desc">The room's description, or null. v2.</param>
/// <param name="Scene">The scene in the room the viewer may see, or null. v2.</param>
public sealed record RoomInfo(
	string? Dbref,
	string Name,
	string? ObjId,
	string? Area,
	ImageRef? Image,
	RoomDescription? Desc,
	RoomScene? Scene);

/// <summary>A command offered on an occupant's row, such as Page.</summary>
public sealed record OccupantAction(string Label, string Cmd);

/// <summary>One row of <c>room.contents</c>: a player or thing in the room.</summary>
/// <param name="Dbref">The occupant's <c>#N</c>, or null.</param>
/// <param name="Name">The occupant's name; empty when the row has none.</param>
/// <param name="Cmd">The command to issue on a click, or null.</param>
/// <param name="ObjId">The occupant's objid, the identity to cache by. v2.</param>
/// <param name="Type">The object type in lower case (<c>player</c>, <c>thing</c>), or null. v2.</param>
/// <param name="Color">The player's name colour, only ever <c>#rrggbb</c>, or null. v2.</param>
/// <param name="Image">The occupant's picture, or null. v2.</param>
/// <param name="Status">A status word (<c>active</c>, <c>idle</c>, <c>away</c> by default), or null. v2.</param>
/// <param name="Idle">Idle seconds, or null. v2.</param>
/// <param name="Profile">Whether the occupant has a profile page. v2.</param>
/// <param name="You">Whether this row is the viewer. v2.</param>
/// <param name="Actions">Commands offered on the row; empty when none. v2.</param>
public sealed record RoomOccupant(
	string? Dbref,
	string Name,
	string? Cmd,
	string? ObjId,
	string? Type,
	string? Color,
	ImageRef? Image,
	string? Status,
	int? Idle,
	bool Profile,
	bool You,
	IReadOnlyList<OccupantAction> Actions);

/// <summary>Whether an exit can be taken, for the viewer it was sent to.</summary>
public enum ExitState
{
	/// <summary>The exit leads somewhere and its lock passes.</summary>
	Open,

	/// <summary>The exit's lock fails for the viewer; the row may carry a hint.</summary>
	Locked,

	/// <summary>The exit leads nowhere.</summary>
	Closed,
}

/// <summary>Where an open exit leads.</summary>
/// <param name="Name">The destination's name, or null.</param>
/// <param name="Area">The destination's area, or null.</param>
/// <param name="Image">The destination's picture, or null.</param>
/// <param name="Desc">The destination's description text, or null.</param>
/// <param name="Here">How many visible, connected players are there, or null.</param>
public sealed record ExitDestination(string? Name, string? Area, ImageRef? Image, string? Desc, int? Here);

/// <summary>One row of <c>room.exits</c>.</summary>
/// <param name="Dbref">The exit's <c>#N</c>, or null (v1 rows carry none).</param>
/// <param name="Name">The exit's name; empty when the row has none.</param>
/// <param name="Cmd">The command that takes the exit, or null. A locked exit keeps it.</param>
/// <param name="ObjId">The exit's objid. v2.</param>
/// <param name="Aliases">The exit's aliases; empty when none. v2.</param>
/// <param name="State">Open, locked or closed, or null when the row does not say. v2.</param>
/// <param name="Hint">Why a locked exit is locked (its <c>@fail</c>), or null. v2.</param>
/// <param name="Confirm">A confirmation to show before taking the exit, or null. v2.</param>
/// <param name="Dest">Where the exit leads, or null. v2.</param>
public sealed record RoomExit(
	string? Dbref,
	string Name,
	string? Cmd,
	string? ObjId,
	IReadOnlyList<string> Aliases,
	ExitState? State,
	string? Hint,
	string? Confirm,
	ExitDestination? Dest);

/// <summary>
/// The latest of each <c>room.*</c> payload, typed. Every push replaces its part whole — the softcode
/// sends whole lists, never diffs — and leaves the other two parts as they were.
/// </summary>
/// <param name="Info">The latest <c>room.info</c>, or null before one arrives (or when it was unreadable).</param>
/// <param name="Occupants">The latest <c>room.contents</c> rows.</param>
/// <param name="Exits">The latest <c>room.exits</c> rows.</param>
public sealed record RoomState(RoomInfo? Info, IReadOnlyList<RoomOccupant> Occupants, IReadOnlyList<RoomExit> Exits)
{
	/// <summary>No room: nothing has been pushed, or the store was cleared.</summary>
	public static RoomState Empty { get; } = new(null, [], []);
}
