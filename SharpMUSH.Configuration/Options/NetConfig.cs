namespace SharpMUSH.Configuration.Options;

public record NetConfig(
	string MudName = "PennMUSH",
	string? MudUrl = "",
	string? IpAddr = "",
	string? SslIpAddr = "",
	uint Port = 4201,
	uint SslPort = 4202,
	string SocketFile = "data/netmush.sock",
	bool UseWs = true,
	string WsUrl = "/wsclient",
	bool UseDns = true,
	bool Logins = true,
	bool PlayerCreation = true,
	bool Guests = true,
	bool Pueblo = true,
	string SqlPlatform = "mysql",
	string SqlHost = "localhost",
	bool JsonUnsafeUnescape = true,
	bool SslRequireClientCert = false
);