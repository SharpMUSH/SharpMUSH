namespace SharpMUSH.Configuration.Options;

public record DatabaseOptions(
	[property: SharpConfig(Name = "player_start", Description = "Room where new players start")] uint PlayerStart,
	[property: SharpConfig(Name = "master_room", Description = "Master room that controls global settings")] uint MasterRoom,
	[property: SharpConfig(Name = "base_room", Description = "Base room used as fallback for homeless objects")] uint BaseRoom,
	[property: SharpConfig(Name = "default_home", Description = "Default home for players without a set home")] uint DefaultHome,
	[property: SharpConfig(Name = "exits_connect_rooms", Description = "Whether exits can connect rooms together")] bool ExitsConnectRooms,
	[property: SharpConfig(Name = "zone_control_zmp_only", Description = "Restrict zone control to ZMP objects only")] bool ZoneControlZmpOnly,
	[property: SharpConfig(Name = "ancestor_room", Description = "Parent object for all room objects")] uint? AncestorRoom,
	[property: SharpConfig(Name = "ancestor_exit", Description = "Parent object for all exit objects")] uint? AncestorExit,
	[property: SharpConfig(Name = "ancestor_thing", Description = "Parent object for all thing objects")] uint? AncestorThing,
	[property: SharpConfig(Name = "ancestor_player", Description = "Parent object for all player objects")] uint? AncestorPlayer,
	[property: SharpConfig(Name = "event_handler", Description = "Object that handles global events")] uint? EventHandler,
	[property: SharpConfig(Name = "http_handler", Description = "Player object that handles HTTP requests")] uint? HttpHandler,
	[property: SharpConfig(Name = "http_per_second", Description = "Maximum HTTP requests to handle per second")] uint HttpRequestsPerSecond
);