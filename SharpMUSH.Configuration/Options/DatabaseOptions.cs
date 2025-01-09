namespace SharpMUSH.Configuration.Options;

public record DatabaseOptions(
	[PennConfig(Name = "player_start")] uint PlayerStart,
	[PennConfig(Name = "master_room")] uint MasterRoom,
	[PennConfig(Name = "base_room")] uint BaseRoom,
	[PennConfig(Name = "default_home")] uint DefaultHome,
	[PennConfig(Name = "exits_connect_rooms")] bool ExitsConnectRooms,
	[PennConfig(Name = "zone_control_zmp_only")] bool ZoneControlZmpOnly,
	[PennConfig(Name = "ancestor_room")] uint? AncestorRoom,
	[PennConfig(Name = "ancestor_exit")] uint? AncestorExit,
	[PennConfig(Name = "ancestor_thing")] uint? AncestorThing,
	[PennConfig(Name = "ancestor_player")] uint? AncestorPlayer,
	[PennConfig(Name = "event_handler")] uint? EventHandler,
	[PennConfig(Name = "http_handler")] uint? HttpHandler,
	[PennConfig(Name = "http_per_second")] uint HttpRequestsPerSecond
);