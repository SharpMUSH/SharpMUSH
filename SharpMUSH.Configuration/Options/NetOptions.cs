namespace SharpMUSH.Configuration.Options;

public record NetOptions(
	[property: PennConfig(Name = "mud_name")] string MudName ,
	[property: PennConfig(Name = "mud_url")] string? MudUrl ,
	[property: PennConfig(Name = "ip_addr")] string? IpAddr ,
	[property: PennConfig(Name = "ssl_ip_addr")] string? SslIpAddr ,
	[property: PennConfig(Name = "port")] uint Port ,
	[property: PennConfig(Name = "ssl_port")] uint SslPort ,
	[property: PennConfig(Name = "portal_port")] uint PortalPort ,
	[property: PennConfig(Name = "ssl_portal_port")] uint SllPortalPort ,
	[property: PennConfig(Name = "socket_file")] string SocketFile ,
	[property: PennConfig(Name = "use_ws")] bool UseWebsockets ,
	[property: PennConfig(Name = "ws_url")] string WebsocketUrl ,
	[property: PennConfig(Name = "use_dns")] bool UseDns ,
	[property: PennConfig(Name = "logins")] bool Logins ,
	[property: PennConfig(Name = "player_creation")] bool PlayerCreation ,
	[property: PennConfig(Name = "guests")] bool Guests ,
	[property: PennConfig(Name = "pueblo")] bool Pueblo ,
	[property: PennConfig(Name = "sql_platform")] string? SqlPlatform ,
	[property: PennConfig(Name = "sql_host")] string SqlHost ,
	[property: PennConfig(Name = "json_unsafe_unescape")] bool JsonUnsafeUnescape ,
	[property: PennConfig(Name = "ssl_require_clientcert")] bool SslRequireClientCert
);