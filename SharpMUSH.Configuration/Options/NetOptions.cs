namespace SharpMUSH.Configuration.Options;

/// <remarks>
/// No parameter declares a default; <see cref="SharpMUSHOptions.Default()"/> is where they live. These
/// used to carry their own, for the admin configuration schema, and an argument omitted there silently
/// took the record's value instead — <c>Mxp</c> was left out and took <c>false</c>, while a game imported
/// from a PennMUSH <c>mush.cnf</c> got <c>true</c>. The schema reads <c>Default()</c> now (#1246), so an
/// omitted argument is a compile error instead.
/// </remarks>
public record NetOptions(
	[property: SharpConfig(Name = "mud_name", Category = "Net", Description = "Name of your MUSH as displayed to players", Group = "General", Order = 1)]
	string MudName,

	[property: SharpConfig(Name = "mud_url", Category = "Net", Description = "Web address of your MUSH for browser redirects", Group = "General", Order = 2)]
	string? MudUrl,

	[property: SharpConfig(Name = "ip_addr", Category = "Net", Description = "Specific IP address to listen on (leave blank for all addresses)", Group = "Connection Settings", Order = 4, Unused = "The connection server listens on every address; there is no setting for one.")]
	string? IpAddr,

	[property: SharpConfig(Name = "ssl_ip_addr", Category = "Net", Description = "IP address to bind to for SSL connections", Group = "Connection Settings", Order = 5, Unused = "The connection server listens on every address; there is no setting for one.")]
	string? SslIpAddr,

	[property: SharpConfig(Name = "port", Category = "Net", Description = "The telnet port reported to MUD crawlers (MSSP and MSSP-REQUEST). Telnet listens on ConnectionServer:TelnetPort, which can differ behind a port mapping", ValidationPattern = @"^\d+$", Group = "Connection Settings", Order = 1, Min = 1, Max = 65535)]
	uint Port,

	[property: SharpConfig(Name = "ssl_port", Category = "Net", Description = "The TLS telnet port reported to MUD crawlers (MSSP and MSSP-REQUEST). TLS telnet listens on ConnectionServer:TelnetSslPort", ValidationPattern = @"^\d+$", Group = "Connection Settings", Order = 2, Min = 0, Max = 65535, Tooltip = "0 reports no TLS port")]
	uint SslPort,

	[property: SharpConfig(Name = "portal_port", Category = "Net", Description = "Port for portal connections", ValidationPattern = @"^\d+$", Group = "Connection Settings", Order = 6, Min = 0, Max = 65535, Unused = "The web portal listens where SharpMUSH.Server's Kestrel addresses say (ASPNETCORE_URLS).")]
	uint PortalPort,

	[property: SharpConfig(Name = "ssl_portal_port", Category = "Net", Description = "Port for secure portal connections", ValidationPattern = @"^\d+$", Group = "Connection Settings", Order = 7, Min = 0, Max = 65535, Unused = "The web portal listens where SharpMUSH.Server's Kestrel addresses say (ASPNETCORE_URLS).")]
	uint SslPortalPort,

	[property: SharpConfig(Name = "socket_file", Category = "Net", Description = "Unix domain socket file for SSL slave communication", Group = "Connection Settings", Order = 8, Unused = "There is no SSL slave process. TLS telnet is ConnectionServer:TelnetSslPort with a Kestrel certificate.")]
	string SocketFile,

	[property: SharpConfig(Name = "use_ws", Category = "Net", Description = "Enable WebSocket support for web clients", Group = "Network Protocol", Order = 4, Unused = "WebSockets are always on, at /ws on ConnectionServer:HttpPort.")]
	bool UseWebsockets,

	[property: SharpConfig(Name = "ws_url", Category = "Net", Description = "URL path for WebSocket connections", Group = "Network Protocol", Order = 5, Unused = "The WebSocket path is always /ws, on ConnectionServer:HttpPort.")]
	string? WebsocketUrl,

	[property: SharpConfig(Name = "use_dns", Category = "Net", Description = "Resolve IP numbers to hostnames (affects WHO display)", Group = "Network Protocol", Order = 6)]
	bool UseDns,

	[property: SharpConfig(Name = "logins", Category = "Net", Description = "Allow player logins to the MUSH", Group = "Connection Limits", Order = 4)]
	bool Logins,

	[property: SharpConfig(Name = "player_creation", Category = "Net", Description = "Allow new players to create accounts", Group = "Connection Limits", Order = 5)]
	bool PlayerCreation,

	[property: SharpConfig(Name = "guests", Category = "Net", Description = "Allow guest connections", Group = "Connection Limits", Order = 6)]
	bool Guests,

	[property: SharpConfig(Name = "pueblo", Category = "Net", Description = "Enable Pueblo/HTML client support", Group = "Network Protocol", Order = 1)]
	bool Pueblo,

	[property: SharpConfig(Name = "mxp", Category = "Net", Description = "Enable MXP (MUD eXtension Protocol) support", Group = "Network Protocol", Order = 2)]
	bool Mxp,

	[property: SharpConfig(Name = "sql_platform", Category = "Net", Description = "SQL database platform to use", Group = "Database", Order = 1)]
	string? SqlPlatform,

	[property: SharpConfig(Name = "sql_host", Category = "Net", Description = "SQL database host connection string", Group = "Database", Order = 2)]
	string? SqlHost,

	[property: SharpConfig(Name = "sql_database", Category = "Net", Description = "SQL database name", Group = "Database", Order = 3)]
	string? SqlDatabase,

	[property: SharpConfig(Name = "sql_username", Category = "Net", Description = "SQL database username", Group = "Database", Order = 4)]
	string? SqlUsername,

	[property: SharpConfig(Name = "sql_password", Category = "Net", Description = "SQL database password", Group = "Database", Order = 5)]
	string? SqlPassword,

	[property: SharpConfig(Name = "json_unsafe_unescape", Category = "Net", Description = "Allow unsafe JSON unescaping", Group = "Advanced", Order = 1)]
	bool JsonUnsafeUnescape,

	[property: SharpConfig(Name = "ssl_require_client_cert", Category = "Net", Description = "Require clients to present valid SSL certificates", Group = "Connection Settings", Order = 3, Tooltip = "Enhanced security but requires client certificate setup")]
	bool SslRequireClientCert
);
