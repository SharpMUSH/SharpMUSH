using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Plugins;

namespace HelloUiPlugin;

/// <summary>
/// A minimal end-to-end <b>UI plugin</b> example, shipped as a Phase-4 <c>kind: managed</c> package.
///
/// <para>It wires together three plugin seams in one assembly:</para>
/// <list type="number">
///   <item><b><see cref="IServiceRegistrar"/></b> — registers this assembly as an MVC ApplicationPart
///   (<c>AddControllers().AddApplicationPart(thisAssembly)</c>) so the host discovers
///   <see cref="Web.HelloUiController"/> across the plugin's AssemblyLoadContext. This is the Phase-9
///   controller seam.</item>
///   <item><b><see cref="IApplicationSource"/></b> — contributes a single full-page Area-21
///   <see cref="RegisteredApplication"/> whose <c>SchemaUrl</c>/<c>DataUrl</c> point at this plugin's own
///   controller routes, placed in a novel NavBar section ("Examples"). This is the Phase-11 portal-UI seam.</item>
///   <item><b><see cref="IEndpointContributor"/></b> — left unused here: the controller's attribute routes
///   (<c>[Route("api/hello-ui")]</c>) are mapped by the host's own <c>MapControllers()</c> once the
///   ApplicationPart is registered, so no manual endpoint mapping is needed. (Implement it only if you map
///   a SignalR hub or minimal-API route, like the Scene plugin does.)</item>
/// </list>
///
/// <para>Because it contributes an <see cref="IServiceRegistrar"/> and an <see cref="IApplicationSource"/>,
/// this plugin is <b>load-once</b> (a mapped/registered web surface cannot be unmapped at runtime), so it is
/// picked up at server boot and a clean uninstall takes effect on the next restart.</para>
///
/// <para>The browser never loads any code from this assembly: the WASM client renders the app generically
/// from the schema JSON the controller serves. That is what lets a UI plugin ship entirely inside its own
/// DLL — descriptor (here), schema/data endpoints (the controller), NavBar placement (the descriptor's
/// <c>NavPlacement</c>) — all without a browser-loaded assembly.</para>
/// </summary>
[SharpPlugin]
public sealed class HelloUiPlugin : PluginBase, IServiceRegistrar, IApplicationSource
{
	public override string Id => "hello-ui";
	public override string Version => "1.0.0";

	/// <summary>The slug of the page app this plugin contributes; the page renders at <c>/apps/hello-ui</c>.</summary>
	public const string AppSlug = "hello-ui";

	/// <summary>The novel NavBar section the app declares — <c>NavMenu.razor</c> renders a new data-driven group.</summary>
	public const string NavSection = "Examples";

	/// <summary>
	/// Phase 9 — expose this assembly's <see cref="Web.HelloUiController"/> to the host's MVC pipeline.
	/// <c>AddApplicationPart</c> is the "FromAssembly" load that lets controller discovery cross the plugin ALC.
	/// </summary>
	public void RegisterServices(IServiceCollection services) =>
		services.AddControllers().AddApplicationPart(typeof(HelloUiPlugin).Assembly);

	/// <summary>
	/// Phase 11 — one read-only full-page Area-21 application, overlaid onto the registry while this plugin
	/// is loaded. Its schema/data routes resolve to this plugin's own controller; the client downloads the
	/// schema, renders a read-only view, and shows a NavBar link under "Examples".
	/// </summary>
	public IEnumerable<RegisteredApplication> GetApplications() =>
	[
		new RegisteredApplication(
			Slug: AppSlug,
			DisplayName: "Hello UI",
			Icon: "WavingHand",
			Kind: ApplicationKind.Page,
			// Relative routes; the client base-joins them onto the API host. They map onto this plugin's
			// own controller (api/hello-ui/schema and api/hello-ui/data).
			SchemaUrl: "api/hello-ui/schema",
			DataUrl: "api/hello-ui/data",
			SubmitRoute: null,
			MinimumRole: PortalRole.Guest,
			NavPlacement: NavSection,
			Zones: null,
			Order: 10,
			OwningPackage: AppSlug)
	];
}
