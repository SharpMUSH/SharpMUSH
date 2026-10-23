using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models.Portal.Onboarding;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Portal.Setup;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

public sealed class OnboardingPresentationService(IOptionsWrapper<SharpMUSHOptions> options, IMediator mediator,
	IWikiService wiki, IWikiAccessService access, IGameFeatureReader features, ILogger<OnboardingPresentationService> logger)
{
	public async Task<OnboardingPresentation?> PublicAsync(CancellationToken ct)
	{
		try
		{
			var wikiEnabled = (await features.EnabledAsync()).Contains(GameFeatures.WikiReader);
			var current = options.CurrentValue;
			var guide = current.Onboarding ?? new();
			var gameId = await mediator.Send(new GetOnboardingGameIdQuery(), ct);
			var reader = wikiEnabled ? await access.AnonymousAsync(ct) : null;
			return new(gameId, guide.Introduction, First(current, "GENRE"), First(current, "SUBGENRE"),
				guide.ArtStyle, guide.ArtOverrideId, await LinkAsync(guide.WelcomePage, reader),
				await LinkAsync(guide.RulesPage, reader), await LinkAsync(guide.CharacterGuidePage, reader));
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
		catch (Exception error)
		{
			logger.LogWarning(error, "Could not project public onboarding information");
			return null;
		}
	}

	private async Task<GuideLink?> LinkAsync(string target, WikiReader? reader)
	{
		if (reader is null || !TryPage(target, out var ns, out var slug)) return null;
		if (await wiki.GetBySlugAsync(slug, ns) is not WikiPage page || !await access.CanSeeAsync(reader, page)) return null;
		return new(page.Title, $"/wiki/{WikiHelpers.NamespaceName(ns)}/{Uri.EscapeDataString(page.Slug)}");
	}

	internal static bool TryPage(string? target, out WikiNamespace ns, out string slug)
	{
		ns = WikiNamespace.Main;
		slug = "";
		if (string.IsNullOrEmpty(target) || target.Length > 256 || target.Any(char.IsControl)) return false;
		var slash = target.IndexOf('/');
		if (slash <= 0 || WikiHelpers.ParseNamespace(target[..slash]) is not { } parsed
			|| int.TryParse(target[..slash], out _) || slash == target.Length - 1) return false;
		slug = target[(slash + 1)..];
		if (slug.IndexOfAny(['/', '\\', '?', '#', '%']) >= 0 || slug is "." or "..") return false;
		ns = parsed;
		return true;
	}

	private static string First(SharpMUSHOptions options, string key)
		=> options.Mssp.Variables.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
			.Value?.FirstOrDefault() ?? "";
}
