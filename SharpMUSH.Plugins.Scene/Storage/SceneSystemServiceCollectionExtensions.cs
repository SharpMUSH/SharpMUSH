using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Scene.Storage;

/// <summary>
/// ASP.NET-style, config-driven, behavior-extensible registration for the Scene System's storage,
/// owned by the Scene plugin. <c>ScenePlugin.RegisterServices</c> calls <see cref="AddSceneSystem"/>.
/// </summary>
public static class SceneSystemServiceCollectionExtensions
{
	/// <summary>Provider keys for the keyed <see cref="ISceneStorage"/> registrations.</summary>
	public const string ArangoKey = "arangodb";
	public const string MemgraphKey = "memgraph";
	public const string SurrealKey = "surrealdb";

	private const string ProviderConfigKey = "SHARPMUSH_DATABASE_PROVIDER";

	/// <summary>
	/// Registers each provider's storage as a KEYED <see cref="ISceneStorage"/>, then registers
	/// <see cref="ISceneService"/> via a factory that resolves the storage matching the active provider
	/// (read from configuration / environment) and wraps it with the registered behaviors IN ORDER.
	/// Returns an <see cref="ISceneSystemBuilder"/> for chaining <c>.AddBehavior&lt;T&gt;()</c>.
	/// </summary>
	public static ISceneSystemBuilder AddSceneSystem(this IServiceCollection services, IConfiguration configuration)
	{
		// Keyed storage cores — each lazily resolves its own host-shared accessor, so only the active
		// provider's accessor must actually be registered (the others are never resolved).
		services.AddKeyedSingleton<ISceneStorage, ArangoSceneStorage>(ArangoKey);
		services.AddKeyedSingleton<ISceneStorage, MemgraphSceneStorage>(MemgraphKey);
		services.AddKeyedSingleton<ISceneStorage, SurrealSceneStorage>(SurrealKey);

		var builder = new SceneSystemBuilder(services);
		services.AddSingleton<ISceneSystemBuilder>(builder);

		services.AddSingleton<ISceneService>(sp =>
		{
			var config = sp.GetService<IConfiguration>() ?? configuration;
			var key = ResolveProviderKey(config);
			ISceneService chain = sp.GetRequiredKeyedService<ISceneStorage>(key);

			// Hand-rolled decoration (no Scrutor): wrap the storage core with each behavior so the
			// last-registered behavior is the outermost. The behavior receives the next ISceneService in
			// the chain as its first constructor argument; further ctor params resolve from DI.
			var behaviorBuilder = (SceneSystemBuilder)sp.GetRequiredService<ISceneSystemBuilder>();
			foreach (var behaviorType in behaviorBuilder.BehaviorTypes)
			{
				chain = (ISceneService)ActivatorUtilities.CreateInstance(sp, behaviorType, chain);
			}

			return chain;
		});

		return builder;
	}

	/// <summary>
	/// Maps the configured provider name to a storage key. Reads <c>SHARPMUSH_DATABASE_PROVIDER</c> from
	/// configuration first, then the process environment, defaulting to ArangoDB (same precedence the host
	/// uses to pick <c>ISharpDatabase</c>).
	/// </summary>
	private static string ResolveProviderKey(IConfiguration? configuration)
	{
		var provider = configuration?[ProviderConfigKey]
		               ?? Environment.GetEnvironmentVariable(ProviderConfigKey);

		return provider switch
		{
			var p when string.Equals(p, "memgraph", StringComparison.OrdinalIgnoreCase) => MemgraphKey,
			var p when string.Equals(p, "surrealdb", StringComparison.OrdinalIgnoreCase) => SurrealKey,
			_ => ArangoKey
		};
	}
}
