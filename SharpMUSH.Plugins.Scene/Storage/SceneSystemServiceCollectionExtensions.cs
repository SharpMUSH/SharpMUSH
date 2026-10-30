using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Scene.Storage;

/// <summary>
/// ASP.NET-style, behavior-extensible registration for the Scene System's storage, owned by the Scene
/// plugin. <c>ScenePlugin.RegisterServices</c> calls <see cref="AddSceneSystem"/>.
/// </summary>
public static class SceneSystemServiceCollectionExtensions
{
	/// <summary>
	/// Registers the Lightning-backed <see cref="ISceneStorage"/>, then registers <see cref="ISceneService"/>
	/// via a factory that wraps that storage with the registered behaviors IN ORDER.
	/// Returns an <see cref="ISceneSystemBuilder"/> for chaining <c>.AddBehavior&lt;T&gt;()</c>.
	/// </summary>
	public static ISceneSystemBuilder AddSceneSystem(this IServiceCollection services)
	{
		// A factory lambda rather than an implementation type, so the host's ValidateOnBuild does not
		// require the accessor before the provider has registered it.
		services.AddSingleton(sp => new LightningSceneStorage(sp.GetRequiredService<ILightningStorageAccessor>()));
		services.AddSingleton<ISceneStorage>(sp => sp.GetRequiredService<LightningSceneStorage>());

		// Drafts and calendar links: what the portal keeps for a character, apart from the scenes.
		services.AddSingleton<IScenePortalStore>(sp => new LightningScenePortalStore(sp.GetRequiredService<ILightningStorageAccessor>()));

		// The two kinds of history the Scene System keeps, measured and bounded by the host's retention
		// pass alongside the provider's own (see IHistoryRetentionService).
		services.AddSingleton<IHistoryStore>(sp => sp.GetRequiredService<LightningSceneStorage>().EditHistory);
		services.AddSingleton<IHistoryStore>(sp => sp.GetRequiredService<LightningSceneStorage>().DeletedPoseHistory);

		// Whether a web reader holds scene.view, for the REST controllers and the live hub.
		services.AddSingleton<Web.ISceneReadAccess, Web.SceneReadAccess>();

		// Scenes, poses and casts name objects by number; a freed object is cleared from them before its number names another.
		services.AddSingleton<IFreedObjectCleanup>(sp => sp.GetRequiredService<LightningSceneStorage>());

		var builder = new SceneSystemBuilder(services);
		services.AddSingleton<ISceneSystemBuilder>(builder);

		services.AddSingleton<ISceneService>(sp =>
		{
			ISceneService chain = sp.GetRequiredService<ISceneStorage>();

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
}
