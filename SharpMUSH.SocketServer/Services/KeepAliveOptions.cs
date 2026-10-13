namespace SharpMUSH.SocketServer.Services;

/// <summary>
/// Dead-connection detection tuning. <see cref="WsInterval"/>/<see cref="WsTimeout"/> drive the
/// WebSocket ping/pong abort; the <c>Tcp*</c> values drive OS-level half-open detection on both
/// listeners. Read from the <c>KeepAlive</c> config section with sensible defaults.
/// </summary>
/// <remarks>
/// <para>
/// The TCP values are one budget, not four independent knobs. A quiet connection is probed after
/// <see cref="TcpIdle"/>, then every <see cref="TcpProbeInterval"/>, and is meant to survive until
/// <see cref="TcpProbeCount"/> probes in a row have gone unanswered: <see cref="TcpProbeBudget"/>.
/// </para>
/// <para>
/// On Linux, setting <c>TCP_USER_TIMEOUT</c> replaces the probe count. The kernel resets the socket
/// as soon as nothing has arrived for the user timeout and one probe is outstanding, so a user
/// timeout at or below <see cref="TcpIdle"/> drops an idle player on a single lost probe or ACK.
/// <see cref="TcpUserTimeout"/> therefore defaults to the probe budget and may not be set below it.
/// Zero leaves <c>TCP_USER_TIMEOUT</c> off, and the probe count alone decides.
/// </para>
/// </remarks>
public sealed record KeepAliveOptions(
	TimeSpan WsInterval,
	TimeSpan WsTimeout,
	TimeSpan TcpUserTimeout,
	TimeSpan TcpIdle,
	TimeSpan TcpProbeInterval,
	int TcpProbeCount)
{
	/// <summary>How long a silent peer is tolerated before the keep-alive probes give up on it.</summary>
	public TimeSpan TcpProbeBudget => TcpIdle + TcpProbeInterval * TcpProbeCount;

	public static KeepAliveOptions FromConfiguration(IConfiguration config)
	{
		var idle = TimeSpan.FromSeconds(ReadWholeNumber(config, "TcpIdleSeconds", 30));
		var probeInterval = TimeSpan.FromSeconds(ReadWholeNumber(config, "TcpProbeIntervalSeconds", 10));
		var probeCount = ReadWholeNumber(config, "TcpProbeCount", 6);
		var budget = idle + probeInterval * probeCount;

		var userTimeout = ReadSeconds(config, "TcpUserTimeoutSeconds", budget.TotalSeconds);
		if (userTimeout > TimeSpan.Zero && userTimeout < budget)
			throw new ArgumentOutOfRangeException("KeepAlive:TcpUserTimeoutSeconds", userTimeout.TotalSeconds,
				$"Must be 0 (off) or at least TcpIdleSeconds + TcpProbeIntervalSeconds x TcpProbeCount ({budget.TotalSeconds}s): " +
				"a shorter TCP_USER_TIMEOUT resets idle connections before the keep-alive probes have run.");

		return new(
			WsInterval: ReadSeconds(config, "WsIntervalSeconds", 15),
			WsTimeout: ReadSeconds(config, "WsTimeoutSeconds", 20),
			TcpUserTimeout: userTimeout,
			TcpIdle: idle,
			TcpProbeInterval: probeInterval,
			TcpProbeCount: probeCount);
	}

	private static TimeSpan ReadSeconds(IConfiguration config, string name, double fallback)
	{
		var key = $"KeepAlive:{name}";
		var seconds = config.GetValue(key, fallback);
		if (!double.IsFinite(seconds) || seconds < 0 || seconds > int.MaxValue / 1000.0)
			throw new ArgumentOutOfRangeException(key, seconds, "Keep-alive seconds must be finite and between 0 and 2147483.647.");
		return TimeSpan.FromSeconds(seconds);
	}

	// The probe timings are whole-second socket options, and zero is not "off" for any of them.
	private static int ReadWholeNumber(IConfiguration config, string name, int fallback)
	{
		var key = $"KeepAlive:{name}";
		var value = config.GetValue(key, fallback);
		if (value is < 1 or > 32767)
			throw new ArgumentOutOfRangeException(key, value, "Keep-alive probe settings must be whole numbers between 1 and 32767.");
		return value;
	}
}
