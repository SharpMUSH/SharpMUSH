namespace SharpMUSH.Configuration.Options;

/// <summary>
/// Every parameter declares its default because the configuration schema the admin pages read reports
/// them per property (<c>SchemaBuilder.BuildProperties</c>), and that is what a "reset to default"
/// there writes back.
/// </summary>
/// <remarks>
/// They therefore have to say what <see cref="SharpMUSHOptions.Default()"/> says, which is checked by
/// <c>ConfigurationDefaultsTests</c>: an omitted argument at the one call site that matters silently
/// takes the value written here instead. One did — <c>Mxp</c> was left out of that default and took
/// <c>false</c>, while a game imported from a PennMUSH <c>mush.cnf</c> got <c>true</c>.
/// </remarks>
public record NetOptions(
	[property: SharpConfig(Name = "mud_name", Category = "Net", Description = "Name of your MUSH as displayed to players", Group = "General", Order = 1)]
	string MudName = "SharpMUSH",

	[property: SharpConfig(Name = "mud_url", Category = "Net", Description = "Web address of your MUSH for browser redirects", Group = "General", Order = 2)]
	string? MudUrl = null,

	[property: SharpConfig(Name = "ip_addr", Category = "Net", Description = "Specific IP address to listen on (leave blank for all addresses)", Group = "Connection Settings", Order = 4)]
	string? IpAddr = null,

	[property: SharpConfig(Name = "ssl_ip_addr", Category = "Net", Description = "IP address to bind to for SSL connections", Group = "Connection Settings", Order = 5)]
	string? SslIpAddr = null,

	[property: SharpConfig(Name = "port", Category = "Net", Description = "The port number the server listens on for incoming connections", ValidationPattern = @"^\d+$", Group = "Connection Settings", Order = 1, Min = 1, Max = 65535)]
	uint Port = 4201,

	[property: SharpConfig(Name = "ssl_port", Category = "Net", Description = "Port for SSL/TLS encrypted connections", ValidationPattern = @"^\d+$", Group = "Connection Settings", Order = 2, Min = 0, Max = 65535, Tooltip = "Set to 0 to disable SSL")]
	uint SslPort = 4203,

	[property: SharpConfig(Name = "portal_port", Category = "Net", Description = "Port for portal connections", ValidationPattern = @"^\d+$", Group = "Connection Settings", Order = 6, Min = 0, Max = 65535)]
	uint PortalPort = 5117,

	[property: SharpConfig(Name = "ssl_portal_port", Category = "Net", Description = "Port for secure portal connections", ValidationPattern = @"^\d+$", Group = "Connection Settings", Order = 7, Min = 0, Max = 65535)]
	uint SslPortalPort = 7296,

	[property: SharpConfig(Name = "socket_file", Category = "Net", Description = "Unix domain socket file for SSL slave communication", Group = "Connection Settings", Order = 8)]
	string SocketFile = "netmush.sock",

	[property: SharpConfig(Name = "use_ws", Category = "Net", Description = "Enable WebSocket support for web clients", Group = "Network Protocol", Order = 4)]
	bool UseWebsockets = true,

	[property: SharpConfig(Name = "ws_url", Category = "Net", Description = "URL path for WebSocket connections", Group = "Network Protocol", Order = 5)]
	string? WebsocketUrl = "/wsclient",

	[property: SharpConfig(Name = "use_dns", Category = "Net", Description = "Resolve IP numbers to hostnames (affects WHO display)", Group = "Network Protocol", Order = 6)]
	bool UseDns = true,

	[property: SharpConfig(Name = "logins", Category = "Net", Description = "Allow player logins to the MUSH", Group = "Connection Limits", Order = 4)]
	bool Logins = true,

	[property: SharpConfig(Name = "player_creation", Category = "Net", Description = "Allow new players to create accounts", Group = "Connection Limits", Order = 5)]
	bool PlayerCreation = true,

	[property: SharpConfig(Name = "guests", Category = "Net", Description = "Allow guest connections", Group = "Connection Limits", Order = 6)]
	bool Guests = true,

	[property: SharpConfig(Name = "pueblo", Category = "Net", Description = "Enable Pueblo/HTML client support", Group = "Network Protocol", Order = 1)]
	bool Pueblo = false,

	[property: SharpConfig(Name = "mxp", Category = "Net", Description = "Enable MXP (MUD eXtension Protocol) support", Group = "Network Protocol", Order = 2)]
	bool Mxp = true,

	[property: SharpConfig(Name = "sql_platform", Category = "Net", Description = "SQL database platform to use", Group = "Database", Order = 1)]
	string? SqlPlatform = null,

	[property: SharpConfig(Name = "sql_host", Category = "Net", Description = "SQL database host connection string", Group = "Database", Order = 2)]
	string? SqlHost = null,

	[property: SharpConfig(Name = "sql_database", Category = "Net", Description = "SQL database name", Group = "Database", Order = 3)]
	string? SqlDatabase = null,

	[property: SharpConfig(Name = "sql_username", Category = "Net", Description = "SQL database username", Group = "Database", Order = 4)]
	string? SqlUsername = null,

	[property: SharpConfig(Name = "sql_password", Category = "Net", Description = "SQL database password", Group = "Database", Order = 5)]
	string? SqlPassword = null,

	[property: SharpConfig(Name = "json_unsafe_unescape", Category = "Net", Description = "Allow unsafe JSON unescaping", Group = "Advanced", Order = 1)]
	bool JsonUnsafeUnescape = false,

	[property: SharpConfig(Name = "ssl_require_client_cert", Category = "Net", Description = "Require clients to present valid SSL certificates", Group = "Connection Settings", Order = 3, Tooltip = "Enhanced security but requires client certificate setup")]
	bool SslRequireClientCert = false
);
