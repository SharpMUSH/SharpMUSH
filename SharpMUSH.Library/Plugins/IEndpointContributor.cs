using Microsoft.AspNetCore.Routing;

namespace SharpMUSH.Library.Plugins;

/// <summary>
/// Phase 9 contribution surface for the ASP.NET endpoint pipeline. A plugin entry type may implement
/// this to map its own endpoints — SignalR hubs, minimal-API routes, or anything an
/// <see cref="IEndpointRouteBuilder"/> exposes — into the host's request pipeline.
///
/// <para>This is the pipeline analog of <see cref="IServiceRegistrar"/>: the registrar runs during
/// <c>ConfigureServices</c> (pre-build, from the <c>PluginCatalog</c>), whereas
/// <see cref="MapEndpoints"/> runs during <c>ConfigureApp</c> (post-build), after the host has mapped
/// its own controllers and hubs. The host invokes every collected contributor in load order, each
/// isolated so a single failing plugin cannot abort endpoint mapping for the rest.</para>
///
/// <para>A plugin that contributes controllers does so the standard ASP.NET way from its
/// <see cref="IServiceRegistrar.RegisterServices"/> —
/// <c>services.AddControllers().AddApplicationPart(thisAssembly)</c> — so it needs no new seam for
/// MVC. This seam exists for everything that is mapped (rather than registered): hubs and routes.</para>
/// </summary>
public interface IEndpointContributor
{
	/// <summary>
	/// Map this plugin's endpoints into the host pipeline. Called once, post-build, with the same
	/// <see cref="IEndpointRouteBuilder"/> the host maps its own controllers/hubs into.
	/// </summary>
	void MapEndpoints(IEndpointRouteBuilder endpoints);
}
