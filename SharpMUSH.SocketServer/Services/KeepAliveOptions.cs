namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Dead-connection detection tuning. <see cref="WsInterval"/>/<see cref="WsTimeout"/> drive the
/// WebSocket ping/pong abort; <see cref="TcpUserTimeout"/> drives OS-level half-open detection on both
/// listeners. Read from the <c>KeepAlive</c> config section with sensible defaults.
/// </summary>
public sealed record KeepAliveOptions(TimeSpan WsInterval, TimeSpan WsTimeout, TimeSpan TcpUserTimeout)
{
	public static KeepAliveOptions FromConfiguration(IConfiguration config) => new(
		WsInterval: ReadSeconds(config, "WsIntervalSeconds", 15),
		WsTimeout: ReadSeconds(config, "WsTimeoutSeconds", 20),
		TcpUserTimeout: ReadSeconds(config, "TcpUserTimeoutSeconds", 20));

	private static TimeSpan ReadSeconds(IConfiguration config, string name, double fallback)
	{
		var key = $"KeepAlive:{name}";
		var seconds = config.GetValue(key, fallback);
		if (!double.IsFinite(seconds) || seconds < 0 || seconds > int.MaxValue / 1000.0)
			throw new ArgumentOutOfRangeException(key, seconds, "Keep-alive seconds must be finite and between 0 and 2147483.647.");
		return TimeSpan.FromSeconds(seconds);
	}
}
