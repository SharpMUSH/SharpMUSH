using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// <c>Startup.ConfigureServices</c> is eleven calls into <c>Registration/</c>. The failure mode that
/// introduces is a band silently dropping out of the list — the host then builds, boots, and is
/// missing a whole subsystem, which surfaces as an unrelated resolution failure somewhere downstream.
/// One marker service per band catches that here instead.
/// </summary>
/// <remarks>
/// Registration only, never resolution: this asserts on the <see cref="ServiceDescriptor"/> list
/// without building a provider, so nothing opens a database, dials NATS or loads a plugin assembly.
/// </remarks>
public class StartupRegistrationTests
{
	private static IServiceCollection Configure()
	{
		var services = new ServiceCollection();
		var environment = Substitute.For<IHostEnvironment>();
		environment.EnvironmentName.Returns(Environments.Production);

		new Startup(colorFile: "colors.json", natsUrl: "nats://localhost:4222")
			.ConfigureServices(services, new ConfigurationBuilder().Build(), environment);

		return services;
	}

	private static bool Registers<T>(IServiceCollection services) =>
		services.Any(d => d.ServiceType == typeof(T));

	[Test]
	[Arguments(typeof(IConfigureOptions<ForwardedHeadersOptions>), "HTTP pipeline")]
	[Arguments(typeof(SharpMUSH.Implementation.Services.PluginCatalog), "database")]
	[Arguments(typeof(IPermissionService), "engine")]
	[Arguments(typeof(IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>), "options")]
	[Arguments(typeof(SharpMUSH.Messaging.Abstractions.IMessageBus), "messaging")]
	[Arguments(typeof(IAdministrativeCapabilityService), "auth")]
	[Arguments(typeof(IAuthorizationPolicyProvider), "web API")]
	[Arguments(typeof(IPluginManager), "plugins")]
	public async Task EveryRegistrationBandIsReachedFromConfigureServices(Type marker, string band)
	{
		var services = Configure();

		await Assert.That(services.Any(d => d.ServiceType == marker))
			.IsTrue()
			.Because($"the {band} band registers {marker.Name}");
	}

	/// <summary>
	/// The hosted services are their own band and all share one service type, so a marker type cannot
	/// tell a missing band from a present one. Count them instead.
	/// </summary>
	[Test]
	public async Task TheHostedServiceBandIsReached()
	{
		var services = Configure();

		await Assert.That(services.Count(d => d.ServiceType == typeof(IHostedService)))
			.IsGreaterThanOrEqualTo(15);
	}

	/// <summary>
	/// The compiled boolean-lock expression cache is keyed, and the key is shared with
	/// <c>BooleanExpressionParser</c>'s <c>[FromKeyedServices]</c>. A keyed registration is invisible
	/// to a plain service-type lookup, so it gets its own assertion.
	/// </summary>
	[Test]
	public async Task TheCompiledExpressionCacheKeepsItsKey()
	{
		var services = Configure();

		await Assert.That(services.Any(d =>
				d.IsKeyedService && Equals(d.ServiceKey, Startup.CompiledExpressionsCacheName)))
			.IsTrue();
	}

	/// <summary>
	/// The one thing the split could get wrong without failing to build: a band registered twice,
	/// because the call was left in <c>ConfigureServices</c> as well as folded into another band.
	/// Singletons with a concrete implementation type are the ones where a duplicate matters —
	/// two registrations of the same pair mean two instances behind two interfaces.
	/// </summary>
	[Test]
	public async Task NoServiceIsRegisteredTwiceWithTheSameImplementation()
	{
		var duplicates = Configure()
			.Where(d => d is { Lifetime: ServiceLifetime.Singleton, ImplementationType: not null })
			.GroupBy(d => (d.ServiceType, d.ImplementationType, d.ServiceKey))
			.Where(g => g.Count() > 1)
			.Select(g => $"{g.Key.ServiceType.Name} -> {g.Key.ImplementationType!.Name}")
			.ToList();

		await Assert.That(duplicates).IsEmpty();
	}
}
