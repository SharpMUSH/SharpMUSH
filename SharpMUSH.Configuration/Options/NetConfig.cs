namespace SharpMUSH.Configuration.Options;

public record NetConfig(
	[PennConfig(Name = "mud_name")] string MudName ,
	[PennConfig(Name = "mud_url")] string? MudUrl ,
	[PennConfig(Name = "ip_addr")] string? IpAddr ,
	[PennConfig(Name = "ssl_ip_addr")] string? SslIpAddr ,
	[PennConfig(Name = "port")] uint Port ,
	[PennConfig(Name = "ssl_port")] uint SslPort ,
	[PennConfig(Name = "socket_file")] string SocketFile ,
	[PennConfig(Name = "use_ws")] bool UseWebsockets ,
	[PennConfig(Name = "ws_url")] string WebsocketUrl ,
	[PennConfig(Name = "use_dns")] bool UseDns ,
	[PennConfig(Name = "logins")] bool Logins ,
	[PennConfig(Name = "player_creation")] bool PlayerCreation ,
	[PennConfig(Name = "guests")] bool Guests ,
	[PennConfig(Name = "pueblo")] bool Pueblo ,
	[PennConfig(Name = "sql_platform")] string? SqlPlatform ,
	[PennConfig(Name = "sql_host")] string SqlHost ,
	[PennConfig(Name = "json_unsafe_unescape")] bool JsonUnsafeUnescape ,
	[PennConfig(Name = "ssl_require_clientcert")] bool SslRequireClientCert
);