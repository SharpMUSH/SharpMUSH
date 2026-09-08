namespace SharpMUSH.Configuration.Options;

public record FlagOptions(
	[property: SharpConfig(
		Name = "player_flags",
		Category = "Flag",
		Description = "Default flags assigned to newly created players",
		Group = "Default Flags",
		Order = 1)]
	string[]? PlayerFlags,

	[property: SharpConfig(
		Name = "room_flags",
		Category = "Flag",
		Description = "Default flags assigned to newly created rooms",
		Group = "Default Flags",
		Order = 2)]
	string[]? RoomFlags,

	[property: SharpConfig(
		Name = "exit_flags",
		Category = "Flag",
		Description = "Default flags assigned to newly created exits",
		Group = "Default Flags",
		Order = 3)]
	string[]? ExitFlags,

	[property: SharpConfig(
		Name = "thing_flags",
		Category = "Flag",
		Description = "Default flags assigned to newly created things",
		Group = "Default Flags",
		Order = 4)]
	string[]? ThingFlags,

	[property: SharpConfig(
		Name = "channel_flags",
		Category = "Flag",
		Description = "Default flags assigned to newly created channels",
		Group = "Default Flags",
		Order = 5)]
	string[]? ChannelFlags
)
{
	/// <summary>
	/// The flags each type is created with when nothing configures them, exactly as PennMUSH's
	/// <c>game/mushcnf.dst</c> ships them.
	/// <para>
	/// NO_COMMAND on players, rooms and things, and nothing on exits. NO_COMMAND keeps an object out
	/// of the <c>$</c>-command search, which is the whole reason it is a default: mushcnf.dst's own
	/// comment calls it "definitely a good idea for rooms and players, and, depending on the
	/// composition of your database, probably a good idea for things". Exits are left bare — the two
	/// alternatives PennMUSH offers there, DARK and TRANSPARENT, are commented out.
	/// </para>
	/// <para>
	/// One copy, because there are two ways to arrive at the defaults — a mush.cnf that omits a line,
	/// and no mush.cnf at all — and they disagreed. Rooms and things came out of one path carrying a
	/// flag literally named "", and exits came out NO_COMMAND from both, so which path built the
	/// options decided whether a freshly dug room was searched on every command.
	/// </para>
	/// </summary>
	public static class Defaults
	{
		public const string Player = "enter_ok ansi no_command";
		public const string Room = "no_command";
		public const string Thing = "no_command";
		public const string Exit = "";
		public const string Channel = "player";

		/// <summary>The same lists, already split, for callers that build options directly.</summary>
		public static string[] Split(string flags) =>
			flags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	}
}
