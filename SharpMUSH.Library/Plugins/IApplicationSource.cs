using SharpMUSH.Library.Models.Portal.Applications;

namespace SharpMUSH.Library.Plugins;

/// <summary>
/// Contribution surface for portal UI. A plugin entry type may implement this to contribute one or more
/// Area-21 <see cref="RegisteredApplication"/>(s) — full-page apps at <c>/apps/{slug}</c> or placeable
/// widgets — whose schema/data routes point at the plugin's own controller (registered the standard ASP.NET
/// way via <c>services.AddControllers().AddApplicationPart(thisAssembly)</c> from
/// <see cref="IServiceRegistrar"/>).
///
/// <para>The pre-build <c>PluginCatalog</c> collects every source into its <c>ApplicationSources</c> bucket;
/// a registry-overlay decorator unions <see cref="GetApplications"/> into the DB-backed application registry
/// as a <b>read-only overlay</b> present only while the plugin is loaded. Plugin apps are never persisted and
/// not admin-editable; a slug that collides with a DB/built-in record is skipped (the DB record wins).</para>
///
/// <para>Like the other portal/web seams (<see cref="IEndpointContributor"/>), contributing applications is a
/// <b>load-once</b> seam: a plugin that contributes UI is not hot-unloadable, because the overlay it backs is
/// part of the built pipeline's view of the registry.</para>
/// </summary>
public interface IApplicationSource
{
	/// <summary>The portal applications this plugin contributes (read-only overlay; not persisted).</summary>
	IEnumerable<RegisteredApplication> GetApplications();
}
