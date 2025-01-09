namespace SharpMUSH.Configuration.Options;

public record DatabaseOptions(
	[property: PennConfig(Name = "player_start")] uint PlayerStart,
	[property: PennConfig(Name = "master_room")] uint MasterRoom,
	[property: PennConfig(Name = "base_room")] uint BaseRoom,
	[property: PennConfig(Name = "default_home")] uint DefaultHome,
	[property: PennConfig(Name = "exits_connect_rooms")] bool ExitsConnectRooms,
	[property: PennConfig(Name = "zone_control_zmp_only")] bool ZoneControlZmpOnly,
	[property: PennConfig(Name = "ancestor_room")] uint? AncestorRoom,
	[property: PennConfig(Name = "ancestor_exit")] uint? AncestorExit,
	[property: PennConfig(Name = "ancestor_thing")] uint? AncestorThing,
	[property: PennConfig(Name = "ancestor_player")] uint? AncestorPlayer,
	[property: PennConfig(Name = "event_handler")] uint? EventHandler,
	[property: PennConfig(Name = "http_handler")] uint? HttpHandler,
	[property: PennConfig(Name = "http_per_second")] uint HttpRequestsPerSecond
);