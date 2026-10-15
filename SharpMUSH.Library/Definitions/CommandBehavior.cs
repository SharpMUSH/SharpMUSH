namespace SharpMUSH.Library.Definitions;

[Flags]
public enum CommandBehavior
{
	Default = 0,
	NoParse = 1 << 0,
	EqSplit = 1 << 1,
	LSArgs = 1 << 2,
	RSArgs = 1 << 3,
	RSNoParse = 1 << 4,
	SOCKET = 1 << 5,
	SingleToken = 1 << 6,
	NoGagged = 1 << 7,
	NoGuest = 1 << 8,
	Player = 1 << 9,
	Switches = 1 << 10,
	RSBrace = 1 << 11,
	Thing = 1 << 12,
	Internal = 1 << 13,
	NoOp = 1 << 14,
	God = 1 << 15,
	Args = 1 << 16,

	/// <summary>
	/// The command speaks a line into the room its executor stands in (SAY, POSE, SEMIPOSE, @EMIT). When a
	/// character runs it, the activity ledger counts a pose, before any hook can replace the command.
	/// </summary>
	Pose = 1 << 17
}