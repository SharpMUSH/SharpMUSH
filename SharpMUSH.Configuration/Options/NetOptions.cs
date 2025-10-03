namespace SharpMUSH.Configuration.Options;

public record NetOptions(
	[property: PennConfig(Name = "mud_name", Description = "Name of your MUSH as displayed to players")] 
	string MudName,
	
	[property: PennConfig(Name = "mud_url", Description = "Web address of your MUSH for browser redirects")] 
	string? MudUrl,
	
	[property: PennConfig(Name = "ip_addr", Description = "Specific IP address to listen on (leave blank for all addresses)")] 
	string? IpAddr,
	
	[property: PennConfig(Name = "ssl_ip_addr", Description = "IP address to bind to for SSL connections", Nullable = true)] 
	string? SslIpAddr,
	
	[property: PennConfig(Name = "port", Description = "Main telnet port for player connections")] 
	uint Port,
	
	[property: PennConfig(Name = "ssl_port", Description = "Port for secure SSL connections (0 to disable)")] 
	uint SslPort,
	
	[property: PennConfig(Name = "portal_port", Description = "Port for portal connections", Nullable = true)] 
	uint PortalPort,
	
	[property: PennConfig(Name = "ssl_portal_port", Description = "Port for secure portal connections", Nullable = true)] 
	uint SllPortalPort,
	
	[property: PennConfig(Name = "socket_file", Description = "Unix domain socket file for SSL slave communication", Nullable = true)] 
	string SocketFile,
	
	[property: PennConfig(Name = "use_ws", Description = "Enable WebSocket support for web clients")] 
	bool UseWebsockets,
	
	[property: PennConfig(Name = "ws_url", Description = "URL path for WebSocket connections")] 
	string? WebsocketUrl,
	
	[property: PennConfig(Name = "use_dns", Description = "Resolve IP numbers to hostnames (affects WHO display)")] 
	bool UseDns,
	
	[property: PennConfig(Name = "logins", Description = "Allow player logins to the MUSH")] 
	bool Logins,
	
	[property: PennConfig(Name = "player_creation", Description = "Allow new players to create accounts")] 
	bool PlayerCreation,
	
	[property: PennConfig(Name = "guests", Description = "Allow guest connections")] 
	bool Guests,
	
	[property: PennConfig(Name = "pueblo", Description = "Enable Pueblo HTML client support")] 
	bool Pueblo,
	
	[property: PennConfig(Name = "sql_platform", Description = "SQL database platform to use", Nullable = true)] 
	string? SqlPlatform,
	
	[property: PennConfig(Name = "sql_host", Description = "SQL database host connection string", Nullable = true)] 
	string? SqlHost,
	
	[property: PennConfig(Name = "json_unsafe_unescape", Description = "Allow unsafe JSON unescaping", Nullable = true)] 
	bool JsonUnsafeUnescape,
	
	[property: PennConfig(Name = "ssl_require_clientcert", Description = "Require clients to present valid SSL certificates", Nullable = true)] 
	bool SslRequireClientCert
);