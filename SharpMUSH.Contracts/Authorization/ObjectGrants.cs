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
public sealed record AccountGrants(IReadOnlyCollection<SharpRole> Roles, IReadOnlyDictionary<string, PermissionState> Overrides)
{
	/// <summary>The reach of each account override that has one: scope → role slug (<see cref="OverrideReach"/>).</summary>
	public IReadOnlyDictionary<string, string> Reaches { get; init; } = new Dictionary<string, string>();
}

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

	private readonly IReadOnlyDictionary<string, string> _customCategories;

	private ObjectGrants(IReadOnlyList<HeldRole> roles, PermissionContext context,
		IReadOnlyDictionary<string, string>? customCategories = null)
	{
		Roles = roles;
		Context = context;
		_customCategories = customCategories ?? new Dictionary<string, string>();
		Granted = Resolver.Resolve(context);
		_named = Named(context);
		_shown = Resolver.Resolve(_named);
	}

	private ObjectGrants(ObjectGrants capped, IReadOnlySet<string> granted, IReadOnlySet<string> shown,
		Func<string, int, bool> ranked, Func<string, int, bool> rankedShown)
	{
		Roles = capped.Roles;
		Context = capped.Context with { IsOwner = false };
		_customCategories = capped._customCategories;
		Granted = granted;
		_shown = shown;
		_named = Named(Context);
		_ranked = ranked;
		_rankedShown = rankedShown;
	}

	/// <summary>What a role or override allows by name: the owner's blanket grant and administrator roles left out.</summary>
	private static PermissionContext Named(PermissionContext context) => context with
	{
		IsOwner = false,
		Roles = context.Roles.Where(r => PermissionResolver.StateOf(r.Permissions, PortalPermission.Administrator) != PermissionState.Allow).ToArray()
	};

	private readonly PermissionContext _named;

	/// <summary>
	/// Capped by <see cref="Within"/> or <see cref="AtMost"/>: <see cref="Granted"/> is no longer what the context alone
	/// resolves to, so a check against a target asks the cap at the target's rank. Null when not capped.
	/// </summary>
	private readonly Func<string, int, bool>? _ranked;

	private readonly Func<string, int, bool>? _rankedShown;

	/// <summary>
	/// These grants as an app holding <paramref name="scopes"/> sees them while it acts as the object: only the
	/// permissions the scopes cover (<see cref="OAuth.DelegatedScopes.Covers"/>), and not the owner unless they cover
	/// every group.
	/// </summary>
	public ObjectGrants Within(IReadOnlyCollection<string> scopes)
		=> IsOwner && OAuth.DelegatedScopes.HasEveryGroup(scopes)
			? this
			: new ObjectGrants(this, OAuth.DelegatedScopes.Cap(Granted, scopes, _customCategories),
				OAuth.DelegatedScopes.Cap(_shown, scopes, _customCategories),
				(scope, rank) => Covered(scopes, scope) && Has(scope, rank),
				(scope, rank) => Covered(scopes, scope) && Shows(scope, rank));

	private bool Covered(IReadOnlyCollection<string> scopes, string permission)
		=> OAuth.DelegatedScopes.Covers(scopes, permission, _customCategories.GetValueOrDefault(permission));

	/// <summary>
	/// These grants under a privilege ceiling at <paramref name="priority"/>: only what is granted both now and by the
	/// roles at or below that priority, with the overrides that allow dropped and not the owner. A ceiling at or above
	/// every role held changes nothing but the owner's blanket grant and the allowing overrides.
	/// </summary>
	public ObjectGrants AtMost(int priority)
	{
		var context = Context with
		{
			IsOwner = false,
			Roles = Context.Roles.Where(role => BuiltInRoles.IsEveryone(role) || role.Priority <= priority).ToArray(),
			Overrides = Denials(Context.Overrides),
			ObjectOverrides = Denials(Context.ObjectOverrides)
		};
		var lowered = new ObjectGrants(Roles.Where(held => context.Roles.Contains(held.Role)).ToArray(), context, _customCategories);
		return new ObjectGrants(lowered, Granted.Where(lowered.Granted.Contains).ToHashSet(StringComparer.Ordinal),
			_shown.Where(lowered._shown.Contains).ToHashSet(StringComparer.Ordinal),
			(scope, rank) => Has(scope, rank) && lowered.Has(scope, rank),
			(scope, rank) => Shows(scope, rank) && lowered.Shows(scope, rank));
	}

	/// <summary>The reaches of the overrides that are set; a reach left behind by a cleared override is ignored.</summary>
	private static IReadOnlyDictionary<string, string> Reached(IReadOnlyDictionary<string, string>? reaches,
		IReadOnlyDictionary<string, PermissionState>? overrides)
		=> reaches is not { Count: > 0 } || overrides is null
			? new Dictionary<string, string>()
			: reaches.Where(entry => overrides.Keys.Contains(entry.Key, StringComparer.OrdinalIgnoreCase))
				.ToDictionary(StringComparer.OrdinalIgnoreCase);

	private static Dictionary<string, PermissionState> Denials(IReadOnlyDictionary<string, PermissionState> overrides)
		=> overrides.Where(entry => entry.Value == PermissionState.Deny).ToDictionary(StringComparer.OrdinalIgnoreCase);

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
	/// Whether <paramref name="scope"/> is granted against <paramref name="target"/>, the owner of what the check acts
	/// on: <see cref="Has(string)"/>, with each override that has a reach (<see cref="OverrideReach"/>) applied only when
	/// the target's <see cref="Rank"/> is within it.
	/// </summary>
	public bool Has(string scope, ObjectGrants target) => Has(scope, target.Rank);

	/// <summary><see cref="Has(string, ObjectGrants)"/> against a target ranked <paramref name="targetRank"/>.</summary>
	public bool Has(string scope, int targetRank)
		=> !Context.HasReaches
			? Has(scope)
			: _ranked?.Invoke(scope, targetRank) ?? Resolver.Allows(Context, scope, targetRank);

	/// <summary><see cref="Shows(string)"/> against <paramref name="target"/>; see <see cref="Has(string, ObjectGrants)"/>.</summary>
	public bool Shows(string scope, ObjectGrants target) => Shows(scope, target.Rank);

	/// <summary><see cref="Shows(string)"/> against a target ranked <paramref name="targetRank"/>.</summary>
	public bool Shows(string scope, int targetRank)
		=> !Context.HasReaches
			? Shows(scope)
			: _rankedShown?.Invoke(scope, targetRank) ?? Resolver.Allows(_named, scope, targetRank);

	/// <summary>
	/// This holder's rank as the target of a check (<see cref="OverrideReach.RankOf"/>): the highest priority among
	/// its roles, <c>everyone</c> left out.
	/// </summary>
	public int Rank => _rank ??= OverrideReach.RankOf(Context.Roles);

	private int? _rank;

	/// <summary>
	/// The slugs of the held roles that guard their holders (<see cref="PortalPermission.ProtectRole"/>), each read on
	/// the role itself: an override on the object or the account neither adds a guard nor lifts one.
	/// </summary>
	public IEnumerable<string> GuardedRoles => Guarded;

	// Worked out once: every control check asks it of the target, and the roles never change.
	private string[] Guarded => _guarded ??= Roles
		.Where(held => PermissionResolver.StateOf(held.Role.Permissions, PortalPermission.ProtectRole) == PermissionState.Allow)
		.Select(held => held.Role.Slug)
		.Distinct(StringComparer.OrdinalIgnoreCase)
		.ToArray();

	private string[]? _guarded;

	/// <summary>
	/// Whether these grants pass every guard <paramref name="target"/>'s roles set: a
	/// <see cref="PortalPermission.ControlRole"/> for each of its <see cref="GuardedRoles"/>.
	/// </summary>
	public bool PassesGuardsOf(ObjectGrants target)
	{
		foreach (var slug in target.Guarded)
		{
			if (!Has(PortalPermission.ControlRole(slug))) return false;
		}

		return true;
	}

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

	/// <summary>Why <paramref name="scope"/> is granted or refused against a target ranked <paramref name="targetRank"/>.</summary>
	public PermissionExplanation Explain(string scope, int targetRank) => Resolver.Explain(Context, scope, targetRank);

	/// <summary>
	/// Whether a protective scope (<c>game.noforce</c>, <c>game.noboot</c>) shields this holder from an actor ranked
	/// <paramref name="actorRank"/>. Held through an override with a reach, it shields from actors the reach covers, as
	/// RhostMUSH's NOFORCE at a level stops forcers ranked at or below it. Held otherwise, it shields only from actors
	/// ranked below this holder, so peers keep what they had.
	/// </summary>
	public bool Protects(string scope, int actorRank)
		=> Explain(scope, actorRank) is { Allowed: true } why && (why.Reach is not null || actorRank < Rank);

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
	/// <param name="objectReaches">The reach of each object override that has one: scope → role slug.</param>
	public static ObjectGrants For(int number, bool isPlayer, IReadOnlyCollection<SharpRole> roles,
		IEnumerable<string> objectRoles, IReadOnlyDictionary<string, PermissionState> objectOverrides, AccountGrants? account,
		IEnumerable<CustomPermission> custom, IReadOnlyDictionary<string, string>? objectReaches = null)
	{
		var defined = custom.DistinctBy(p => p.Scope, StringComparer.OrdinalIgnoreCase).ToArray();
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
			ObjectReaches = OverrideReach.Of(Reached(objectReaches, objectOverrides), roles),
			Reaches = OverrideReach.Of(Reached(account?.Reaches, account?.Overrides), roles),
			CustomScopes = defined.Select(p => p.Scope).ToHashSet(StringComparer.OrdinalIgnoreCase),
			ControlScopes = roles.Select(r => PortalPermission.ControlRole(r.Slug)).ToHashSet(StringComparer.OrdinalIgnoreCase)
		};
		return new ObjectGrants(held, context,
			defined.ToDictionary(p => p.Scope, p => p.Category, StringComparer.OrdinalIgnoreCase));
	}
}
