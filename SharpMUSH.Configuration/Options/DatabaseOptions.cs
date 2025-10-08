namespace SharpMUSH.Configuration.Options;

public record DatabaseOptions(
	[property: SharpConfig(Name = "player_start", Category = "Database", Description = "Room where new players start", ValidationPattern = @"^\d+$")] uint PlayerStart,
	[property: SharpConfig(Name = "master_room", Category = "Database", Description = "Master room that controls global settings", ValidationPattern = @"^\d+$")] uint MasterRoom,
	[property: SharpConfig(Name = "base_room", Category = "Database", Description = "Base room used as fallback for homeless objects", ValidationPattern = @"^\d+$")] uint BaseRoom,
	[property: SharpConfig(Name = "default_home", Category = "Database", Description = "Default home for players without a set home", ValidationPattern = @"^\d+$")] uint DefaultHome,
	[property: SharpConfig(Name = "exits_connect_rooms", Category = "Database", Description = "Whether exits can connect rooms together")] bool ExitsConnectRooms,
	[property: SharpConfig(Name = "zone_control_zmp_only", Category = "Database", Description = "Restrict zone control to ZMP objects only")] bool ZoneControlZmpOnly,
	[property: SharpConfig(Name = "ancestor_room", Category = "Database", Description = "Parent object for all room objects", ValidationPattern = @"^\d*$")] uint? AncestorRoom,
	[property: SharpConfig(Name = "ancestor_exit", Category = "Database", Description = "Parent object for all exit objects", ValidationPattern = @"^\d*$")] uint? AncestorExit,
	[property: SharpConfig(Name = "ancestor_thing", Category = "Database", Description = "Parent object for all thing objects", ValidationPattern = @"^\d*$")] uint? AncestorThing,
	[property: SharpConfig(Name = "ancestor_player", Category = "Database", Description = "Parent object for all player objects", ValidationPattern = @"^\d*$")] uint? AncestorPlayer,
	[property: SharpConfig(Name = "event_handler", Category = "Database", Description = "Object that handles global events", ValidationPattern = @"^\d*$")] uint? EventHandler,
	[property: SharpConfig(Name = "http_handler", Category = "Database", Description = "Player object that handles HTTP requests", ValidationPattern = @"^\d*$")] uint? HttpHandler,
	[property: SharpConfig(Name = "http_per_second", Category = "Database", Description = "Maximum HTTP requests to handle per second", ValidationPattern = @"^\d+$")] uint HttpRequestsPerSecond
);