namespace SharpMUSH.Library.Models.Portal.Onboarding;

public sealed record GuideLink(string Title, string Url);

/// <summary>Only information safe for an anonymous visitor. A missing projection means unknown, not enabled.</summary>
public sealed record OnboardingPresentation(string GameId, string Introduction, string Genre, string Subgenre,
	string ArtStyle, string ArtOverrideId, GuideLink? Welcome, GuideLink? Rules, GuideLink? CharacterGuide);

public sealed record OnboardingSettings(string MudName, string[] Genres, string[] Subgenres,
	string Introduction, string WelcomePage, string RulesPage, string CharacterGuidePage, string ArtStyle, string ArtOverrideId);

/// <summary>Package-specific commands are absent until an adapter proves both installed version and character access.</summary>
public sealed record GuideCapability(string Id, string Command, string Audience, string? HelpUrl, string PackageId, string InstalledVersion);
