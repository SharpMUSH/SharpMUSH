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
}
