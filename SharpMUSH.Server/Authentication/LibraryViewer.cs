using System.Security.Claims;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Server.Authentication;

/// <summary>A caller of the shared image library, as <see cref="Library.Models.Wiki.WikiAssetLibrary"/> judges them.</summary>
/// <param name="Scopes">Their permissions, account-wide as for every portal gate.</param>
/// <param name="Dbref">The character they are playing, which is what an upload records as its uploader.</param>
public sealed record LibraryViewer(IReadOnlySet<string> Scopes, string? Dbref)
{
	public static async Task<LibraryViewer> ResolveAsync(ClaimsPrincipal user, IAdministrativeCapabilityService capabilities, CancellationToken ct)
	{
		var dbref = user.GetActingCharacter()?.ToString();
		if (!AccountSessionAuthenticationHandler.TryGetAccount(user, out var accountId, out _))
			return new(new HashSet<string>(), dbref);
		var granted = await capabilities.GetGrantedScopesAsync(new CapabilityActor(accountId), ct);
		return new(granted.ToHashSet(StringComparer.OrdinalIgnoreCase), dbref);
	}
}
