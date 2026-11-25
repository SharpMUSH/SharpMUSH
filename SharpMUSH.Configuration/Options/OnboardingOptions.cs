namespace SharpMUSH.Configuration.Options;

/// <summary>Optional game-authored context for the player guide; identity remains in Net, Cosmetic and MSSP.</summary>
public sealed record OnboardingOptions
{
	[SharpConfig(Name = "onboarding_introduction", Category = "Onboarding", Description = "Plain-text introduction shown to new players")]
	public string Introduction { get; init; } = "";
	[SharpConfig(Name = "onboarding_welcome_page", Category = "Onboarding", Description = "Welcome wiki page as namespace/slug")]
	public string WelcomePage { get; init; } = "";
	[SharpConfig(Name = "onboarding_rules_page", Category = "Onboarding", Description = "Rules wiki page as namespace/slug")]
	public string RulesPage { get; init; } = "";
	[SharpConfig(Name = "onboarding_character_guide", Category = "Onboarding", Description = "Character guide wiki page as namespace/slug")]
	public string CharacterGuidePage { get; init; } = "";
	[SharpConfig(Name = "onboarding_art_style", Category = "Onboarding", Description = "Sharpie artwork family")]
	public string ArtStyle { get; init; } = "minimalist";
	[SharpConfig(Name = "onboarding_art_override", Category = "Onboarding", Description = "Reviewed Sharpie artwork identifier, or empty for the game genre")]
	public string ArtOverrideId { get; init; } = "";
	[SharpConfig(Name = "onboarding_guide_name", Category = "Onboarding", Description = "The guide's name; empty for Sharpie",
		ValidationPattern = OnboardingGuide.NamePattern, Example = "Drakie",
		Tooltip = "At most 40 characters. The guide introduces itself by this name and the portal's guide labels use it.")]
	public string GuideName { get; init; } = "";
	[SharpConfig(Name = "onboarding_guide_art", Category = "Onboarding", Description = "The guide's picture; empty for Sharpie drawn in onboarding_art_style",
		ValidationPattern = PortalPicture.Pattern, Image = true, Example = "https://example.com/guide.png",
		Tooltip = "A media upload (/api/wiki-assets/...) or an http(s) address, shown square and as it is everywhere the "
			+ "guide appears: its lessons, the welcome page and, when portal_sharpie is on, Home.")]
	public string GuideArt { get; init; } = "";
}

/// <summary>What <c>onboarding_guide_name</c> and <c>onboarding_guide_art</c> hold, and what the portal shows when they are empty.</summary>
public static class OnboardingGuide
{
	/// <summary>The guide's own name, which the portal's guide strings carry.</summary>
	public const string DefaultName = "Sharpie";

	/// <summary>At most 40 characters, none of them control characters.</summary>
	public const string NamePattern = @"^[^\p{C}]{0,40}$";

	/// <summary><c>onboarding_guide_name</c>, or <see cref="DefaultName"/> when it is empty.</summary>
	public static string Name(OnboardingOptions? onboarding) =>
		string.IsNullOrWhiteSpace(onboarding?.GuideName) ? DefaultName : onboarding.GuideName.Trim();

	/// <summary><c>onboarding_guide_art</c>, or empty when the guide is Sharpie.</summary>
	public static string Art(OnboardingOptions? onboarding) => onboarding?.GuideArt?.Trim() ?? "";
}
