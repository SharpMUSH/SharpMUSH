using Microsoft.Extensions.Localization;
using SharpMUSH.Client.Services;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Client.Resources;

/// <summary>
/// The portal's <see cref="SharedResource"/> strings, with <c>AppTitle</c> answered by the game's own name
/// (<c>mud_name</c>, through <see cref="ServerInfoService.KnownGameName"/>). Every page title, the home and wiki
/// headers and the sign-in and setup pages name the game through that one key, so a game renamed in
/// Configuration or with <c>@config/set mud_name</c> is renamed everywhere. The resource's own value is
/// what shows until the server has answered.
/// <para>
/// The guide's strings (<see cref="GuideNamedKeys"/>) name the game's own guide (<c>onboarding_guide_name</c>, through
/// <see cref="ServerInfoService.KnownGuideName"/>) where they say Sharpie. Every translation keeps Sharpie as written,
/// so the name is put in its place.
/// </para>
/// </summary>
public sealed class GameNamedLocalizer(IStringLocalizerFactory factory, ServerInfoService serverInfo)
	: IStringLocalizer<SharedResource>
{
	public const string AppTitleKey = "AppTitle";

	/// <summary>The player-facing strings that name the guide. The administrator's strings about Sharpie's artwork keep its name.</summary>
	public static readonly IReadOnlySet<string> GuideNamedKeys = new HashSet<string>(StringComparer.Ordinal)
	{
		"GuidePanelLabel", "GuideWithSharpie", "GuidePracticeResult", "GuideWelcomeIntro", "GuideArtwork",
		"GuideCompanionTitle", "GuideRecoveryTitle",
	};

	private readonly IStringLocalizer _resources = factory.Create(typeof(SharedResource));

	public LocalizedString this[string name] =>
		name == AppTitleKey && serverInfo.KnownGameName is { } game
			? new LocalizedString(name, game)
			: Guided(name, _resources[name]);

	public LocalizedString this[string name, params object[] arguments] =>
		name == AppTitleKey ? this[name]
		// The name goes into the format, not the formatted text, so an argument that says Sharpie is left as it is.
		: GuideNamedKeys.Contains(name) && serverInfo.KnownGuideName is not null && this[name] is { ResourceNotFound: false } format
			? new LocalizedString(name, string.Format(System.Globalization.CultureInfo.CurrentCulture, format.Value, arguments), false, format.SearchedLocation)
			: _resources[name, arguments];

	public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
		=> _resources.GetAllStrings(includeParentCultures);

	private LocalizedString Guided(string name, LocalizedString text) =>
		GuideNamedKeys.Contains(name) && serverInfo.KnownGuideName is { } guide
			? new LocalizedString(name, WithGuide(text.Value, guide), text.ResourceNotFound, text.SearchedLocation)
			: text;

	/// <summary><paramref name="text"/> naming <paramref name="guide"/> where it names Sharpie; as it is for an empty name.</summary>
	public static string WithGuide(string text, string? guide) =>
		string.IsNullOrWhiteSpace(guide) ? text : text.Replace(OnboardingGuide.DefaultName, guide.Trim(), StringComparison.Ordinal);
}
