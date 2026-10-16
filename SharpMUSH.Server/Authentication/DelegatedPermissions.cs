using Microsoft.AspNetCore.Http;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.OAuth;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Authentication;

/// <summary>
/// What an app's token may use in the portal's API: the permissions the character it acts as holds, read fresh, that
/// the token's scopes cover (<see cref="DelegatedScopes.Cap"/>). Read once per request.
/// </summary>
public sealed class DelegatedPermissions(
	IAdministrativeCapabilityService capabilities,
	IRoleRegistryService registry,
	IHttpContextAccessor httpContextAccessor)
{
	private static readonly object ItemsKey = new();

	public async Task<IReadOnlySet<string>> GrantedAsync(string accountId, DelegatedApp app)
	{
		var items = httpContextAccessor.HttpContext?.Items;
		if (items?.TryGetValue(ItemsKey, out var memo) == true && memo is Granted known && known.AccountId == accountId
				&& known.ClientId == app.ClientId && known.Character == app.Character.Number)
			return known.Scopes;

		var held = await capabilities.GetGrantedScopesAsync(new(accountId, app.Character, app.Character));
		var categories = (await registry.GetCustomPermissionsAsync())
			.DistinctBy(permission => permission.Scope, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(permission => permission.Scope, permission => permission.Category, StringComparer.OrdinalIgnoreCase);
		var scopes = DelegatedScopes.Cap(held, app.Scopes, categories);
		if (items is not null) items[ItemsKey] = new Granted(accountId, app.ClientId, app.Character.Number, scopes);
		return scopes;
	}

	private sealed record Granted(string AccountId, string ClientId, int Character, IReadOnlySet<string> Scopes);
}
