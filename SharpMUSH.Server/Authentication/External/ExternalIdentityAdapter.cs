using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
namespace SharpMUSH.Server.Authentication.External;

public sealed record ExternalIdentityProof(ExternalIdentityKey Key, string? Label);
/// <summary>Profile fields are display metadata only. Only validated subject and issuer identify an account.</summary>
public static class ExternalIdentityAdapter
{
	private static bool Bounded(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);
	private static string? Label(string? value) => Bounded(value, 128) ? value : null;
	public static Result<ExternalIdentityProof> OAuth(EffectiveExternalProvider provider, JsonElement profile)
	{
		var github = provider.Definition.Id == "github";
		var subjectField = provider.Definition.Id == "grapevine" ? "uid" : "id";
		if (profile.ValueKind != JsonValueKind.Object || !profile.TryGetProperty(subjectField, out var id))
			return new Error<string>("The provider returned no valid account identifier.");
		// GitHub's user id is a JSON number; the others send a string.
		var subject = (github, id.ValueKind) switch
		{
			(true, JsonValueKind.Number) when id.TryGetInt64(out var number) && number > 0 => number.ToString(CultureInfo.InvariantCulture),
			(false, JsonValueKind.String) => id.GetString(),
			_ => null
		};
		if (!Bounded(subject, 512))
			return new Error<string>("The provider returned no valid account identifier.");
		var label = profile.TryGetProperty(github ? "login" : "username", out var name) && name.ValueKind == JsonValueKind.String ? Label(name.GetString()) : null;
		return new ExternalIdentityProof(new(provider.Settings.RegistrationId, provider.Definition.Issuer, subject!), label);
	}
	public static Result<ExternalIdentityProof> OpenId(EffectiveExternalProvider provider, ClaimsPrincipal principal)
	{
		var subject = principal.FindAll("sub").Select(c => c.Value).ToArray();
		var issuers = principal.FindAll("iss").Select(c => c.Value).ToArray();
		if (subject.Length != 1 || issuers.Length != 1 || !Bounded(subject[0], 512) || !Bounded(issuers[0], 512)) return new Error<string>("The provider returned no valid account identifier.");
		var issuer = issuers[0];
		if (!provider.Definition.OpenIdConnect && issuer != provider.Definition.Issuer) return new Error<string>("Untrusted provider issuer.");
		if (provider.Definition.Id == "google")
		{
			// Google documents both issuer spellings for one namespace and stable sub. Normalize only these exact aliases.
			if (issuer is not ("https://accounts.google.com" or "accounts.google.com")) return new Error<string>("Untrusted provider issuer.");
			issuer = "https://accounts.google.com";
		}
		if (provider.Definition.Id == "microsoft")
		{
			var tenants = principal.FindAll("tid").Select(c => c.Value).ToArray();
			if (tenants.Length != 1 || !Guid.TryParseExact(tenants[0], "D", out var tid) || issuer != $"https://login.microsoftonline.com/{tid:D}/v2.0") return new Error<string>("Untrusted Microsoft tenant issuer.");
			var tenant = tid.ToString("D");
			const string personal = "9188040d-6c67-4c5b-b112-36a304b66dad";
			var audience = provider.Settings.MicrosoftAudience;
			if ((audience == "organizations" && tenant == personal) || (audience == "consumers" && tenant != personal)
			 || (Guid.TryParseExact(audience, "D", out var audienceTenant) && audienceTenant != tid)
			 || (provider.Settings.AllowedTenants.Length > 0 && !provider.Settings.AllowedTenants.Contains(tenant, StringComparer.OrdinalIgnoreCase))) return new Error<string>("Microsoft tenant is not permitted.");
		}
		return new ExternalIdentityProof(new(provider.Settings.RegistrationId, issuer, subject[0]), Label(principal.FindFirst("name")?.Value));
	}
}
