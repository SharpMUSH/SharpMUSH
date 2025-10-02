namespace SharpMUSH.Configuration.Options;

public record NetOptions(
	[property: PennConfig(Name = "mud_name", Description = "Name of your MUSH as displayed to players", DefaultValue = "SharpMUSH")] 
	string MudName,
	
	[property: PennConfig(Name = "mud_url", Description = "Web address of your MUSH for browser redirects", DefaultValue = null)] 
	string? MudUrl,
	
	[property: PennConfig(Name = "ip_addr", Description = "Specific IP address to listen on (leave blank for all addresses)", DefaultValue = null)] 
	string? IpAddr,
	
	[property: PennConfig(Name = "ssl_ip_addr", Description = "IP address to bind to for SSL connections", Nullable = true, DefaultValue = "4202")] 
	string? SslIpAddr,
	
	[property: PennConfig(Name = "port", Description = "Main telnet port for player connections", DefaultValue = "4203")] 
	uint Port,
	
	[property: PennConfig(Name = "ssl_port", Description = "Port for secure SSL connections (0 to disable)", DefaultValue = "0")] 
	uint SslPort,
	
	[property: PennConfig(Name = "portal_port", Description = "Port for portal connections", Nullable = true, DefaultValue = null)] 
	uint PortalPort,
	
	[property: PennConfig(Name = "ssl_portal_port", Description = "Port for secure portal connections", Nullable = true, DefaultValue = null)] 
	uint SllPortalPort,
	
	[property: PennConfig(Name = "socket_file", Description = "Unix domain socket file for SSL slave communication", Nullable = true, DefaultValue = null)] 
	string SocketFile,
	
	[property: PennConfig(Name = "use_ws", Description = "Enable WebSocket support for web clients", DefaultValue = "true")] 
	bool UseWebsockets,
	
	[property: PennConfig(Name = "ws_url", Description = "URL path for WebSocket connections", DefaultValue = "/wsclient")] 
	string? WebsocketUrl,
	
	[property: PennConfig(Name = "use_dns", Description = "Resolve IP numbers to hostnames (affects WHO display)", DefaultValue = "yes")] 
	bool UseDns,
	
	[property: PennConfig(Name = "logins", Description = "Allow player logins to the MUSH", DefaultValue = "yes")] 
	bool Logins,
	
	[property: PennConfig(Name = "player_creation", Description = "Allow new players to create accounts", DefaultValue = "yes")] 
	bool PlayerCreation,
	
	[property: PennConfig(Name = "guests", Description = "Allow guest connections", DefaultValue = "yes")] 
	bool Guests,
	
	[property: PennConfig(Name = "pueblo", Description = "Enable Pueblo HTML client support", DefaultValue = "yes")] 
	bool Pueblo,
	
	[property: PennConfig(Name = "sql_platform", Description = "SQL database platform to use", Nullable = true, DefaultValue = null)] 
	string? SqlPlatform,
	
	[property: PennConfig(Name = "sql_host", Description = "SQL database host connection string", Nullable = true, DefaultValue = null)] 
	string? SqlHost,
	
	[property: PennConfig(Name = "json_unsafe_unescape", Description = "Allow unsafe JSON unescaping", Nullable = true, DefaultValue = null)] 
	bool JsonUnsafeUnescape,
	
	[property: PennConfig(Name = "ssl_require_clientcert", Description = "Require clients to present valid SSL certificates", Nullable = true, DefaultValue = null)] 
	bool SslRequireClientCert
);