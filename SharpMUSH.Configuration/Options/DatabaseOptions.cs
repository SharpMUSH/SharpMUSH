namespace SharpMUSH.Configuration.Options;

public record DatabaseOptions(
	[property: SharpConfig(
		Name = "player_start",
		Dbref = true,
		Category = "Database",
		Description = "Room where new players start",
		ValidationPattern = @"^\d+$",
		Group = "Core Rooms",
		Order = 1,
		Min = 0)]
	uint PlayerStart,

	[property: SharpConfig(
		Name = "master_room",
		Dbref = true,
		Category = "Database",
		Description = "Master room that controls global settings",
		ValidationPattern = @"^\d+$",
		Group = "Core Rooms",
		Order = 2,
		Min = 0)]
	uint MasterRoom,

	[property: SharpConfig(
		Name = "base_room",
		Dbref = true,
		Category = "Database",
		Description = "Base room used as fallback for homeless objects",
		ValidationPattern = @"^\d+$",
		Group = "Core Rooms",
		Order = 3,
		Min = 0)]
	uint BaseRoom,

	[property: SharpConfig(
		Name = "default_home",
		Dbref = true,
		Category = "Database",
		Description = "Default home for players without a set home",
		ValidationPattern = @"^\d+$",
		Group = "Core Rooms",
		Order = 4,
		Min = 0)]
	uint DefaultHome,

	[property: SharpConfig(
		Name = "exits_connect_rooms",
		Category = "Database",
		Description = "Whether exits can connect rooms together",
		Group = "Behavior",
		Order = 1)]
	bool ExitsConnectRooms,

	[property: SharpConfig(
		Name = "zone_control_zmp_only",
		Category = "Database",
		Description = "Restrict zone control to ZMP objects only",
		Group = "Behavior",
		Order = 2)]
	bool ZoneControlZmpOnly,

	[property: SharpConfig(
		Name = "ancestor_room",
		Dbref = true,
		Category = "Database",
		Description = "Parent object for all room objects",
		ValidationPattern = @"^\d*$",
		Group = "Ancestors",
		Order = 1,
		Min = 0,
		Tooltip = "Leave empty for no ancestor")]
	uint? AncestorRoom,

	[property: SharpConfig(
		Name = "ancestor_exit",
		Dbref = true,
		Category = "Database",
		Description = "Parent object for all exit objects",
		ValidationPattern = @"^\d*$",
		Group = "Ancestors",
		Order = 2,
		Min = 0,
		Tooltip = "Leave empty for no ancestor")]
	uint? AncestorExit,

	[property: SharpConfig(
		Name = "ancestor_thing",
		Dbref = true,
		Category = "Database",
		Description = "Parent object for all thing objects",
		ValidationPattern = @"^\d*$",
		Group = "Ancestors",
		Order = 3,
		Min = 0,
		Tooltip = "Leave empty for no ancestor")]
	uint? AncestorThing,

	[property: SharpConfig(
		Name = "ancestor_player",
		Dbref = true,
		Category = "Database",
		Description = "Parent object for all player objects",
		ValidationPattern = @"^\d*$",
		Group = "Ancestors",
		Order = 4,
		Min = 0,
		Tooltip = "Leave empty for no ancestor")]
	uint? AncestorPlayer,

	[property: SharpConfig(
		Name = "event_handler",
		Dbref = true,
		Category = "Database",
		Description = "Wizard object that handles global events (default: the seeded Event Handler, #9)",
		ValidationPattern = @"^\d*$",
		Group = "Handlers",
		Order = 1,
		Min = 0)]
	uint? EventHandler,

	[property: SharpConfig(
		Name = "http_handler",
		Dbref = true,
		Category = "Database",
		Description = "Wizard object that handles HTTP requests (default: the seeded HTTP Handler, #8)",
		ValidationPattern = @"^\d*$",
		Group = "Handlers",
		Order = 2,
		Min = 0)]
	uint? HttpHandler,

	[property: SharpConfig(
		Name = "package_manager",
		Dbref = true,
		Category = "Database",
		Description = "Wizard object that owns softcode-package-managed objects",
		ValidationPattern = @"^\d*$",
		Group = "Handlers",
		Order = 4,
		Min = 0,
		Tooltip = "Leave empty to use the seeded Package Manager, #7")]
	uint? PackageManager,

	[property: SharpConfig(
		Name = "http_per_second",
		Category = "Database",
		Description = "Maximum HTTP requests to handle per second",
		ValidationPattern = @"^\d+$",
		Group = "Handlers",
		Order = 3,
		Min = 0,
		Max = 100000,
		Tooltip = "Set to 0 to turn the softcode HTTP surface off entirely; otherwise the whole game "
			+ "serves at most this many /http/ requests a second, bursting up to the same number.")]
	uint HttpRequestsPerSecond,

	[property: SharpConfig(
		Name = "allow_browser_code",
		Category = "Database",
		Description = "Allow plugins to ship compiled Blazor components that load into the browser portal",
		Group = "Behavior",
		Order = 3,
		Tooltip = "Off by default. Enabling lets operator-trusted plugins run arbitrary compiled C# in the browser; gives up client AOT/trimming.")]
	bool AllowBrowserCode,

	[property: SharpConfig(
		Name = "messages_object",
		Dbref = true,
		Category = "Database",
		Description = "Object whose attributes hold the connect screen, the MOTDs and the other server messages",
		ValidationPattern = @"^\d*$",
		Group = "Handlers",
		Order = 5,
		Min = 0,
		Tooltip = "Leave empty to show the stored texts from the Messages page. The bundled messages package sets this to its object.")]
	uint? MessagesObject
)
{
	/// <summary>
	/// The Package Manager player the seed creates (<c>Migration_CreateDatabase</c>, and the PennMUSH
	/// converter's seed), and the <c>package_manager</c> default.
	/// </summary>
	public const uint SeededPackageManager = 7;

	/// <summary>
	/// The Package Manager to write as: <paramref name="configured"/>, else the seeded one. The package
	/// installer and the profile-handler reset both resolve it here, so they cannot disagree.
	/// </summary>
	public static uint PackageManagerOrSeeded(uint? configured) => configured ?? SeededPackageManager;
}
