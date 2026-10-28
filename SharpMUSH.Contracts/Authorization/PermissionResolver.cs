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

	/// <summary><see cref="Explain"/>'s verdict below the owner and administrator rules, for a scope the context knows.</summary>
	private static bool Allows(PermissionContext context, string scope)
	{
		switch (StateOf(context.ObjectOverrides, scope))
		{
			case PermissionState.Allow: return true;
			case PermissionState.Deny: return false;
		}

		switch (StateOf(context.Overrides, scope))
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
	{
		if (!context.Knows(scope))
			return new(false, null, [], "unknown-scope");
		if (context.IsOwner)
			return new(true, null, [], "owner");

		if (OwnerOnly(scope))
			return new(false, null, [], "owner-only");

		var administrators = context.Roles
			.Where(r => StateOf(r.Permissions, PortalPermission.Administrator) == PermissionState.Allow).ToArray();
		if (administrators.Length > 0)
			return Decided(true, administrators, "administrator");

		switch (StateOf(context.ObjectOverrides, scope))
		{
			case PermissionState.Allow: return new(true, null, [], "object-allow");
			case PermissionState.Deny: return new(false, null, [], "object-deny");
		}

		switch (StateOf(context.Overrides, scope))
		{
			case PermissionState.Allow: return new(true, null, [], "account-allow");
			case PermissionState.Deny: return new(false, null, [], "account-deny");
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
public sealed record PermissionExplanation(bool Allowed, int? Priority, string[] Roles, string Reason);
