using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Authorization;

/// <summary>Where a held role comes from.</summary>
public enum RoleSource
{
	/// <summary>Held by being something: <c>everyone</c>, <c>player</c> for a player character, <c>god</c> for #1.</summary>
	Implicit,

	/// <summary>Assigned to the object itself (<c>@set</c>, <c>@power</c>, <c>@role/assign</c>).</summary>
	Object,

	/// <summary>Assigned to the account the character is linked to.</summary>
	Account
}

/// <summary>One role a holder has, and where it comes from.</summary>
public sealed record HeldRole(SharpRole Role, RoleSource Source);

/// <summary>An account's own roles and overrides, as they reach a character linked to it.</summary>
public sealed record AccountGrants(IReadOnlyCollection<SharpRole> Roles, IReadOnlyDictionary<string, PermissionState> Overrides);

/// <summary>
/// What one game object is granted: its roles and where each comes from, the overrides on it and on
/// its account, and the scopes they resolve to. The engine's privilege checks (<c>Wizard()</c>,
/// <c>haspower()</c>, control) read this rather than flags.
/// </summary>
/// <remarks>
/// Holding rules, in one place for every caller:
/// <list type="bullet">
/// <item>Every object holds <c>everyone</c>.</item>
/// <item>A player character holds <c>player</c> unless it holds <c>guest</c>.</item>
/// <item>An object holds the roles and overrides assigned to it.</item>
/// <item>A character linked to an active account also holds the account's roles and overrides; its
/// own overrides outrank the account's.</item>
/// <item>Player #1 holds <c>god</c> and is the owner.</item>
/// </list>
/// Nothing is inherited from an object's owner: TRUST and INHERIT decide control, not privilege, as in
/// PennMUSH.
/// </remarks>
public sealed class ObjectGrants
{
	private static readonly IPermissionResolver Resolver = new PermissionResolver();
	private readonly IReadOnlySet<string> _shown;

	private ObjectGrants(IReadOnlyList<HeldRole> roles, PermissionContext context)
	{
		Roles = roles;
		Context = context;
		Granted = Resolver.Resolve(context);
		// What a role or override allows by name: the owner's blanket grant and administrator roles left out.
		var named = context with
		{
			IsOwner = false,
			Roles = context.Roles.Where(r => PermissionResolver.StateOf(r.Permissions, PortalPermission.Administrator) != PermissionState.Allow).ToArray()
		};
		_shown = Resolver.Resolve(named);
	}

	/// <summary>Grants nothing: an object whose grants could not be read.</summary>
	public static readonly ObjectGrants None = new([], PermissionContext.None);

	/// <summary>Every role held, with its source. A role held two ways appears twice.</summary>
	public IReadOnlyList<HeldRole> Roles { get; }

	/// <summary>The context the scopes were resolved from.</summary>
	public PermissionContext Context { get; }

	/// <summary>Every scope granted.</summary>
	public IReadOnlySet<string> Granted { get; }

	/// <summary>True for player #1.</summary>
	public bool IsOwner => Context.IsOwner;

	/// <summary>Whether <paramref name="scope"/> is granted.</summary>
	public bool Has(string scope) => Granted.Contains(scope);

	/// <summary>
	/// The spelling <see cref="Has"/> expects for <paramref name="scope"/>, matched without regard to case:
	/// the catalog's for a built-in permission, lowercase for a custom one, null for neither.
	/// </summary>
	public string? Canonical(string scope)
		=> PortalPermission.Canonical(scope) ?? (Context.CustomScopes.Contains(scope) ? scope.ToLowerInvariant() : null);

	/// <summary>
	/// Whether a role or override allows <paramref name="scope"/> by name, leaving out the owner and
	/// <c>administrator</c> roles, which hold everything. This is what <c>hasflag()</c>, <c>haspower()</c>,
	/// <c>flags()</c> and <c>powers()</c> show: PennMUSH's God has no ROYALTY flag and no powers.
	/// </summary>
	public bool Shows(string scope) => _shown.Contains(scope);

	/// <summary>Whether a role with this slug is held, from any source.</summary>
	public bool HoldsRole(string slug) => Roles.Any(r => string.Equals(r.Role.Slug, slug, StringComparison.OrdinalIgnoreCase));

	/// <summary>Why <paramref name="scope"/> is granted or refused.</summary>
	public PermissionExplanation Explain(string scope) => Resolver.Explain(Context, scope);

	/// <summary>
	/// Grants for a context resolved elsewhere (an account in the portal), whose roles' sources are not
	/// known; each is reported as <see cref="RoleSource.Account"/>.
	/// </summary>
	public static ObjectGrants FromContext(PermissionContext context)
		=> new(context.Roles.Select(r => new HeldRole(r, RoleSource.Account)).ToArray(), context);

	/// <summary>Applies the holding rules (see the remarks on <see cref="ObjectGrants"/>).</summary>
	/// <param name="number">The object's dbref number.</param>
	/// <param name="isPlayer">Whether it is a player character.</param>
	/// <param name="roles">Every role in the world, to resolve slugs. A slug with no role is ignored.</param>
	/// <param name="objectRoles">The slugs assigned to the object.</param>
	/// <param name="objectOverrides">The overrides set on the object.</param>
	/// <param name="account">The linked account's grants, when the object is a character on an active account.</param>
	/// <param name="custom">The custom permissions the world defines.</param>
	public static ObjectGrants For(int number, bool isPlayer, IReadOnlyCollection<SharpRole> roles,
		IEnumerable<string> objectRoles, IReadOnlyDictionary<string, PermissionState> objectOverrides, AccountGrants? account,
		IEnumerable<CustomPermission> custom)
	{
		var bySlug = roles.ToDictionary(r => r.Slug, StringComparer.OrdinalIgnoreCase);
		var held = new List<HeldRole>();

		void Hold(string slug, RoleSource source)
		{
			if (bySlug.TryGetValue(slug, out var role)) held.Add(new HeldRole(role, source));
		}

		Hold(BuiltInRoles.EveryoneSlug, RoleSource.Implicit);
		foreach (var slug in objectRoles.Where(slug => !BuiltInRoles.IsImplicit(slug))) Hold(slug, RoleSource.Object);
		foreach (var role in account?.Roles.Where(r => !BuiltInRoles.IsImplicit(r.Slug)) ?? []) held.Add(new HeldRole(role, RoleSource.Account));
		var guest = held.Any(r => r.Role.Slug == BuiltInRoles.GuestSlug);
		if (isPlayer && !guest) Hold(BuiltInRoles.PlayerSlug, RoleSource.Implicit);
		var owner = number == 1;
		if (owner) Hold(BuiltInRoles.GodSlug, RoleSource.Implicit);

		var context = new PermissionContext(
			held.Select(r => r.Role).DistinctBy(r => r.Slug, StringComparer.OrdinalIgnoreCase).ToArray(),
			account?.Overrides ?? new Dictionary<string, PermissionState>(),
			owner)
		{
			ObjectOverrides = objectOverrides,
			CustomScopes = custom.Select(p => p.Scope).ToHashSet(StringComparer.OrdinalIgnoreCase)
		};
		return new ObjectGrants(held, context);
	}
}
