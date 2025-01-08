namespace SharpMUSH.Configuration.Options;

public class DatabaseOptions(
	uint PlayerStart = 0,
	uint MasterRoom = 2,
	uint BaseRoom = 0,
	uint DefaultHome = 0,
	bool ExitsConnectRooms = false,
	bool ZoneControlZmpOnly = true,
	uint? AncestorRoom = null,
	uint? AncestorExit = null,
	uint? AncestorThing = null,
	uint? AncestorPlayer = null,
	uint? EventHandler = null,
	uint? HttpHandler = null,
	uint HttpPerSecond = 10
);