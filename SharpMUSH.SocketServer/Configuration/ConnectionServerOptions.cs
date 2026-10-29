namespace SharpMUSH.SocketServer.Configuration;

/// <summary>
/// Configuration settings for the ConnectionServer
/// </summary>
public class ConnectionServerOptions
{
	/// <summary>
	/// Maximum concurrent upgraded HTTP connections (WebSockets). Must be positive.
	/// Override with ConnectionServer:MaxConcurrentUpgradedConnections.
	/// </summary>
	public long MaxConcurrentUpgradedConnections { get; set; } = 10000;

	/// <summary>
	/// Port for Telnet connections
	/// </summary>
	public int TelnetPort { get; set; } = 4201;

	/// <summary>
	/// Port for HTTP/WebSocket connections
	/// </summary>
	public int HttpPort { get; set; } = 4202;

	/// <summary>
	/// Port a Prometheus scraper reads <c>/metrics</c> from, or 0 for none. It answers <c>/metrics</c> only, so it
	/// can be published to a monitoring network without exposing the WebSocket endpoint a second time.
	/// </summary>
	public int MetricsPort { get; set; } = 9091;

	/// <summary>
	/// Port for TLS-encrypted Telnet connections (PennMUSH's <c>ssl_port</c>), or 0 to open no such
	/// listener. Defaults to 0: opening it needs a certificate, and Kestrel fails to start rather
	/// than serving an endpoint without one, so it stays opt-in.
	/// <para>
	/// The certificate comes from Kestrel's own configuration — <c>Kestrel:Certificates:Default</c>
	/// in appsettings.json, or the ASPNETCORE_Kestrel__Certificates__Default__* environment
	/// variables — rather than a SharpMUSH-specific setting, so it is configured the same way as any
	/// other .NET service and can be rotated without a code change.
	/// </para>
	/// </summary>
	public int TelnetSslPort { get; set; } = 0;

	/// <summary>
	/// PEM certificate (leaf, then its intermediates) for <see cref="TelnetSslPort"/>, read from disk
	/// instead of Kestrel's default certificate. Set, the listener opens even before the file exists
	/// and refuses handshakes until it does, and a renewed file is picked up by the next handshake, so
	/// renewing drops nobody. Used by the one-click stack, where Caddy obtains the certificate.
	/// </summary>
	public string TelnetSslCertificatePath { get; set; } = "";

	/// <summary>The PEM private key for <see cref="TelnetSslCertificatePath"/>.</summary>
	public string TelnetSslKeyPath { get; set; } = "";

	/// <summary>
	/// Starting descriptor number for Telnet connections
	/// </summary>
	public long TelnetDescriptorStart { get; set; } = 0;

	/// <summary>
	/// Starting descriptor number for WebSocket connections
	/// </summary>
	public long WebSocketDescriptorStart { get; set; } = 1000000;

	/// <summary>
	/// Enable MXP (MUD eXtension Protocol) telnet negotiation.
	/// When true, the server offers MXP via telnet option 91.
	/// <para>
	/// Defaults to true: MXP is a negotiated telnet option, so a client that does not want it answers
	/// IAC DONT and sees nothing. (The Pueblo hello follows the game's <c>pueblo</c> option instead.)
	/// Leaving it off meant the MXP plugin was never registered at all, so the server never sent
	/// IAC WILL MXP and no client — however capable — could negotiate it.
	/// </para>
	/// Override via appsettings.json "ConnectionServer:MxpEnabled".
	/// The main server's NetOptions.Mxp (mushcnf) is a separate toggle for
	/// MXP feature handling at the application layer.
	/// </summary>
	public bool MxpEnabled { get; set; } = true;

	/// <summary>
	/// How long to wait for a client's answer to the <c>&lt;SUPPORT&gt;</c> question MXP mode opens with,
	/// in milliseconds. A client is under no obligation to reply, and an element it never answered about
	/// is one it does not get — so this is how long a connection waits before its first output can carry
	/// a sound or a picture.
	/// <para>
	/// Zero asks and does not wait: what the client has said by then, usually nothing, is recorded at
	/// once, so every element stays withheld until an answer arrives. Override via appsettings.json "ConnectionServer:MxpSupportTimeoutMilliseconds".
	/// </para>
	/// </summary>
	public int MxpSupportTimeoutMilliseconds { get; set; } = 2000;

	/// <summary>
	/// How long a new telnet connection waits, in milliseconds, for the client to report its terminal types and
	/// agree a character set before the game sends the connect screen, so the screen is drawn for what the
	/// client can show. A client that finishes sooner, or types a line, stops the wait; one that answers
	/// nothing waits all of it. Zero sends the connect screen at once. Override via appsettings.json
	/// "ConnectionServer:NegotiationWaitMilliseconds".
	/// </summary>
	public int NegotiationWaitMilliseconds { get; set; } = 1000;
}
