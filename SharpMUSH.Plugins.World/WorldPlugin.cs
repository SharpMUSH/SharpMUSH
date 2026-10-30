using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.World.Storage;

namespace SharpMUSH.Plugins.World;

/// <summary>
/// Stored maps: places painted on a grid instead of one room each. The plugin keeps them in its own Lightning tables
/// (<see cref="WorldStore"/>), surfaces <c>@world</c> and the <c>world…</c> functions, hands the World map their
/// places (<see cref="WorldMapSource"/>), and forgets a map's places when its object is freed. It registers services,
/// so it is not unloadable.
/// </summary>
[SharpPlugin]
public sealed class WorldPlugin : PluginBase, IServiceRegistrar
{
	public override string Id => "world";
	public override string Version => "1.0.0";

	public void RegisterServices(IServiceCollection services)
	{
		services.AddSingleton(sp => new WorldStore(sp.GetRequiredService<ILightningStorageAccessor>()));
		services.AddSingleton<IWorldMapSource>(sp => new WorldMapSource(sp.GetRequiredService<WorldStore>(),
			sp.GetRequiredService<IAttributeService>(), sp.GetRequiredService<IPermissionService>()));
		services.AddSingleton<IFreedObjectCleanup>(sp => sp.GetRequiredService<WorldStore>());
	}
}
