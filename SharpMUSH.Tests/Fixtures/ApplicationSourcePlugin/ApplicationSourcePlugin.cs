using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;

namespace ApplicationSourcePlugin;

/// <summary>
/// Fixture plugin that contributes UI via <see cref="IApplicationSource"/> — the seam under test. It returns
/// one full-page Area-21 <see cref="RegisteredApplication"/> whose schema/data routes point at its own
/// (notional) controller, and it also defines a command so that the integration test can prove the
/// load-once verdict: a plugin that is otherwise command-only (an unloadable shape) becomes <b>load-once</b>
/// the moment it also contributes applications.
/// </summary>
[SharpPlugin]
public sealed class ApplicationSourcePlugin : PluginBase, IApplicationSource
{
	public override string Id => "app-source";
	public override string Version => "1.0.0";

	/// <summary>The slug of the page app this fixture contributes (asserted by the overlay tests).</summary>
	public const string AppSlug = "plugin-widget-demo";

	/// <summary>The novel NavPlacement section this fixture's app declares (asserted by the nav tests).</summary>
	public const string NavSection = "Plugins";

	/// <summary>IApplicationSource: one full-page app overlaid onto the registry while this plugin is loaded.</summary>
	public IEnumerable<RegisteredApplication> GetApplications() =>
	[
		new RegisteredApplication(
			Slug: AppSlug,
			DisplayName: "Plugin Widget Demo",
			Icon: "Extension",
			Kind: ApplicationKind.Page,
			SchemaUrl: "http/plugin-demo/schema",
			DataUrl: "http/plugin-demo/data",
			SubmitRoute: null,
			MinimumRole: PortalRole.Player,
			NavPlacement: NavSection,
			Zones: null,
			Order: 50,
			OwningPackage: null)
	];

	[SharpCommand(Name = "+APPSOURCE", MinArgs = 0, MaxArgs = 0)]
	public static ValueTask<Option<CallState>> AppSource(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> ValueTask.FromResult(new Option<CallState>(new CallState("application-source plugin says hello")));
}
