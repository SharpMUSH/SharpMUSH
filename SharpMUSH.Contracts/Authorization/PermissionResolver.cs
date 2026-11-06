using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Authorization;

/// <summary>
/// Everything that decides one holder's permissions, whether the holder is a game object, a
/// character played through an account, or an account in the portal: the roles it holds
/// (<c>everyone</c>, the implicit <c>player</c>, its object roles and its account's roles), the
/// overrides on its account and on the object itself, and whether it is the owner (player #1).
/// </summary>
/// <param name="Roles">Every role held, <c>everyone</c> included.</param>
/// <param name="Overrides">Account Allow/Deny by scope. Absent scopes are Inherit.</param>
/// <param name="IsOwner">True for player #1 and the account linked to it, which hold every scope.</param>
public sealed record PermissionContext(
	IReadOnlyCollection<SharpRole> Roles,
	IReadOnlyDictionary<string, PermissionState> Overrides,
	bool IsOwner)
{
	/// <summary>A context that grants nothing: no roles, no overrides, not the owner.</summary>
	public static readonly PermissionContext None = new([], new Dictionary<string, PermissionState>(), false);

	/// <summary>
	/// Allow/Deny set on the game object itself (PennMUSH's powers). They outrank account overrides,
	/// being the more specific setting. Absent scopes are Inherit.
	/// </summary>
	public IReadOnlyDictionary<string, PermissionState> ObjectOverrides { get; init; } = new Dictionary<string, PermissionState>();

	/// <summary>
	/// The reach of each object override that has one (<see cref="OverrideReach"/>), by scope. An override without an
	/// entry here reaches every target.
	/// </summary>
	public IReadOnlyDictionary<string, OverrideReach> ObjectReaches { get; init; } = OverrideReach.None;

	/// <summary>The reach of each account override that has one, by scope; see <see cref="ObjectReaches"/>.</summary>
	public IReadOnlyDictionary<string, OverrideReach> Reaches { get; init; } = OverrideReach.None;

	/// <summary>Whether any override has a reach, so a check against a target can differ from <see cref="IPermissionResolver.Resolve"/>.</summary>
	public bool HasReaches => ObjectReaches.Count > 0 || Reaches.Count > 0;

	/// <summary>
	/// The custom permissions the world defines (<see cref="CustomPermission"/>). They resolve like the
	/// built-in scopes; a scope that is neither is refused, so a role still naming a removed one grants nothing.
	/// </summary>
	public IReadOnlySet<string> CustomScopes { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// A <see cref="PortalPermission.ControlRole"/> for each role in the world, resolved with the built-in scopes. A
	/// context with none (an account's, in the portal) resolves no guard passes.
	/// </summary>
	public IReadOnlySet<string> ControlScopes { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	/// <summary>Whether <paramref name="scope"/> is a built-in permission or one of <see cref="CustomScopes"/>.</summary>
	public bool Knows(string scope) => PortalPermission.IsKnown(scope) || CustomScopes.Contains(scope);

	/// <summary>The highest priority among the held roles, or <see cref="int.MinValue"/> with none.</summary>
	public int TopPriority => Roles.Count == 0 ? int.MinValue : Roles.Max(r => r.Priority);
}

/// <summary>
/// Resolves the granted permission scopes for a <see cref="PermissionContext"/> with Discord's rules.
/// </summary>
public interface IPermissionResolver
{
	/// <summary>The scopes <paramref name="context"/> is granted.</summary>
	IReadOnlySet<string> Resolve(PermissionContext context);

	/// <summary>Whether <paramref name="context"/> holds <paramref name="scope"/>, and which layer decided it.</summary>
	PermissionExplanation Explain(PermissionContext context, string scope);

	/// <summary>
	/// Whether <paramref name="context"/> holds <paramref name="scope"/> against a target ranked
	/// <paramref name="targetRank"/> (<see cref="OverrideReach.RankOf"/>): <see cref="Resolve"/>'s answer, with each
	/// override that has a reach applied only when the target is within it.
	/// </summary>
	bool Allows(PermissionContext context, string scope, int targetRank);

	/// <summary><see cref="Explain(PermissionContext, string)"/> against a target ranked <paramref name="targetRank"/>.</summary>
	PermissionExplanation Explain(PermissionContext context, string scope, int targetRank);
}

/// <summary>
/// How far an override goes: the role whose priority bounds the targets it applies to. An Allow with a reach applies
/// to targets ranked at or below the role, so a power held at <c>councilor</c> acts on what holders of councilor or a
/// lower role own and never on a wizard's. A Deny with a reach applies to targets ranked at or above the role, so a
/// player denied <c>player.page</c> with reach <c>guildmaster</c> may page players and not staff. Outside its reach an
/// override is Inherit, and the layers below it decide. RhostMUSH's power and depower levels are reaches.
/// </summary>
/// <param name="Role">The slug of the role the reach names.</param>
/// <param name="Priority">That role's priority; <see cref="int.MinValue"/> when no role has the slug, so an Allow
/// reaches nothing and a Deny reaches everything.</param>
public sealed record OverrideReach(string Role, int Priority)
{
	/// <summary>No reaches.</summary>
	public static readonly IReadOnlyDictionary<string, OverrideReach> None = new Dictionary<string, OverrideReach>(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// The rank a check with no target is made against: a player holding no staff role. An Allow reaching any staff
	/// role applies, and a Deny reaching a staff role does not.
	/// </summary>
	public const int Unranked = (int)PortalRole.Player;

	/// <summary>
	/// A target's rank: the highest priority among the roles held, <c>everyone</c> left out, or
	/// <see cref="PortalRole.Guest"/>'s for none. The target is the owner of what a check acts on.
	/// </summary>
	public static int RankOf(IEnumerable<SharpRole> roles)
	{
		var rank = (int)PortalRole.Guest;
		foreach (var role in roles)
			if (!BuiltInRoles.IsEveryone(role) && role.Priority > rank) rank = role.Priority;
		return rank;
	}

	/// <summary>
	/// The reaches <paramref name="reaches"/> names (scope → role slug), each with the priority of its role in
	/// <paramref name="roles"/>.
	/// </summary>
	public static IReadOnlyDictionary<string, OverrideReach> Of(IReadOnlyDictionary<string, string> reaches, IEnumerable<SharpRole> roles)
	{
		if (reaches.Count == 0) return None;
		var priorities = roles.ToDictionary(r => r.Slug, r => r.Priority, StringComparer.OrdinalIgnoreCase);
		return reaches.ToDictionary(entry => entry.Key,
			entry => new OverrideReach(entry.Value.ToLowerInvariant(), priorities.TryGetValue(entry.Value, out var priority) ? priority : int.MinValue),
			StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>Whether an override in state <paramref name="state"/> with this reach applies to a target ranked <paramref name="rank"/>.</summary>
	public bool Covers(PermissionState state, int rank)
		=> state == PermissionState.Allow ? rank <= Priority : rank >= Priority;
}

/// <summary>
/// Discord's permission computation, with one scope at a time standing in for one permission bit:
/// <list type="number">
/// <item>The owner holds every scope.</item>
/// <item>A held role that allows <see cref="PortalPermission.Administrator"/> grants every scope but the pass
/// through God's guard (<see cref="PortalPermission.ControlRole"/> of <c>god</c>), which is the owner's alone, and
/// per-account overrides do not apply.</item>
/// <item>An override on the object decides the scope when it says Allow or Deny, then one on the
/// account (Discord's member overwrite). PennMUSH's powers are object overrides.</item>
/// <item>Otherwise any held role that allows the scope grants it, whatever its priority; failing that,
/// a role that denies it refuses it (Discord pools role denies, then role allows, so an Allow on any
/// role beats a Deny on another).</item>
/// <item>Otherwise the <c>everyone</c> role decides, and a scope nobody allows is denied.</item>
/// </list>
/// Priority plays no part in this; it only orders roles for who may manage whom
/// (see <see cref="RoleHierarchy"/>). A layer's opinion on a scope it leaves on Inherit is its opinion
/// on the umbrella scope that implies it (<c>wiki.admin</c> covers <c>wiki.edit</c>), so an explicit
/// child setting always beats the umbrella within the same layer.
/// </summary>
public sealed class PermissionResolver : IPermissionResolver
{
	/// <remarks>
	/// Every object's grants are resolved through here when they are first read and again after any role change,
	/// so it decides each scope as <see cref="Explain"/> does without building the explanation: no arrays, no LINQ.
	/// </remarks>
	public IReadOnlySet<string> Resolve(PermissionContext context)
	{
		var granted = new HashSet<string>(StringComparer.Ordinal);
		var holdsEverything = context.IsOwner || HoldsAdministrator(context.Roles);
		foreach (var scope in PortalPermission.AllScopes)
			if (holdsEverything || Allows(context, scope)) granted.Add(scope);
		foreach (var scope in context.CustomScopes)
			if (holdsEverything || Allows(context, scope)) granted.Add(scope);
		foreach (var scope in context.ControlScopes)
			if (context.IsOwner || (!OwnerOnly(scope) && (holdsEverything || Allows(context, scope))))
				granted.Add(scope);
		return granted;
	}

	private static readonly string GodGuard = PortalPermission.ControlRole(BuiltInRoles.GodSlug);

	/// <summary>The pass through God's guard: no role or override grants it, so nothing but #1 controls #1.</summary>
	private static bool OwnerOnly(string scope) => string.Equals(scope, GodGuard, StringComparison.OrdinalIgnoreCase);

	private static bool HoldsAdministrator(IReadOnlyCollection<SharpRole> roles)
	{
		foreach (var role in roles)
			if (StateOf(role.Permissions, PortalPermission.Administrator) == PermissionState.Allow) return true;
		return false;
	}

	public bool Allows(PermissionContext context, string scope, int targetRank)
	{
		if (!context.Knows(scope) || (PortalPermission.IsControlRole(scope) && !context.ControlScopes.Contains(scope))) return false;
		if (context.IsOwner) return true;
		if (OwnerOnly(scope)) return false;
		return HoldsAdministrator(context.Roles) || Below(context, scope, targetRank);
	}

	private static bool Allows(PermissionContext context, string scope) => Below(context, scope, OverrideReach.Unranked);

	/// <summary><see cref="Explain"/>'s verdict below the owner and administrator rules, for a scope the context knows.</summary>
	private static bool Below(PermissionContext context, string scope, int targetRank)
	{
		switch (OverrideStateOf(context.ObjectOverrides, context.ObjectReaches, scope, targetRank))
		{
			case PermissionState.Allow: return true;
			case PermissionState.Deny: return false;
		}

		switch (OverrideStateOf(context.Overrides, context.Reaches, scope, targetRank))
		{
			case PermissionState.Allow: return true;
			case PermissionState.Deny: return false;
		}

		var heldDenies = false;
		var everyoneAllows = false;
		foreach (var role in context.Roles)
		{
			var state = StateOf(role.Permissions, scope);
			if (BuiltInRoles.IsEveryone(role))
				everyoneAllows |= state == PermissionState.Allow;
			else if (state == PermissionState.Allow)
				return true;
			else
				heldDenies |= state == PermissionState.Deny;
		}

		return !heldDenies && everyoneAllows;
	}

	public PermissionExplanation Explain(PermissionContext context, string scope)
		=> Explain(context, scope, OverrideReach.Unranked);

	public PermissionExplanation Explain(PermissionContext context, string scope, int targetRank)
	{
		if (!context.Knows(scope) || (PortalPermission.IsControlRole(scope) && !context.ControlScopes.Contains(scope)))
			return new(false, null, [], "unknown-scope");
		if (context.IsOwner)
			return new(true, null, [], "owner");

		if (OwnerOnly(scope))
			return new(false, null, [], "owner-only");

		var administrators = context.Roles
			.Where(r => StateOf(r.Permissions, PortalPermission.Administrator) == PermissionState.Allow).ToArray();
		if (administrators.Length > 0)
			return Decided(true, administrators, "administrator");

		switch (OverrideStateOf(context.ObjectOverrides, context.ObjectReaches, scope, targetRank))
		{
			case PermissionState.Allow: return new(true, null, [], "object-allow") { Reach = ReachOf(context.ObjectReaches, scope) };
			case PermissionState.Deny: return new(false, null, [], "object-deny") { Reach = ReachOf(context.ObjectReaches, scope) };
		}

		switch (OverrideStateOf(context.Overrides, context.Reaches, scope, targetRank))
		{
			case PermissionState.Allow: return new(true, null, [], "account-allow") { Reach = ReachOf(context.Reaches, scope) };
			case PermissionState.Deny: return new(false, null, [], "account-deny") { Reach = ReachOf(context.Reaches, scope) };
		}

		var everyone = context.Roles.Where(BuiltInRoles.IsEveryone).ToArray();
		var held = context.Roles.Where(r => !BuiltInRoles.IsEveryone(r)).ToArray();
		var allowing = held.Where(r => StateOf(r.Permissions, scope) == PermissionState.Allow).ToArray();
		if (allowing.Length > 0)
			return Decided(true, allowing, "role-allow");
		var denying = held.Where(r => StateOf(r.Permissions, scope) == PermissionState.Deny).ToArray();
		if (denying.Length > 0)
			return Decided(false, denying, "role-deny");

		return everyone.Any(r => StateOf(r.Permissions, scope) == PermissionState.Allow)
			? Decided(true, everyone, "everyone-allow")
			: new(false, null, [], "default-deny");
	}

	/// <summary>
	/// One layer's stance on <paramref name="scope"/>: its explicit Allow/Deny, else its stance on an
	/// umbrella scope that implies it (Allow over Deny when two umbrellas disagree), else Inherit.
	/// </summary>
	public static PermissionState StateOf(IReadOnlyDictionary<string, PermissionState> permissions, string scope)
	{
		var explicitState = ExplicitState(permissions, scope);
		if (explicitState != PermissionState.Inherit) return explicitState;
		var denied = false;
		foreach (var parent in PortalPermission.ParentsOf(scope))
		{
			switch (StateOf(permissions, parent))
			{
				case PermissionState.Allow: return PermissionState.Allow;
				case PermissionState.Deny: denied = true; break;
			}
		}

		return denied ? PermissionState.Deny : PermissionState.Inherit;
	}

	/// <summary>
	/// An override layer's stance on <paramref name="scope"/> against a target ranked <paramref name="targetRank"/>:
	/// <see cref="StateOf"/>, with an override whose reach (<paramref name="reaches"/>) leaves the target out read as
	/// Inherit, so an umbrella override, or the next layer, decides instead.
	/// </summary>
	public static PermissionState OverrideStateOf(IReadOnlyDictionary<string, PermissionState> overrides,
		IReadOnlyDictionary<string, OverrideReach> reaches, string scope, int targetRank)
	{
		if (reaches.Count == 0) return StateOf(overrides, scope);
		var explicitState = ExplicitState(overrides, scope);
		if (explicitState != PermissionState.Inherit
			&& (!reaches.TryGetValue(scope, out var reach) || reach.Covers(explicitState, targetRank)))
			return explicitState;
		var denied = false;
		foreach (var parent in PortalPermission.ParentsOf(scope))
		{
			switch (OverrideStateOf(overrides, reaches, parent, targetRank))
			{
				case PermissionState.Allow: return PermissionState.Allow;
				case PermissionState.Deny: denied = true; break;
			}
		}

		return denied ? PermissionState.Deny : PermissionState.Inherit;
	}

	/// <summary>The role an override's reach names for <paramref name="scope"/>, or null for none.</summary>
	private static string? ReachOf(IReadOnlyDictionary<string, OverrideReach> reaches, string scope)
		=> reaches.TryGetValue(scope, out var reach) ? reach.Role : null;

	/// <summary>The first entry naming <paramref name="scope"/> in any case, or Inherit.</summary>
	private static PermissionState ExplicitState(IReadOnlyDictionary<string, PermissionState> permissions, string scope)
	{
		foreach (var (key, state) in permissions)
			if (string.Equals(key, scope, StringComparison.OrdinalIgnoreCase)) return state;
		return PermissionState.Inherit;
	}

	private static PermissionExplanation Decided(bool allowed, IReadOnlyCollection<SharpRole> roles, string reason)
		=> new(allowed, roles.Max(r => r.Priority), roles.Select(r => r.Slug).Order(StringComparer.Ordinal).ToArray(), reason);
}

/// <summary>
/// Why a scope was granted or refused. <paramref name="Priority"/> is the highest priority among the
/// deciding <paramref name="Roles"/>, null when no role decided. <paramref name="Reason"/> is one of
/// <c>owner</c>, <c>administrator</c>, <c>object-allow</c>, <c>object-deny</c>, <c>account-allow</c>,
/// <c>account-deny</c>, <c>role-allow</c>,
/// <c>role-deny</c>, <c>everyone-allow</c>, <c>default-deny</c>, <c>owner-only</c> or <c>unknown-scope</c>.
/// </summary>
public sealed record PermissionExplanation(bool Allowed, int? Priority, string[] Roles, string Reason)
{
	/// <summary>The role the deciding override's reach names, when an override with a reach decided.</summary>
	public string? Reach { get; init; }
}
