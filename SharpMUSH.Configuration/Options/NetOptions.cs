namespace SharpMUSH.Configuration.Options;

public record NetOptions(
	[property: SharpConfig(Name = "mud_name", Description = "Name of your MUSH as displayed to players")] 
	string MudName,
	
	[property: SharpConfig(Name = "mud_url", Description = "Web address of your MUSH for browser redirects")] 
	string? MudUrl,
	
	[property: SharpConfig(Name = "ip_addr", Description = "Specific IP address to listen on (leave blank for all addresses)")] 
	string? IpAddr,
	
	[property: SharpConfig(Name = "ssl_ip_addr", Description = "IP address to bind to for SSL connections")] 
	string? SslIpAddr,
	
	[property: SharpConfig(Name = "port", Description = "Main telnet port for player connections")] 
	uint Port,
	
	[property: SharpConfig(Name = "ssl_port", Description = "Port for secure SSL connections (0 to disable)")] 
	uint SslPort,
	
	[property: SharpConfig(Name = "portal_port", Description = "Port for portal connections")] 
	uint PortalPort,
	
	[property: SharpConfig(Name = "ssl_portal_port", Description = "Port for secure portal connections")] 
	uint SllPortalPort,
	
	[property: SharpConfig(Name = "socket_file", Description = "Unix domain socket file for SSL slave communication")] 
	string SocketFile,
	
	[property: SharpConfig(Name = "use_ws", Description = "Enable WebSocket support for web clients")] 
	bool UseWebsockets,
	
	[property: SharpConfig(Name = "ws_url", Description = "URL path for WebSocket connections")] 
	string? WebsocketUrl,
	
	[property: SharpConfig(Name = "use_dns", Description = "Resolve IP numbers to hostnames (affects WHO display)")] 
	bool UseDns,
	
	[property: SharpConfig(Name = "logins", Description = "Allow player logins to the MUSH")] 
	bool Logins,
	
	[property: SharpConfig(Name = "player_creation", Description = "Allow new players to create accounts")] 
	bool PlayerCreation,
	
	[property: SharpConfig(Name = "guests", Description = "Allow guest connections")] 
	bool Guests,
	
	[property: SharpConfig(Name = "pueblo", Description = "Enable Pueblo HTML client support")] 
	bool Pueblo,
	
	[property: SharpConfig(Name = "sql_platform", Description = "SQL database platform to use")] 
	string? SqlPlatform,
	
	[property: SharpConfig(Name = "sql_host", Description = "SQL database host connection string")] 
	string? SqlHost,
	
	[property: SharpConfig(Name = "json_unsafe_unescape", Description = "Allow unsafe JSON unescaping")] 
	bool JsonUnsafeUnescape,
	
	[property: SharpConfig(Name = "ssl_require_clientcert", Description = "Require clients to present valid SSL certificates")] 
	bool SslRequireClientCert
);