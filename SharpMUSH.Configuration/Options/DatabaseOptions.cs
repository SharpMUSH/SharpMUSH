namespace SharpMUSH.Configuration.Options;

public record DatabaseOptions(
	[property: PennConfig(Name = "player_start", Description = "Room where new players start")] uint PlayerStart,
	[property: PennConfig(Name = "master_room", Description = "Master room that controls global settings")] uint MasterRoom,
	[property: PennConfig(Name = "base_room", Description = "Base room used as fallback for homeless objects")] uint BaseRoom,
	[property: PennConfig(Name = "default_home", Description = "Default home for players without a set home")] uint DefaultHome,
	[property: PennConfig(Name = "exits_connect_rooms", Description = "Whether exits can connect rooms together")] bool ExitsConnectRooms,
	[property: PennConfig(Name = "zone_control_zmp_only", Description = "Restrict zone control to ZMP objects only")] bool ZoneControlZmpOnly,
	[property: PennConfig(Name = "ancestor_room", Description = "Parent object for all room objects")] uint? AncestorRoom,
	[property: PennConfig(Name = "ancestor_exit", Description = "Parent object for all exit objects")] uint? AncestorExit,
	[property: PennConfig(Name = "ancestor_thing", Description = "Parent object for all thing objects")] uint? AncestorThing,
	[property: PennConfig(Name = "ancestor_player", Description = "Parent object for all player objects")] uint? AncestorPlayer,
	[property: PennConfig(Name = "event_handler", Description = "Object that handles global events")] uint? EventHandler,
	[property: PennConfig(Name = "http_handler", Description = "Player object that handles HTTP requests")] uint? HttpHandler,
	[property: PennConfig(Name = "http_per_second", Description = "Maximum HTTP requests to handle per second")] uint HttpRequestsPerSecond
);