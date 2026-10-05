namespace SharpMUSH.Library.Authorization;

/// <summary>How <c>examine</c> lists an object's roles and overrides.</summary>
public static class ObjectGrantsDisplay
{
	/// <summary>
	/// The roles held beyond the implicit ones, each marked with where it comes from when that is not
	/// the object itself: <c>wizard moderator(account)</c>. Empty when there are none.
	/// </summary>
	public static string Roles(ObjectGrants grants)
		=> string.Join(' ', grants.Roles
			.Where(held => held.Source != RoleSource.Implicit)
			.Select(held => held.Source == RoleSource.Account ? $"{held.Role.Slug}(account)" : held.Role.Slug));

	/// <summary>
	/// The overrides on the object and on its account, <c>+</c> for Allow and <c>-</c> for Deny. An
	/// Allow on a power's scope is left out: the Powers line already shows it. Empty when there are none.
	/// </summary>
	public static string Overrides(ObjectGrants grants)
		=> string.Join(' ', Format(grants.Context.ObjectOverrides, "").Concat(Format(grants.Context.Overrides, "(account)")));

	private static IEnumerable<string> Format(IReadOnlyDictionary<string, PermissionState> overrides, string suffix)
		=> overrides
			.Where(o => o.Value != PermissionState.Inherit)
			.Where(o => !(o.Value == PermissionState.Allow && GamePowers.ForScope(o.Key) is not null))
			.OrderBy(o => o.Key, StringComparer.Ordinal)
			.Select(o => $"{(o.Value == PermissionState.Allow ? '+' : '-')}{o.Key}{suffix}");
}
