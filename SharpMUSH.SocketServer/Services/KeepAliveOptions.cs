namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Dead-connection detection tuning. <see cref="WsInterval"/>/<see cref="WsTimeout"/> drive the
/// WebSocket ping/pong abort; <see cref="TcpUserTimeout"/> drives OS-level half-open detection on both
/// listeners. Read from the <c>KeepAlive</c> config section with sensible defaults.
/// </summary>
public sealed record KeepAliveOptions(TimeSpan WsInterval, TimeSpan WsTimeout, TimeSpan TcpUserTimeout)
{
	public static KeepAliveOptions FromConfiguration(IConfiguration config) => new(
		WsInterval: TimeSpan.FromSeconds(config.GetValue("KeepAlive:WsIntervalSeconds", 15.0)),
		WsTimeout: TimeSpan.FromSeconds(config.GetValue("KeepAlive:WsTimeoutSeconds", 20.0)),
		TcpUserTimeout: TimeSpan.FromSeconds(config.GetValue("KeepAlive:TcpUserTimeoutSeconds", 20.0)));
}
