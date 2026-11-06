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
	/// The overrides on the object and on its account, <c>+</c> for Allow and <c>-</c> for Deny, each with its reach
	/// after a slash (<see cref="WithReach"/>). An Allow on a power's scope with no reach is left out: the Powers line
	/// already shows it. Empty when there are none.
	/// </summary>
	public static string Overrides(ObjectGrants grants)
		=> string.Join(' ', Format(grants.Context.ObjectOverrides, grants.Context.ObjectReaches, "")
			.Concat(Format(grants.Context.Overrides, grants.Context.Reaches, "(account)")));

	/// <summary><paramref name="scope"/>, followed by a slash and the role its override's reach names when it has one.</summary>
	public static string WithReach(string scope, IReadOnlyDictionary<string, OverrideReach> reaches)
		=> reaches.TryGetValue(scope, out var reach) ? $"{scope}/{reach.Role}" : scope;

	private static IEnumerable<string> Format(IReadOnlyDictionary<string, PermissionState> overrides,
		IReadOnlyDictionary<string, OverrideReach> reaches, string suffix)
		=> overrides
			.Where(o => o.Value != PermissionState.Inherit)
			.Where(o => !(o.Value == PermissionState.Allow && GamePowers.ForScope(o.Key) is not null && !reaches.ContainsKey(o.Key)))
			.OrderBy(o => o.Key, StringComparer.Ordinal)
			.Select(o => $"{(o.Value == PermissionState.Allow ? '+' : '-')}{WithReach(o.Key, reaches)}{suffix}");
}
