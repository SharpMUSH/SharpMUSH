using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Server.Authentication.External;

public static class ExternalProviderValidation
{
	public static Result<ExternalProviderSettings> Validate(string provider, ExternalProviderSettings settings, bool development)
	{
		if (settings.ClientId is null || settings.PublicOrigin is null || settings.MicrosoftAudience is null || settings.AllowedTenants is null
	 || settings.AllowedTenants.Any(tenant => tenant is null)) return new Error<string>("Sign-in settings cannot contain null fields.");
		if (!ExternalProviderDefinition.IsKnown(provider)) return new Error<string>("Unknown sign-in provider.");
		if (settings.ClientId.Length > 256 || settings.ClientId.Any(char.IsControl))
			return new Error<string>("Client id must be at most 256 characters without control characters.");
		if (settings.Enabled && string.IsNullOrWhiteSpace(settings.ClientId))
			return new Error<string>("An enabled sign-in provider requires a client id.");
		var origin = settings.PublicOrigin;
		if (origin.Length > 0 || settings.Enabled)
		{
			if (origin.Contains('\\') || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
				|| !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
				|| uri.AbsolutePath != "/" || (uri.Scheme != Uri.UriSchemeHttps
					&& !(development && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
				return new Error<string>("Public origin must be an HTTPS origin without user information, path, query or fragment.");
			origin = uri.GetLeftPart(UriPartial.Authority);
		}
		if (provider == "microsoft" && settings.MicrosoftAudience is not ("common" or "organizations" or "consumers")
			&& !Guid.TryParseExact(settings.MicrosoftAudience, "D", out _))
			return new Error<string>("Microsoft audience must be common, organizations, consumers or an explicit tenant UUID.");
		if (settings.AllowedTenants.Length > 100 || settings.AllowedTenants.Any(tenant => !Guid.TryParseExact(tenant, "D", out _)))
			return new Error<string>("Allowed Microsoft tenants must be UUIDs (at most 100).");
		return settings with { PublicOrigin = origin, AllowedTenants = settings.AllowedTenants.Select(tenant => tenant.ToLowerInvariant()).Distinct().ToArray() };
	}
}
