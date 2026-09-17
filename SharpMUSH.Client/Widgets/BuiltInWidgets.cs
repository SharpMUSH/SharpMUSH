using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Models.Widgets;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Widgets;

/// <summary>
/// Every widget the portal ships with, in one table.
/// </summary>
/// <remarks>
/// <para>This replaced thirteen sealed descriptor classes with no behaviour between them, and, more
/// to the point, thirteen <c>registry.Register(new …)</c> lines in <c>Program.cs</c> that a new
/// widget had to remember to join. The tests that check config documentation and resx coverage now
/// iterate <see cref="All"/> instead of their own hand-written arrays, so a widget added here is
/// registered and checked, and one that is not here is neither.</para>
///
/// <para>Deliberately a literal table rather than an assembly scan for a <c>[PortalWidget]</c>
/// attribute: this is a trimmed WASM bundle, and a scan would need the whole assembly's types kept
/// and would run at every startup. Every <c>typeof</c> here is a direct reference the trimmer can
/// follow.</para>
///
/// <para>Widget-kind Dynamic Applications are not here. They are discovered from the server at
/// startup and registered as <see cref="ApplicationPortalWidget"/>, and an unknown widget name
/// deliberately resolves as an application slug — see <c>WidgetRegistry.GetWidget</c>.</para>
/// </remarks>
public static class BuiltInWidgets
{
	private static readonly WidgetZone[] Content =
		[WidgetZone.MainContent, WidgetZone.LeftSidebar, WidgetZone.RightSidebar];

	private static readonly WidgetZone[] MainOnly = [WidgetZone.MainContent];

	/// <summary>One entry by machine name. Throws rather than returning null — a name that is not in
	/// the catalogue is a typo, not a runtime condition; unknown names at render time are
	/// <c>WidgetRegistry.GetWidget</c>'s business.</summary>
	public static PortalWidgetDescriptor Named(string name) =>
		All.FirstOrDefault(w => w.Name == name)
			?? throw new KeyNotFoundException($"No built-in widget is named '{name}'.");

	/// <summary>The catalogue, in palette order.</summary>
	public static IReadOnlyList<PortalWidgetDescriptor> All { get; } =
	[
		// Quick Links: a short link list, so it suits the chrome zones as well as the content ones.
		new("QuickLinks", "LayWidgetQuickLinks", WidgetSize.Small,
			[WidgetZone.TopBar, WidgetZone.LeftSidebar, WidgetZone.RightSidebar, WidgetZone.Footer],
			typeof(QuickLinksWidget), typeof(QuickLinksConfig)),

		new("WelcomeText", "LayWidgetWelcomeText", WidgetSize.Large, MainOnly,
			typeof(WelcomeTextWidget), typeof(WelcomeTextConfig)),

		// Lists every character with search and links to their profiles; powers /characters.
		new("CharacterDirectory", "LayWidgetCharacterDirectory", WidgetSize.Large, Content,
			typeof(CharacterDirectoryWidget)),

		// The character comes from the profile page context, or from the config when placed elsewhere.
		new("CharacterGallery", "LayWidgetCharacterGallery", WidgetSize.Medium,
			[WidgetZone.MainContent, WidgetZone.RightSidebar],
			typeof(CharacterGalleryWidget), typeof(CharacterTargetConfig)),

		// The wiki landing page (hero, client-side search, category grid); drives the "wiki-index" scope.
		new("WikiIndex", "LayWidgetWikiIndex", WidgetSize.Large, MainOnly, typeof(WikiIndexWidget)),

		// One wiki page inline. With no config it takes the character from the profile page context,
		// which is how it serves as the biography in the default "profile" layout.
		new("WikiBody", "LayWidgetWikiBody", WidgetSize.Large, Content,
			typeof(WikiBodyWidget), typeof(WikiBodyConfig)),

		// Reserved empty space. Width is the placement's column span, height is per-instance — which is
		// why the horizontal top bar is not an allowed zone.
		new("Spacer", "LayWidgetSpacer", WidgetSize.Small,
			[WidgetZone.MainContent, WidgetZone.LeftSidebar, WidgetZone.RightSidebar, WidgetZone.Footer],
			typeof(SpacerWidget), typeof(SpacerConfig)),

		new("Stats", "LayWidgetGameStats", WidgetSize.Large, MainOnly, typeof(StatsWidget)),
		new("ActiveScene", "LayWidgetActiveScene", WidgetSize.Medium, Content, typeof(ActiveSceneWidget)),
		new("RecentWikiActivity", "LayWidgetRecentWikiActivity", WidgetSize.Medium, Content,
			typeof(RecentWikiActivityWidget)),
		new("OnlineCharacters", "LayWidgetOnlineCharacters", WidgetSize.Medium, Content,
			typeof(OnlineCharactersWidget)),
		new("Quickstart", "LayWidgetQuickstart", WidgetSize.Medium, Content, typeof(QuickstartWidget)),

		// Schema-driven (Area 21): its config carries { schemaUrl, dataUrl } pointing at softcode
		// HTTP-handler routes, and it renders the returned Portal Schema Document.
		new("SchemaWidget", "LayWidgetSchemaApplication", WidgetSize.Medium, Content,
			typeof(SchemaWidget), typeof(SchemaWidgetConfig)),
	];
}
