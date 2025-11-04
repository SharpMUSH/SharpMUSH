namespace SharpMUSH.Configuration.Options;

public record NetOptions(
	[property: SharpConfig(Name = "mud_name", Category = "Net", Description = "Name of your MUSH as displayed to players")] 
	string MudName,
	
	[property: SharpConfig(Name = "mud_url", Category = "Net", Description = "Web address of your MUSH for browser redirects")] 
	string? MudUrl,
	
	[property: SharpConfig(Name = "ip_addr", Category = "Net", Description = "Specific IP address to listen on (leave blank for all addresses)")] 
	string? IpAddr,
	
	[property: SharpConfig(Name = "ssl_ip_addr", Category = "Net", Description = "IP address to bind to for SSL connections")] 
	string? SslIpAddr,
	
	[property: SharpConfig(Name = "port", Category = "Net", Description = "Main telnet port for player connections", ValidationPattern = @"^\d+$")] 
	uint Port,
	
	[property: SharpConfig(Name = "ssl_port", Category = "Net", Description = "Port for secure SSL connections (0 to disable)", ValidationPattern = @"^\d+$")] 
	uint SslPort,
	
	[property: SharpConfig(Name = "portal_port", Category = "Net", Description = "Port for portal connections", ValidationPattern = @"^\d+$")] 
	uint PortalPort,
	
	[property: SharpConfig(Name = "ssl_portal_port", Category = "Net", Description = "Port for secure portal connections", ValidationPattern = @"^\d+$")] 
	uint SslPortalPort,
	
	[property: SharpConfig(Name = "socket_file", Category = "Net", Description = "Unix domain socket file for SSL slave communication")] 
	string SocketFile,
	
	[property: SharpConfig(Name = "use_ws", Category = "Net", Description = "Enable WebSocket support for web clients")] 
	bool UseWebsockets,
	
	[property: SharpConfig(Name = "ws_url", Category = "Net", Description = "URL path for WebSocket connections")] 
	string? WebsocketUrl,
	
	[property: SharpConfig(Name = "use_dns", Category = "Net", Description = "Resolve IP numbers to hostnames (affects WHO display)")] 
	bool UseDns,
	
	[property: SharpConfig(Name = "logins", Category = "Net", Description = "Allow player logins to the MUSH")] 
	bool Logins,
	
	[property: SharpConfig(Name = "player_creation", Category = "Net", Description = "Allow new players to create accounts")] 
	bool PlayerCreation,
	
	[property: SharpConfig(Name = "guests", Category = "Net", Description = "Allow guest connections")] 
	bool Guests,
	
	[property: SharpConfig(Name = "pueblo", Category = "Net", Description = "Enable Pueblo HTML client support")] 
	bool Pueblo,
	
	[property: SharpConfig(Name = "sql_platform", Category = "Net", Description = "SQL database platform to use")] 
	string? SqlPlatform,
	
	[property: SharpConfig(Name = "sql_host", Category = "Net", Description = "SQL database host connection string")] 
	string? SqlHost,

	[property: SharpConfig(Name = "sql_database", Category = "Net", Description = "SQL database host connection string")] 
	string? SqlDatabase,

	[property: SharpConfig(Name = "sql_username", Category = "Net", Description = "SQL database host connection string")] 
	string? SqlUsername,

	[property: SharpConfig(Name = "sql_password", Category = "Net", Description = "SQL database host connection string")] 
	string? SqlPassword,

	[property: SharpConfig(Name = "json_unsafe_unescape", Category = "Net", Description = "Allow unsafe JSON unescaping")] 
	bool JsonUnsafeUnescape,
	
	[property: SharpConfig(Name = "ssl_require_clientcert", Category = "Net", Description = "Require clients to present valid SSL certificates")] 
	bool SslRequireClientCert
);