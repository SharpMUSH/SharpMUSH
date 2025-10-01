namespace SharpMUSH.Configuration.Options;

public record NetOptions(
	[property: PennConfig(Name = "mud_name", Description = "Name of your MUSH as displayed to players", Category = "Essential")] 
	string MudName,
	
	[property: PennConfig(Name = "mud_url", Description = "Web address of your MUSH for browser redirects", Category = "Essential")] 
	string? MudUrl,
	
	[property: PennConfig(Name = "ip_addr", Description = "Specific IP address to listen on (leave blank for all addresses)", Category = "Network")] 
	string? IpAddr,
	
	[property: PennConfig(Name = "ssl_ip_addr", Description = "IP address to bind to for SSL connections", Category = "Security", IsAdvanced = true)] 
	string? SslIpAddr,
	
	[property: PennConfig(Name = "port", Description = "Main telnet port for player connections", Category = "Essential", DefaultValue = "4203")] 
	uint Port,
	
	[property: PennConfig(Name = "ssl_port", Description = "Port for secure SSL connections (0 to disable)", Category = "Security", DefaultValue = "0")] 
	uint SslPort,
	
	[property: PennConfig(Name = "portal_port", Description = "Port for portal connections", Category = "Network", IsAdvanced = true)] 
	uint PortalPort,
	
	[property: PennConfig(Name = "ssl_portal_port", Description = "Port for secure portal connections", Category = "Security", IsAdvanced = true)] 
	uint SllPortalPort,
	
	[property: PennConfig(Name = "socket_file", Description = "Unix domain socket file for SSL slave communication", Category = "Security", IsAdvanced = true)] 
	string SocketFile,
	
	[property: PennConfig(Name = "use_ws", Description = "Enable WebSocket support for web clients", Category = "Network", DefaultValue = "true")] 
	bool UseWebsockets,
	
	[property: PennConfig(Name = "ws_url", Description = "URL path for WebSocket connections", Category = "Network", DefaultValue = "/wsclient")] 
	string WebsocketUrl,
	
	[property: PennConfig(Name = "use_dns", Description = "Resolve IP numbers to hostnames (affects WHO display)", Category = "Network", DefaultValue = "yes")] 
	bool UseDns,
	
	[property: PennConfig(Name = "logins", Description = "Allow player logins to the MUSH", Category = "Access Control", DefaultValue = "yes")] 
	bool Logins,
	
	[property: PennConfig(Name = "player_creation", Description = "Allow new players to create accounts", Category = "Access Control", DefaultValue = "yes")] 
	bool PlayerCreation,
	
	[property: PennConfig(Name = "guests", Description = "Allow guest connections", Category = "Access Control", DefaultValue = "yes")] 
	bool Guests,
	
	[property: PennConfig(Name = "pueblo", Description = "Enable Pueblo HTML client support", Category = "Network", DefaultValue = "yes")] 
	bool Pueblo,
	
	[property: PennConfig(Name = "sql_platform", Description = "SQL database platform to use", Category = "Database", IsAdvanced = true)] 
	string? SqlPlatform,
	
	[property: PennConfig(Name = "sql_host", Description = "SQL database host connection string", Category = "Database", IsAdvanced = true)] 
	string SqlHost,
	
	[property: PennConfig(Name = "json_unsafe_unescape", Description = "Allow unsafe JSON unescaping", Category = "Security", IsAdvanced = true)] 
	bool JsonUnsafeUnescape,
	
	[property: PennConfig(Name = "ssl_require_clientcert", Description = "Require clients to present valid SSL certificates", Category = "Security", IsAdvanced = true)] 
	bool SslRequireClientCert
);