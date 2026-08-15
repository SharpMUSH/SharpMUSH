using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Models.Widgets;

/// <summary>
/// Config schema for the Wiki Body widget: which wiki page it renders.
/// <para>
/// A page's identity is (namespace, category, slug), so <see cref="Slug"/> alone addresses
/// <c>main:general:{slug}</c> and the other two narrow it. An explicit page always wins over the
/// cascading profile context, which is what lets a fixed page sit on a character profile.
/// </para>
/// </summary>
/// <param name="Slug">
/// Page slug. Set this to render an arbitrary wiki page. Unset, the widget falls back to
/// <see cref="Character"/> and then to the profile page context.
/// </param>
/// <param name="Namespace">Wiki namespace; null means the Main namespace.</param>
/// <param name="Category">Category segment; null means <c>general</c>.</param>
/// <param name="Locale">Locale to render; null means the reader's stored preference.</param>
/// <param name="Character">
/// Shorthand for a character biography — equivalent to <see cref="Slug"/> = the name with
/// <see cref="Namespace"/> = <c>character</c>. Ignored when <see cref="Slug"/> is set.
/// </param>
public record WikiBodyConfig(
	[property: WidgetConfigKey("LayCfgWikiBodySlug")]
	string? Slug = null,
	[property: WidgetConfigKey("LayCfgWikiBodyNamespace", Default = "\"main\"")]
	string? Namespace = null,
	[property: WidgetConfigKey("LayCfgWikiBodyCategory", Default = "\"general\"")]
	string? Category = null,
	[property: WidgetConfigKey("LayCfgWikiBodyLocale")]
	string? Locale = null,
	[property: WidgetConfigKey("LayCfgWikiBodyCharacter")]
	string? Character = null);
