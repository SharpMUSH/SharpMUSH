namespace SharpMUSH.Configuration.Options;

public record DatabaseOptions(
	[property: PennConfig(Name = "player_start", Description = "Room where new players start", DefaultValue = "0")] uint PlayerStart,
	[property: PennConfig(Name = "master_room", Description = "Master room that controls global settings", DefaultValue = "2")] uint MasterRoom,
	[property: PennConfig(Name = "base_room", Description = "Base room used as fallback for homeless objects", DefaultValue = "0")] uint BaseRoom,
	[property: PennConfig(Name = "default_home", Description = "Default home for players without a set home", DefaultValue = "0")] uint DefaultHome,
	[property: PennConfig(Name = "exits_connect_rooms", Description = "Whether exits can connect rooms together", DefaultValue = "no")] bool ExitsConnectRooms,
	[property: PennConfig(Name = "zone_control_zmp_only", Description = "Restrict zone control to ZMP objects only", DefaultValue = "yes")] bool ZoneControlZmpOnly,
	[property: PennConfig(Name = "ancestor_room", Description = "Parent object for all room objects", DefaultValue = "")] uint? AncestorRoom,
	[property: PennConfig(Name = "ancestor_exit", Description = "Parent object for all exit objects", DefaultValue = "")] uint? AncestorExit,
	[property: PennConfig(Name = "ancestor_thing", Description = "Parent object for all thing objects", DefaultValue = "")] uint? AncestorThing,
	[property: PennConfig(Name = "ancestor_player", Description = "Parent object for all player objects", DefaultValue = "")] uint? AncestorPlayer,
	[property: PennConfig(Name = "event_handler", Description = "Object that handles global events", DefaultValue = "")] uint? EventHandler,
	[property: PennConfig(Name = "http_handler", Description = "Player object that handles HTTP requests", DefaultValue = "")] uint? HttpHandler,
	[property: PennConfig(Name = "http_per_second", Description = "Maximum HTTP requests to handle per second", DefaultValue = "30")] uint HttpRequestsPerSecond
);