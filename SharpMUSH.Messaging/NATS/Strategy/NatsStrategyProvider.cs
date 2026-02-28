namespace SharpMUSH.Messaging.NATS.Strategy;

/// <summary>
/// Selects the appropriate <see cref="NatsStrategy"/> based on the runtime environment.
/// </summary>
/// <remarks>
/// Decision logic:
/// <list type="bullet">
///   <item><c>NATS_URL</c> is set → <see cref="NatsExternalStrategy"/> (connects to an existing server)</item>
///   <item><c>NATS_URL</c> is absent → <see cref="NatsTestContainerStrategy"/> (starts a local container)</item>
/// </list>
/// </remarks>
public static class NatsStrategyProvider
{
	public static NatsStrategy GetStrategy()
	{
		var natsUrl = Environment.GetEnvironmentVariable("NATS_URL");

		return string.IsNullOrWhiteSpace(natsUrl)
			? new NatsTestContainerStrategy()
			: new NatsExternalStrategy(natsUrl);
	}
}
