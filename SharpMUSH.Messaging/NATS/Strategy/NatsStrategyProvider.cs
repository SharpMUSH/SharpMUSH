using System.Diagnostics.CodeAnalysis;

namespace SharpMUSH.Messaging.NATS.Strategy;

/// <summary>
/// Selects the appropriate <see cref="NatsStrategy"/> based on the runtime environment.
/// </summary>
/// <remarks>
/// Decision logic:
/// <list type="bullet">
///   <item><c>NATS_URL</c> is set → <see cref="NatsExternalStrategy"/> (connects to an existing server)</item>
///   <item><c>NATS_URL</c> is absent → <see cref="NatsTestContainerStrategy"/> (starts a local container)</item>
///   <item><c>NATS_URL</c> is absent in a project published ahead of time (its builds too, which run without
///   reflection-based JSON), which cannot run Testcontainers →
///   <see cref="NatsDockerStrategy"/> (starts the same container through Docker's API)</item>
/// </list>
/// </remarks>
public static class NatsStrategyProvider
{
	/// <summary>Where a NATS server runs on this machine by default.</summary>
	public const string LocalUrl = "nats://localhost:4222";

	/// <summary>
	/// Whether this process may start a NATS container through Testcontainers. A project published ahead of time turns it off
	/// (<c>RuntimeHostConfigurationOption</c> <c>SharpMUSH.Messaging.NatsTestContainers</c>), which leaves
	/// Testcontainers, a reflection-based library, out of the native binary.
	/// </summary>
	[FeatureSwitchDefinition(TestContainersSwitch)]
	[FeatureGuard(typeof(RequiresUnreferencedCodeAttribute))]
	[FeatureGuard(typeof(RequiresDynamicCodeAttribute))]
	[UnconditionalSuppressMessage("Trimming", "IL4000", Justification = "A feature switch: the trimmer reads its value, not this body.")]
	public static bool TestContainersSupported =>
		!AppContext.TryGetSwitch(TestContainersSwitch, out var enabled) || enabled;

	private const string TestContainersSwitch = "SharpMUSH.Messaging.NatsTestContainers";

	public static NatsStrategy GetStrategy()
	{
		var natsUrl = Environment.GetEnvironmentVariable("NATS_URL");

		if (!string.IsNullOrWhiteSpace(natsUrl)) return new NatsExternalStrategy(natsUrl);
		return TestContainersSupported ? new NatsTestContainerStrategy() : new NatsDockerStrategy();
	}
}
